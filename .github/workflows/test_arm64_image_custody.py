"""Pure archive/budget contracts. These tests never execute or mock Docker."""

import copy
import hashlib
from pathlib import Path
import tempfile
import unittest

from arm64_image_custody import MIB, RESERVE, approved_limit, file_hash, validate_manifest, validate_public_config, validate_public_env, validate_public_tree


SOURCE = 'a' * 40
IMAGE = 'sha256:' + 'b' * 64
CHECKSUM = 'c' * 64


def manifest():
    return {'format': 'docker-save-tar-v1', 'source_sha': SOURCE, 'image_id': IMAGE,
            'platform': 'linux/arm64', 'uid': 1654, 'gid': 1654, 'oci_revision': SOURCE,
            'archive': {'name': 'nexflow-api.tar', 'sha256': CHECKSUM, 'size_bytes': MIB}}


class ImageCustodyTests(unittest.TestCase):
    def test_custody_disabled_by_default(self):
        self.assertIsNone(approved_limit('false', 'false', '0'))

    def test_quota_attestation_and_budget_are_required(self):
        for requested, confirmed, budget in (('true', 'false', '512'), ('true', 'true', '0'),
                                              ('true', 'true', '513'), ('true', 'true', '-1'),
                                              ('true', 'true', '5.5'), ('true', 'true', 'x')):
            with self.subTest(budget=budget, confirmed=confirmed), self.assertRaises(ValueError):
                approved_limit(requested, confirmed, budget)

    def test_payload_budget_is_bounded(self):
        self.assertEqual(approved_limit('true', 'true', '512'), 512 * MIB)
        validate_manifest(manifest(), SOURCE, IMAGE, CHECKSUM, 2 * MIB)

    def test_file_checksum_uses_actual_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'synthetic-bytes'
            path.write_bytes(b'only synthetic bytes')
            self.assertEqual(file_hash(path), hashlib.sha256(b'only synthetic bytes').hexdigest())
            before = file_hash(path)
            path.write_bytes(b'changed synthetic bytes')
            self.assertNotEqual(file_hash(path), before)

    def test_wrong_source_image_or_revision_is_rejected(self):
        for key, value in (('source_sha', 'd' * 40), ('image_id', 'sha256:' + 'e' * 64), ('oci_revision', 'd' * 40)):
            data = manifest()
            data[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_manifest(data, SOURCE, IMAGE, CHECKSUM, 2 * MIB)

    def test_wrong_platform_or_user_is_rejected(self):
        for key, value in (('platform', 'linux/amd64'), ('uid', 0), ('gid', 0)):
            data = manifest()
            data[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_manifest(data, SOURCE, IMAGE, CHECKSUM, 2 * MIB)

    def test_archive_path_and_hash_are_bound_to_the_export(self):
        for key, value in (('name', '../other.tar'), ('sha256', 'e' * 64)):
            data = manifest()
            data['archive'][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_manifest(data, SOURCE, IMAGE, CHECKSUM, 2 * MIB)

    def test_missing_zero_or_oversized_archive_is_rejected(self):
        for value in (None, True, 0, -1, 2 * MIB - RESERVE + 1):
            data = manifest()
            data['archive']['size_bytes'] = value
            with self.subTest(size=value), self.assertRaises(ValueError):
                validate_manifest(data, SOURCE, IMAGE, CHECKSUM, 2 * MIB)

    def test_invalid_identity_formats_are_rejected(self):
        for source, image, checksum in ((SOURCE[:7], IMAGE, CHECKSUM), (SOURCE, IMAGE[7:], CHECKSUM), (SOURCE, IMAGE, 'not a hash')):
            with self.subTest(source=source, image=image), self.assertRaises(ValueError):
                validate_manifest(copy.deepcopy(manifest()), source, image, checksum, 2 * MIB)


    def test_only_public_image_environment_is_allowed(self):
        validate_public_env(['APP_UID=1654', 'ASPNETCORE_ENVIRONMENT=Production'])
        for values in (['GOOGLE_APPLICATION_CREDENTIALS=/synthetic.json'], ['PRIVATE_VALUE=synthetic'], ['APP_UID=1654', 'APP_UID=0']):
            with self.subTest(values=values), self.assertRaises(ValueError):
                validate_public_env(values)

    def test_credential_json_is_rejected_without_matching_package_names(self):
        validate_public_config({'ConnectionStrings': {'DefaultConnection': ''}, 'Gemini': {'ApiKey': ''}})
        validate_public_config({'System.IdentityModel.Tokens/1.0': {'type': 'package'}})
        for value in ({'type': 'service_account'}, {'private_key': 'synthetic'}, {'ConnectionStrings': {'DefaultConnection': 'synthetic'}}, {'Gemini': {'ApiKey': 'synthetic'}}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_public_config(value)

    def test_private_paths_and_unknown_json_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            public = root / 'appsettings.json'
            public.write_text('{}')
            expected = file_hash(public)
            self.assertEqual(validate_public_tree(root, expected)['status'], 'PASS')
            for name in ('.env', 'firebase-service-account.json', 'appsettings.Production.json', 'unknown.json', 'credentials', 'private.pem'):
                path = root / name
                path.write_text('{}')
                with self.subTest(name=name), self.assertRaises(ValueError):
                    validate_public_tree(root, expected)
                path.unlink()

    def test_generated_web_config_cannot_add_private_environment(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            public = root / 'appsettings.json'
            public.write_text('{}')
            web = root / 'web.config'
            web.write_text('<configuration><system.webServer><aspNetCore processPath="dotnet" arguments="./NexFlow.API.dll" /></system.webServer></configuration>')
            self.assertEqual(validate_public_tree(root, file_hash(public))['status'], 'PASS')
            web.write_text('<configuration><environmentVariables /></configuration>')
            with self.assertRaises(ValueError):
                validate_public_tree(root, file_hash(public))

    def test_public_config_bytes_must_match_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'appsettings.json').write_text('{}')
            with self.assertRaises(ValueError):
                validate_public_tree(root, '0' * 64)


if __name__ == '__main__':
    unittest.main()
