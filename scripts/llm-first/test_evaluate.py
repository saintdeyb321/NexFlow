"""No credentials, models or dotnet launched: preflight must stop before them."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import importlib.util
import contextlib
import io
from unittest.mock import patch
import xml.etree.ElementTree as ET

RUNNER = Path(__file__).with_name("evaluate.py")
CORPUS = RUNNER.parents[2] / "backend/NexFlow.Tests/Concierge/AgentDevelopmentConversations.json"


class EvaluationPreflightTests(unittest.TestCase):
    def invoke(self, *arguments):
        env = {k: v for k, v in os.environ.items() if not k.startswith("NEXFLOW_")}
        return subprocess.run([sys.executable, "-B", str(RUNNER), *arguments], env=env, capture_output=True, text=True, timeout=15)

    def test_live_requires_explicit_authorization_before_creating_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "output"
            result = self.invoke("--live", "--sha256", hashlib.sha256(CORPUS.read_bytes()).hexdigest(), "--output", str(target))
            self.assertEqual(2, result.returncode)
            self.assertIn("explicit live authorization", result.stderr)
            self.assertFalse(target.exists())

    def test_external_corpus_requires_hash_before_dotnet(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus = Path(directory) / "sealed.json"; corpus.write_bytes(CORPUS.read_bytes())
            result = self.invoke("--corpus", str(corpus), "--output", str(Path(directory) / "out"))
            self.assertEqual(2, result.returncode); self.assertIn("requires --sha256", result.stderr)

    def test_wrong_hash_is_not_evaluated(self):
        result = self.invoke("--sha256", "0" * 64, "--output", "unused")
        self.assertEqual(2, result.returncode); self.assertIn("SHA256 mismatch", result.stderr)

    def test_development_inventory_is_full_conversations_and_explicitly_nonblind(self):
        corpus = json.loads(CORPUS.read_bytes())
        self.assertEqual(40, len(corpus["Conversations"]))
        self.assertEqual(40, len({c["Id"] for c in corpus["Conversations"]}))
        self.assertTrue(all(3 <= len(c["Turns"]) <= 12 for c in corpus["Conversations"]))
        self.assertIn("NOT blind", corpus["Provenance"])


class LiveEvidenceContractTests(unittest.TestCase):
    """Runner protocol tests; subprocess and provider observations are fabricated.

    They reproduce false-positive evidence and marker bugs without any HTTP.
    """
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        spec = importlib.util.spec_from_file_location("llm_evaluate", RUNNER)
        self.runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.runner)
        self.root = Path(self.temp.name); self.runner.ROOT = self.root
        self.corpus = self.root / "corpus.json"
        self.corpus.write_text(json.dumps({"Version": 1, "Provenance": "synthetic protocol fixture, not NLU",
            "Conversations": [{"Id": name, "Category": "protocol", "InitialDraft": False,
                "Turns": [{"Text": "Consulta sintética", "Decision": "Information"} for _ in range(3)]}
                for name in ("D01", "D02")]}), encoding="utf-8")
        self.digest = hashlib.sha256(self.corpus.read_bytes()).hexdigest()
        self.env = {"NEXFLOW_LLM_LIVE_AUTHORIZED": "1", "NEXFLOW_LLM_QUOTAS_VERIFIED": "1",
            "NEXFLOW_TEST_GROQ_API_KEY": "synthetic-not-a-key", "NEXFLOW_TEST_GEMINI_API_KEY": "synthetic-not-a-key",
            "NEXFLOW_TEST_GROQ_MODEL": "groq-synthetic", "NEXFLOW_TEST_GEMINI_MODEL": "gemini-synthetic"}
        self.calls = 0; self.missing = False; self.partial = False; self.no_http = False; self.omitted = False

    def fake_process(self, command, **kwargs):
        self.calls += 1; folder = Path(command[command.index("--results-directory") + 1]); env = kwargs["env"]
        case = env["NEXFLOW_LLM_LIVE_CASE"]
        if self.no_http: return subprocess.CompletedProcess(command, 1, "compile failed", "")
        claim = env.get("NEXFLOW_LLM_CLAIM_FILE")
        if claim:
            with Path(claim).open("x", encoding="utf-8") as f:
                json.dump({"state": "Started", "identity": env["NEXFLOW_LLM_ATTEMPT_ID"]}, f)
        run = ET.Element("TestRun"); summary = ET.SubElement(run, "ResultSummary")
        counts = {k: "0" for k in ("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable",
            "notExecuted", "disconnected", "warning", "inProgress", "pending")}
        counts.update(total="1", executed="0" if self.omitted else "1", passed="0" if self.omitted else "1", notExecuted="1" if self.omitted else "0")
        ET.SubElement(summary, "Counters", counts)
        rows = ET.SubElement(run, "Results")
        if not self.omitted: ET.SubElement(rows, "UnitTestResult", {"testId": "one", "executionId": "one", "outcome": "Passed",
            "testName": "NexFlow.Tests.Concierge.AgentLiveEvaluationTests.Authorized_complete_conversation_uses_real_router_with_shared_HTTP_budget"})
        ET.ElementTree(run).write(folder / "evaluation.trx")
        if not self.missing:
            observations = [{"Turn": i, "Act": "Information", "ExpectedAct": "Information", "StateCorrect": True,
                "ActCorrect": True, "PublicText": "Respuesta sintética", "Outcome": "DialoguePlan", "ModelCalls": 1 if i == 0 else 0,
                "ElapsedMilliseconds": 12, "ResponseValidation": "ServerRealizedVerifiedSegments", "CommercialEffects": 0,
                "OutboundEffects": 0} for i in range(1 if self.partial else 3)]
            report = {"Version": 2, "Case": case, "CorpusSHA256": self.digest, "AttemptId": env.get("NEXFLOW_LLM_ATTEMPT_ID"),
                "Mode": "live", "Observations": observations, "HTTPAttempts": 1, "TokenReservation": 2000,
                "ReportedTokens": 42, "UsageResponses": 1, "Requests": [{"Provider": "Groq", "Model": "groq-synthetic",
                    "Transport": "ProviderHttp", "Status": 200, "ElapsedMs": 12, "ReservedTokens": 2000, "ReportedTokens": 42}],
                "Failures": [], "HumanQuality": "BLOCKED", "CommercialAcceptance": "NOT_CERTIFIED"}
            (folder / ("live-" + case + ".json")).write_text(json.dumps(report), encoding="utf-8")
        return subprocess.CompletedProcess(command, 0, "synthetic subprocess; HTTP=0", "")

    def invoke(self, case="D01", output="out"):
        target = self.root / output
        args = [str(RUNNER), "--live", "--no-build", "--corpus", str(self.corpus), "--sha256", self.digest,
            "--case", case, "--output", str(target)]
        with patch.dict(os.environ, self.env, clear=True), patch.object(sys, "argv", args), \
             patch.object(self.runner.subprocess, "check_output", return_value="e" * 40 + "\n"), \
             patch.object(self.runner.subprocess, "run", side_effect=self.fake_process), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            try: result = self.runner.main()
            except SystemExit as ex: result = ex.code
            except FileExistsError: result = 2
            return result, json.loads((target / "summary.json").read_text()) if (target / "summary.json").exists() else None

    def test_live_report_name_and_actual_usage_are_included(self):
        code, summary = self.invoke(); self.assertEqual(0, code)
        self.assertEqual(1, summary["live"]["http_attempts"]); self.assertEqual(42, summary["live"]["reported_tokens"])
        self.assertEqual(["Groq"], summary["live"]["providers_observed"])
        self.assertEqual("BLOCKED", summary["human_quality"])

    def test_missing_or_partial_live_report_never_passes(self):
        for kind in ("missing", "partial"):
            with self.subTest(kind=kind):
                self.missing = kind == "missing"; self.partial = kind == "partial"
                code, summary = self.invoke(case="D01" if kind == "missing" else "D02", output=kind)
                self.assertEqual(1, code); self.assertEqual("FAIL", summary["result"]); setattr(self, kind, False)

    def test_different_cases_have_independent_claims_and_same_case_never_repeats(self):
        self.assertEqual(0, self.invoke()[0]); self.assertEqual(0, self.invoke("D02", "second")[0])
        self.assertEqual(2, self.invoke("D01", "replay")[0]); self.assertEqual(2, self.calls)

    def test_failed_preflight_does_not_reserve_a_live_attempt(self):
        (self.root / "existing").mkdir(); self.assertEqual(2, self.invoke(output="existing")[0])
        self.assertEqual([], list((self.root / "backend/TestResults").glob("llm-live*")))
        self.assertEqual(0, self.calls)

    def test_compile_failure_before_HTTP_allows_a_new_attempt(self):
        self.no_http = True; code, _ = self.invoke(); self.assertEqual(1, code)
        self.no_http = False; self.assertEqual(0, self.invoke(output="retry-preflight-only")[0]); self.assertEqual(2, self.calls)

    def test_omitted_test_is_not_certified(self):
        self.omitted = True; code, summary = self.invoke(); self.assertEqual(1, code); self.assertEqual("FAIL", summary["result"])

    def test_unknown_legacy_marker_blocks_but_known_other_case_is_preserved(self):
        folder = self.root / "backend/TestResults"; folder.mkdir(parents=True)
        marker = folder / ("llm-live-" + self.digest + ".started")
        marker.write_text(json.dumps({"case": "D01"})); before = marker.read_bytes()
        self.assertEqual(2, self.invoke()[0]); self.assertEqual(0, self.invoke("D02", "another")[0]); self.assertEqual(before, marker.read_bytes())

    def test_unreadable_legacy_marker_is_uncertain(self):
        folder = self.root / "backend/TestResults"; folder.mkdir(parents=True)
        (folder / ("llm-live-" + self.digest + ".started")).write_text("partial")
        self.assertEqual(2, self.invoke()[0]); self.assertEqual(2, self.invoke("D02", "another")[0]); self.assertEqual(0, self.calls)

    def test_provider_model_and_transport_are_checked_independently(self):
        import evaluation_evidence as evidence
        report = {"Version": 2, "Case": "D01", "Mode": "live", "CorpusSHA256": self.digest, "AttemptId": "attempt",
            "Observations": [{"Turn": i, "ElapsedMilliseconds": 1, "ModelCalls": 1, "StateCorrect": True, "ActCorrect": True,
                "CommercialEffects": 0, "OutboundEffects": 0, "ResponseValidation": "ServerRealizedVerifiedSegments"} for i in range(3)],
            "HTTPAttempts": 1, "UsageResponses": 0, "ReportedTokens": None, "TokenReservation": 1000, "Failures": [],
            "Requests": [{"Provider": "Groq", "Model": "groq-synthetic", "Transport": "InterceptedHttp", "Status": 200,
                "ElapsedMs": 1, "ReservedTokens": 1000, "ReportedTokens": None}]}
        with self.assertRaisesRegex(ValueError, "intercepted HTTP"):
            evidence.live_report(report, json.loads(self.corpus.read_text())["Conversations"][0], self.digest, "attempt", {"Groq": "groq-synthetic"})
        report["Requests"][0]["Transport"] = "ProviderHttp"; report["Requests"][0]["Model"] = "unobserved-model"
        with self.assertRaisesRegex(ValueError, "provider/model"):
            evidence.live_report(report, json.loads(self.corpus.read_text())["Conversations"][0], self.digest, "attempt", {"Groq": "groq-synthetic"})


if __name__ == "__main__":
    unittest.main()
