"""Synthetic TRX only; never starts PostgreSQL or Firestore."""
import io
import unittest
import xml.etree.ElementTree as ET


JOURNAL_CLASS = "NexFlow.Tests.Integration.AgentJournalEmulatorTests"
JOURNAL_METHOD = "Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone"


def trx(journal=True, wrong_class=False, duplicate=False, skipped=False, failed=False):
    root = ET.Element("TestRun"); results = ET.SubElement(root, "Results"); definitions = ET.SubElement(root, "TestDefinitions")
    summary = ET.SubElement(root, "ResultSummary", outcome="Completed")
    counters = dict.fromkeys(("error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable",
        "notExecuted", "disconnected", "warning", "inProgress", "pending"), "0")
    counters.update(total="37", executed="37", passed="37", failed="0")
    if failed: counters.update(passed="36", failed="1")
    if skipped: counters.update(executed="36", passed="36", notExecuted="1")
    ET.SubElement(summary, "Counters", counters)
    for i in range(37):
        is_journal = journal and (i == 0 or duplicate and i == 1)
        cls = JOURNAL_CLASS if is_journal and not wrong_class else "Synthetic.Integration.OtherTests"
        method = JOURNAL_METHOD if is_journal else "Case" + str(i)
        definition = ET.SubElement(definitions, "UnitTest", id=str(i))
        ET.SubElement(definition, "TestMethod", className=cls, name=method)
        ET.SubElement(results, "UnitTestResult", testId=str(i), executionId=str(i), testName=cls + "." + method,
            outcome="NotExecuted" if skipped and i == 0 else "Failed" if failed and i == 0 else "Passed")
    return io.StringIO(ET.tostring(root, encoding="unicode"))


class IntegrationEvidenceTests(unittest.TestCase):
    def validate(self, stream):
        import verify_integration_evidence as validator
        return validator.validate(stream)

    def test_named_journal_PASS_is_required_beyond_the_integration_count(self):
        self.assertEqual(37, self.validate(trx())["passed"])
        with self.assertRaisesRegex(ValueError, "journal"):
            self.validate(trx(journal=False))

    def test_same_method_in_another_class_cannot_certify_journal(self):
        with self.assertRaisesRegex(ValueError, "journal"):
            self.validate(trx(wrong_class=True))

    def test_duplicate_journal_identity_is_not_certified(self):
        with self.assertRaisesRegex(ValueError, "journal"):
            self.validate(trx(duplicate=True))

    def test_failed_or_omitted_journal_is_never_certified(self):
        for flags in ({"failed": True}, {"skipped": True}):
            with self.subTest(flags=flags), self.assertRaises(ValueError):
                self.validate(trx(**flags))


if __name__ == "__main__":
    unittest.main()
