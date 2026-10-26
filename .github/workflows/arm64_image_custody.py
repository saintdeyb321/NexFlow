"""CI-only Docker archive custody. No Docker command runs on import or in unit tests."""

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys


MIB = 1024 * 1024
RESERVE = MIB  # Manifest, checksum and ZIP metadata allowance, outside the tar limit.
BUNDLE = Path('artifacts/api-image')
EVIDENCE = Path('artifacts/arm64')
PROOF = Path('artifacts/api-import/import-verification.json')


def approved_limit(requested, confirmed, budget):
    if requested == 'false':
        return None
    if requested != 'true' or confirmed != 'true' or not re.fullmatch(r'[0-9]+', budget):
        raise ValueError('Image retention requires explicit quota confirmation and a byte budget.')
    amount = int(budget)
    if not 2 <= amount <= 512:
        raise ValueError('Approved payload budget must be between 2 and 512 MiB.')
    return amount * MIB


def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate_manifest(data, source, image_id, archive_hash, limit):
    if not re.fullmatch(r'[0-9a-f]{40}', source) or not re.fullmatch(r'sha256:[0-9a-f]{64}', image_id):
        raise ValueError('Invalid source or image identity.')
    if not re.fullmatch(r'[0-9a-f]{64}', archive_hash):
        raise ValueError('Invalid archive checksum.')
    if data.get('format') != 'docker-save-tar-v1' or data.get('source_sha') != source or data.get('image_id') != image_id:
        raise ValueError('Archive identity differs from the successful source job.')
    if data.get('platform') != 'linux/arm64' or data.get('uid') != 1654 or data.get('gid') != 1654 or data.get('oci_revision') != source:
        raise ValueError('Archive platform, user or revision mismatch.')
    archive = data.get('archive', {})
    size = archive.get('size_bytes')
    if archive.get('name') != 'nexflow-api.tar' or archive.get('sha256') != archive_hash or type(size) is not int or not 0 < size <= limit - RESERVE:
        raise ValueError('Archive path, size or checksum mismatch.')


def runner_limit():
    if os.environ.get('GITHUB_ACTIONS') != 'true' or platform.system() != 'Linux' or platform.machine().lower() not in ('aarch64', 'arm64'):
        raise ValueError('Only the isolated native ARM64 GitHub runner is allowed.')
    limit = approved_limit(os.environ['RETAIN_API_IMAGE'], os.environ['STORAGE_QUOTA_CONFIRMED'], os.environ['ARTIFACT_BUDGET_MIB'])
    if limit is None or os.environ['RELEASE_SHA'] != os.environ['GITHUB_SHA']:
        raise ValueError('Retention was not requested for this workflow SHA.')
    return limit


def run(args, **kwargs):
    return subprocess.run(args, check=True, capture_output=True, text=True, timeout=600, **kwargs).stdout


def inspect(image_id):
    return json.loads(run(['docker', 'image', 'inspect', image_id]))[0]


def validate_image(image, image_id, source):
    if image['Id'] != image_id or image['Os'] != 'linux' or image['Architecture'] != 'arm64' or image['Config']['User'] != '1654' or image['Config']['Labels']['org.opencontainers.image.revision'] != source:
        raise ValueError('Loaded image differs from the image that passed the smoke.')


def validate_public_env(entries):
    allowed = {'PATH', 'APP_UID', 'ASPNETCORE_HTTP_PORTS', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_RUNNING_IN_CONTAINER', 'DOTNET_VERSION', 'ASPNET_VERSION'}
    seen = set()
    for entry in entries:
        name, separator, _ = entry.partition('=')
        if not separator or name not in allowed or name in seen:
            raise ValueError('Unexpected or ambiguous environment configuration in image.')
        seen.add(name)


def validate_public_config(value, restricted=False):
    if isinstance(value, dict):
        if value.get('type') == 'service_account':
            raise ValueError('Service accounts cannot enter a public archive.')
        sensitive = {'apikey', 'secret', 'apisecret', 'webhooksecret', 'webhookkey', 'password', 'token', 'credential', 'credentials', 'connectionstrings', 'privatekey', 'privatekeyid', 'clientemail'}
        for key, child in value.items():
            normalized = key.lower().replace('_', '').replace('-', '')
            validate_public_config(child, restricted or normalized in sensitive)
    elif isinstance(value, list):
        for child in value:
            validate_public_config(child, restricted)
    elif restricted and value:
        raise ValueError('Nonempty credential-like image configuration.')


def validate_public_tree(folder, expected_config_hash):
    import xml.etree.ElementTree as ET
    allowed_json = {'appsettings.json', 'NexFlow.API.deps.json', 'NexFlow.API.runtimeconfig.json', 'NexFlow.API.staticwebassets.endpoints.json'}
    files = 0
    for path in folder.rglob('*'):
        relative = path.relative_to(folder).as_posix()
        name = path.name.lower()
        if path.is_symlink() or name.startswith('.env') or name in ('.git', 'secrets', 'credentials') or any(p in name for p in ('service-account', 'service_account', 'firebase-adminsdk')):
            raise ValueError('Credential-like path or symlink in the image payload.')
        if not path.is_file():
            continue
        files += 1
        if path.suffix.lower() in ('.key', '.pem', '.pfx', '.p12', '.jks', '.yaml', '.yml', '.ini'):
            raise ValueError('Private or unexpected configuration file in image payload.')
        if path.suffix.lower() == '.json':
            if relative not in allowed_json or path.stat().st_size > 4 * MIB:
                raise ValueError('Unexpected JSON configuration in image payload.')
            validate_public_config(json.loads(path.read_text(encoding='utf-8-sig')))
        if path.suffix.lower() == '.config':
            if relative != 'web.config':
                raise ValueError('Unexpected XML configuration in image payload.')
            tree = ET.parse(path)
            tags = {'configuration', 'location', 'system.webServer', 'handlers', 'add', 'aspNetCore'}
            attributes = {'path', 'inheritInChildApplications', 'name', 'verb', 'modules', 'resourceType', 'processPath', 'arguments', 'stdoutLogEnabled', 'stdoutLogFile', 'hostingModel'}
            if any(node.tag not in tags or set(node.attrib) - attributes for node in tree.iter()):
                raise ValueError('Nonstandard generated web.config; private review required.')
            host = tree.find('.//aspNetCore')
            if host is None or host.get('processPath') != 'dotnet' or host.get('arguments') not in ('.\\NexFlow.API.dll', './NexFlow.API.dll'):
                raise ValueError('Unexpected process or arguments in generated web.config.')
    if file_hash(folder / 'appsettings.json') != expected_config_hash:
        raise ValueError('Image appsettings differs from the reviewed public source.')
    return {'status': 'PASS', 'files_checked': files, 'public_appsettings_sha256': expected_config_hash}


def audit_public_payload(image, image_id):
    validate_public_env(image['Config']['Env'])
    context = json.loads((EVIDENCE / 'context-manifest.json').read_text())
    expected = next(item['sha256'] for item in context if item['path'] == 'backend/NexFlow.API/appsettings.json')
    destination = Path(os.environ['ARM64_ROOT']) / 'public-image-scan'
    container = run(['docker', 'create', '--network', 'none', '--entrypoint', '/bin/true', image_id]).strip()
    try:
        run(['docker', 'cp', container + ':/app', str(destination)])
    finally:
        run(['docker', 'rm', container])
    result = validate_public_tree(destination, expected)
    (EVIDENCE / 'custody-safety.json').write_text(json.dumps(result, indent=2) + '\n')


def export():
    limit = runner_limit()
    source = os.environ['RELEASE_SHA']
    images = json.loads((EVIDENCE / 'images.json').read_text())
    api = images['api']
    image_id = api['id']
    if any(images[k].get(field) != 'PASS' for k in ('api', 'migrator') for field in ('permissions', 'restrictive_permissions', 'synthetic_mount')):
        raise ValueError('Both certified targets must pass every permissions check.')
    codec = api['codec']
    if codec['image_id'] != image_id or codec['exit_code'] != 0 or any(codec[k] != 'PASS' for k in ('png', 'jpeg', 'webp')):
        raise ValueError('Codecs did not pass against this API Image ID.')
    image = inspect(image_id)
    validate_image(image, image_id, source)
    if type(image.get('Size')) is not int or not 0 < image['Size'] <= limit - RESERVE:
        raise ValueError('Unpacked image exceeds the approved archive budget; use separately approved custody.')
    BUNDLE.mkdir(parents=True, exist_ok=False)
    if shutil.disk_usage(BUNDLE).free < 2 * limit + 128 * MIB:
        raise ValueError('Insufficient disposable runner disk for the approved archive budget.')
    audit_public_payload(image, image_id)
    # Linux child limit bounds the regular tar file while Docker writes it, not only after save.
    import resource
    def bound_file():
        resource.setrlimit(resource.RLIMIT_FSIZE, (limit - RESERVE, limit - RESERVE))
    archive = BUNDLE / 'nexflow-api.tar'
    run(['docker', 'image', 'save', '--output', str(archive), image_id], preexec_fn=bound_file)
    checksum = file_hash(archive)
    data = {'format': 'docker-save-tar-v1', 'source_sha': source, 'run_id': os.environ['GITHUB_RUN_ID'], 'run_attempt': os.environ['GITHUB_RUN_ATTEMPT'],
            'image_id': image_id, 'platform': 'linux/arm64', 'uid': 1654, 'gid': 1654, 'oci_revision': source,
            'build_manifest_digest': api['build_manifest_digest'], 'registry_digest': None,
            'archive': {'name': archive.name, 'sha256': checksum, 'size_bytes': archive.stat().st_size},
            'payload_budget_bytes': limit, 'retention_days': 1, 'status': 'EXPORTED_PENDING_ISOLATED_IMPORT',
            'smoke_evidence_sha256': {name: file_hash(EVIDENCE / name) for name in ('images.json', 'context-manifest.json', 'codec-arm64.log', 'custody-safety.json')}}
    validate_manifest(data, source, image_id, checksum, limit)
    manifest = BUNDLE / 'custody-manifest.json'
    manifest.write_text(json.dumps(data, indent=2) + '\n')
    (BUNDLE / 'SHA256SUMS').write_text(f'{checksum}  nexflow-api.tar\n{file_hash(manifest)}  custody-manifest.json\n')
    if sum(p.stat().st_size for p in BUNDLE.iterdir()) > limit:
        raise ValueError('Archive payload exceeds the approved storage budget.')
    (EVIDENCE / 'custody-export.json').write_text(json.dumps(data, indent=2) + '\n')
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        output.write(f'image_id={image_id}\narchive_sha256={checksum}\nmanifest_sha256={file_hash(manifest)}\n')
    print('PASS: saved the smoke-tested API by Image ID, within the approved payload budget.')


def verify():
    limit = runner_limit()
    source = os.environ['RELEASE_SHA']
    image_id = os.environ['EXPECTED_IMAGE_ID']
    checksum = os.environ['EXPECTED_ARCHIVE_SHA256']
    manifest = BUNDLE / 'custody-manifest.json'
    archive = BUNDLE / 'nexflow-api.tar'
    if any(p.is_symlink() or not p.is_file() for p in (manifest, archive)) or manifest.stat().st_size > 32_768:
        raise ValueError('Invalid custody files.')
    if file_hash(manifest) != os.environ['EXPECTED_MANIFEST_SHA256']:
        raise ValueError('Manifest checksum differs from the exporting job.')
    data = json.loads(manifest.read_text())
    validate_manifest(data, source, image_id, checksum, limit)
    if data['run_id'] != os.environ['GITHUB_RUN_ID'] or data['run_attempt'] != os.environ['GITHUB_RUN_ATTEMPT']:
        raise ValueError('Archive belongs to another workflow run or attempt; rerun all jobs.')
    if archive.stat().st_size != data['archive']['size_bytes'] or file_hash(archive) != checksum:
        raise ValueError('Archive bytes differ from the exporting job.')
    run(['docker', 'info', '--format', '{{.OSType}}/{{.Architecture}}'])
    absent = subprocess.run(['docker', 'image', 'inspect', image_id], capture_output=True, timeout=30)
    if absent.returncode != 1:
        raise ValueError('Fresh runner must not contain the expected image before import.')
    run(['docker', 'image', 'load', '--input', str(archive)])
    validate_image(inspect(image_id), image_id, source)
    script = 'set -eu; test -x /app; for f in appsettings.json NexFlow.API.dll NexFlow.API.deps.json NexFlow.API.runtimeconfig.json; do test -r "/app/$f"; done; printf "%s:%s" "$(id -u)" "$(id -g)"'
    ids = run(['docker', 'run', '--rm', '--pull=never', '--platform', 'linux/arm64', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--pids-limit', '64', '--memory', '128m', '--cpus', '1', '--entrypoint', '/bin/sh', image_id, '-ec', script]).strip()
    if ids != '1654:1654':
        raise ValueError('Imported image UID/GID changed.')
    proof = {'status': 'PASS', 'source_sha': source, 'run_id': data['run_id'], 'run_attempt': data['run_attempt'], 'image_id': image_id,
             'archive_sha256': checksum, 'size_bytes': archive.stat().st_size, 'manifest_sha256': file_hash(manifest),
             'platform': 'linux/arm64', 'uid': 1654, 'gid': 1654, 'oci_revision': source, 'image_absent_before_load': True,
             'api_entrypoint_executed': False, 'registry_push': False}
    PROOF.write_text(json.dumps(proof, indent=2) + '\n')
    print('PASS: isolated load recovered the same image, revision, platform, UID/GID and file access.')


def main():
    mode = sys.argv[1] if len(sys.argv) == 2 else ''
    if mode == 'verify':
        PROOF.parent.mkdir(parents=True, exist_ok=True)
    try:
        if mode == 'preflight':
            limit = approved_limit(os.environ['RETAIN_API_IMAGE'], os.environ['STORAGE_QUOTA_CONFIRMED'], os.environ['ARTIFACT_BUDGET_MIB'])
            print('PASS: custody disabled.' if limit is None else 'PASS: custody inputs validated; quota is attested by the owner, not queried.')
        elif mode == 'export':
            export()
        elif mode == 'verify':
            verify()
        else:
            raise ValueError('Expected preflight, export or verify.')
        return 0
    except Exception as error:
        if mode == 'export':
            EVIDENCE.mkdir(parents=True, exist_ok=True)
            (EVIDENCE / 'custody-export-failure.json').write_text(json.dumps({'status': 'FAIL', 'error_type': type(error).__name__, 'binary_upload_allowed': False}) + '\n')
        if mode == 'verify':
            PROOF.write_text(json.dumps({'status': 'FAIL', 'error_type': type(error).__name__}) + '\n')
        print('NO-GO: custody ' + mode + ' failed (' + type(error).__name__ + '); no raw configuration printed.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
