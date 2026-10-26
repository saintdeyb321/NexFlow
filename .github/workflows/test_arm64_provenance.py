"""Synthetic metadata contracts; never runs Docker or loads the workflow's other code."""

import copy
import json
from pathlib import Path
import textwrap
import unittest


WORKFLOW = Path(__file__).with_name('agent-runtime-arm64-certification.yml')
text = WORKFLOW.read_text(encoding='utf-8')
source = text.split('# BEGIN provenance parser (also exercised directly by the unit tests)\n', 1)[1].split('# END provenance parser', 1)[0]
namespace = {}
exec(compile(textwrap.dedent(source), str(WORKFLOW) + ':provenance-parser', 'exec'), namespace)
parse = namespace['parse_base_materials']

# Reduced, synthetic projections of Docker's documented SLSA/Buildx schemas.
# Digests are test values, never evidence of an image built by this repository.
BASES = [
    {'uri': 'pkg:docker/mcr.microsoft.com/dotnet/sdk@10.0?platform=linux%2Farm64', 'digest': {'sha256': 'a' * 64}},
    {'uri': 'pkg:docker/mcr.microsoft.com/dotnet/aspnet@10.0?platform=linux%2Farm64', 'digest': {'sha256': 'b' * 64}},
]


def metadata(materials=None):
    return {'buildx.build.provenance': {'buildType': 'https://mobyproject.org/buildkit@v1', 'materials': copy.deepcopy(BASES if materials is None else materials)}}


class Arm64ProvenanceTests(unittest.TestCase):
    def test_direct_buildx_v02(self):
        self.assertEqual(parse(metadata()), BASES)

    def test_wrapped_slsa_v02(self):
        record = metadata()['buildx.build.provenance']
        self.assertEqual(parse({'buildx.build.provenance': {'predicateType': 'https://slsa.dev/provenance/v0.2', 'predicate': record}}), BASES)

    def test_direct_slsa_v1(self):
        self.assertEqual(parse({'buildx.build.provenance': {'buildDefinition': {'resolvedDependencies': BASES}}}), BASES)

    def test_wrapped_slsa_v1(self):
        self.assertEqual(parse({'buildx.build.provenance': {'predicateType': 'https://slsa.dev/provenance/v1', 'predicate': {'buildDefinition': {'resolvedDependencies': BASES}}}}), BASES)

    def test_platform_qualified_buildx(self):
        record = metadata()['buildx.build.provenance']
        self.assertEqual(parse({'buildx.build.provenance/linux/arm64': record}), BASES)

    def test_duplicate_identical_base_is_not_a_conflict(self):
        self.assertEqual(parse(metadata(BASES + [BASES[0]])), BASES)

    def test_missing_provenance_is_rejected(self):
        with self.assertRaises(ValueError):
            parse({})

    def test_malformed_metadata_is_rejected(self):
        for value in (None, [], 'not metadata', {'buildx.build.provenance': None}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                parse(value)

    def test_both_bases_are_required(self):
        for bases in ([], BASES[:1], BASES[1:]):
            with self.subTest(bases=bases), self.assertRaises(ValueError):
                parse(metadata(bases))

    def test_sha256_is_required_and_must_be_valid(self):
        for digest in ({}, {'sha1': 'c' * 40}, {'sha256': ''}, {'sha256': 'f' * 63}, {'sha256': 'g' * 64}, {'sha256': 'sha256:' + 'a' * 64}, {'sha256': []}, 'not a digest'):
            bases = copy.deepcopy(BASES)
            bases[0]['digest'] = digest
            with self.subTest(digest=digest), self.assertRaises(ValueError):
                parse(metadata(bases))

    def test_base_repository_requires_exact_match(self):
        for uri in ('pkg:docker/evil.invalid/mcr.microsoft.com/dotnet/sdk@10.0', 'pkg:docker/mcr.microsoft.com/dotnet/sdk-evil@10.0', 'https://mcr.microsoft.com/dotnet/sdk@10.0'):
            bases = copy.deepcopy(BASES)
            bases[0]['uri'] = uri
            with self.subTest(uri=uri), self.assertRaises(ValueError):
                parse(metadata(bases))

    def test_conflicting_digest_is_rejected(self):
        duplicate = copy.deepcopy(BASES[0])
        duplicate['digest']['sha256'] = 'c' * 64
        with self.assertRaises(ValueError):
            parse(metadata(BASES + [duplicate]))

    def test_ambiguous_schema_is_rejected(self):
        value = metadata()
        value['buildx.build.provenance']['buildDefinition'] = {'resolvedDependencies': BASES}
        with self.assertRaises(ValueError):
            parse(value)

    def test_ambiguous_platform_records_are_rejected(self):
        value = metadata()
        value['buildx.build.provenance/linux/arm64'] = value['buildx.build.provenance']
        with self.assertRaises(ValueError):
            parse(value)

    def test_unknown_or_mismatched_predicate_type_is_rejected(self):
        for schema in ('unknown', None, 'https://slsa.dev/provenance/v1'):
            with self.subTest(schema=schema), self.assertRaises(ValueError):
                parse({'buildx.build.provenance': {'predicateType': schema, 'predicate': {'materials': BASES}}})

    def test_non_arm64_or_ambiguous_platform_is_rejected(self):
        for query in ('platform=linux%2Famd64', 'platform=linux%2Farm64&platform=linux%2Famd64'):
            bases = copy.deepcopy(BASES)
            bases[0]['uri'] = 'pkg:docker/mcr.microsoft.com/dotnet/sdk@10.0?' + query
            with self.subTest(query=query), self.assertRaises(ValueError):
                parse(metadata(bases))

    def test_malformed_dependency_list_is_rejected(self):
        for materials in (None, {}, 'not a list'):
            with self.subTest(materials=materials), self.assertRaises(ValueError):
                parse({'buildx.build.provenance': {'materials': materials}})

    def test_malformed_dependency_entry_is_rejected(self):
        for material in (None, {}, {'uri': 10}):
            with self.subTest(material=material), self.assertRaises(ValueError):
                parse(metadata(BASES + [material]))

    def test_unknown_nested_materials_do_not_count_as_evidence(self):
        with self.assertRaises(ValueError):
            parse({'buildx.build.provenance': {'unrelated': {'materials': BASES}}})

    def test_json_serialized_metadata_roundtrip(self):
        self.assertEqual(parse(json.loads(json.dumps(metadata()))), BASES)


if __name__ == '__main__':
    unittest.main()
