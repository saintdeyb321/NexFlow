"""Tests of the CI validator only; no application code, stores or network."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import g8_certification as gate


def evidence(historical=True):
    root = ET.Element("TestRun")
    results = ET.SubElement(root, "Results")
    definitions = ET.SubElement(root, "TestDefinitions")
    summary = ET.SubElement(root, "ResultSummary", outcome="Failed" if historical else "Completed")
    total, passed, failed = (130, 102, 28) if historical else (gate.MIN_CURRENT, gate.MIN_CURRENT, 0)
    counters = dict(total=total, executed=total, passed=passed, failed=failed)
    counters.update({key: 0 for key in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                                      "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending")})
    ET.SubElement(summary, "Counters", {key: str(value) for key, value in counters.items()})
    cases = gate.historical_contract(gate.ROOT) if historical else {
        f"current-{i}": ("NexFlow.Tests.Concierge.RuntimeF3ContractV2Tests" if i < 12 else
                        "NexFlow.Tests.AssistantCurrentTests" if i < gate.MIN_RUNTIME else "NexFlow.Tests.CurrentTests",
                        "case", None) for i in range(total)}
    for i, (name, (cls, method, reason)) in enumerate(cases.items()):
        result = ET.SubElement(results, "UnitTestResult", testName=name, testId=str(i),
                               executionId=f"execution-{i}", outcome="Failed" if reason else "Passed")
        if reason:
            ET.SubElement(ET.SubElement(ET.SubElement(result, "Output"), "ErrorInfo"), "Message").text = reason
        definition = ET.SubElement(definitions, "UnitTest", id=str(i))
        ET.SubElement(definition, "TestMethod", className=cls, name=method)
    return root


class GateTests(unittest.TestCase):
    def setUp(self):
        folder = gate.ROOT / ".github/workflows/TestResults/g8"
        folder.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=folder)
        self.trx = Path(self.temp.name) / "synthetic.trx"

    def tearDown(self):
        self.temp.cleanup()

    def verify(self, root, suite="historical", code=1):
        ET.ElementTree(root).write(self.trx, encoding="utf-8", xml_declaration=True)
        return gate.validate(suite, self.trx, code)

    def test_exact_frozen_historical_contract_preserves_red_tests(self):
        result = self.verify(evidence())
        self.assertEqual("EXPECTED_HISTORICAL_INCOMPATIBILITIES", result["decision"])
        self.assertEqual(28, len(result["known_failures"]))

    def test_new_order_consent_historical_failures_are_exactly_pinned(self):
        cases = gate.historical_contract(gate.ROOT)
        expected = {
            f'{gate.PUBLIC}.Independent_commercial_conversation(caseId: "{sector}-{number}")': f"{sector}-{number}: OrderEffects"
            for sector in ("clinic", "hotel", "restaurant", "bakery", "limited")
            for number in ("03", "04", "19")
        }
        expected[f'{gate.HOLDOUT}.Frozen_independent_conversation(id: "H26")'] = (
            "H26/2/A: OrderEffects; H26/3/A: OrderEffects")
        observed = {name: reason for name, (_, _, reason) in cases.items() if reason and "OrderEffects" in reason}
        self.assertEqual(expected, observed)
        self.assertEqual(12, sum("VerifiedFact" in reason for _, _, reason in cases.values() if reason))
        self.assertEqual(130, len(cases))
        for name in expected:
            with self.subTest(case=name):
                root = evidence()
                result = next(r for r in root.find("Results") if r.attrib["testName"] == name)
                result.find("Output/ErrorInfo/Message").text += "; unexpected change"
                with self.assertRaises(ValueError):
                    self.verify(root)

    def test_missing_duplicate_or_foreign_historical_case_is_rejected(self):
        for kind in ("missing", "duplicate", "foreign", "execution", "testId"):
            with self.subTest(kind=kind):
                root = evidence()
                results = root.find("Results")
                if kind == "missing":
                    results.remove(results[0])
                elif kind == "duplicate":
                    results[0].set("testName", results[1].attrib["testName"])
                elif kind == "execution":
                    results[0].set("executionId", results[1].attrib["executionId"])
                elif kind == "testId":
                    results[0].set("testId", results[1].attrib["testId"])
                else:
                    results[0].set("testName", "another-test")
                with self.assertRaises(ValueError):
                    self.verify(root)

    def test_same_counts_with_different_failure_or_reason_are_rejected(self):
        for kind in ("reason", "failure"):
            with self.subTest(kind=kind):
                root = evidence()
                results = root.find("Results")
                failed = next(r for r in results if r.attrib["outcome"] == "Failed")
                if kind == "reason":
                    failed.find("Output/ErrorInfo/Message").text += "; new error"
                else:
                    passed = next(r for r in results if r.attrib["outcome"] == "Passed")
                    passed.set("outcome", "Failed")
                    failed.set("outcome", "Passed")
                with self.assertRaises(ValueError):
                    self.verify(root)

    def test_unexpected_exit_codes_and_skips_are_rejected(self):
        for code in (0, 2, -1):
            with self.subTest(code=code), self.assertRaises(ValueError):
                self.verify(evidence(), code=code)
        root = evidence()
        root.find("Results")[0].set("outcome", "NotExecuted")
        with self.assertRaises(ValueError):
            self.verify(root)

    def test_frozen_source_change_is_rejected_but_checkout_eol_is_normalized(self):
        actual = Path.read_bytes
        with patch.object(Path, "read_bytes", lambda path: actual(path).replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")):
            gate.pinned_sources(gate.ROOT)
        with patch.object(Path, "read_bytes", lambda path: actual(path) + b"\n"):
            with self.assertRaisesRegex(ValueError, "Frozen corpus/evaluator changed"):
                gate.pinned_sources(gate.ROOT)

    def test_current_requires_coverage_no_failures_and_f3_v2(self):
        self.assertEqual("PASS", self.verify(evidence(False), "current", 0)["decision"])
        for kind in ("exit", "count", "failed", "v2", "runtime", "history"):
            with self.subTest(kind=kind):
                root = evidence(False)
                if kind == "count":
                    root.find("ResultSummary/Counters").set("total", str(gate.MIN_CURRENT - 1))
                if kind == "failed":
                    root.find("Results")[0].set("outcome", "Failed")
                if kind in ("v2", "history", "runtime"):
                    for method in root.findall("TestDefinitions/UnitTest/TestMethod"):
                        if kind != "runtime" or method.attrib["className"] == "NexFlow.Tests.AssistantCurrentTests":
                            method.set("className", gate.PUBLIC if kind == "history" else "NexFlow.Tests.CurrentTests")
                with self.assertRaises(ValueError):
                    self.verify(root, "current", 1 if kind == "exit" else 0)

    def test_malformed_trx_and_wrong_test_definition_are_rejected(self):
        self.trx.write_text("<broken", encoding="utf-8")
        with self.assertRaises(ET.ParseError):
            gate.validate("historical", self.trx, 1)
        root = evidence()
        root.find("TestDefinitions/UnitTest/TestMethod").set("className", "AnotherClass")
        with self.assertRaises(ValueError):
            self.verify(root)


if __name__ == "__main__":
    unittest.main()
