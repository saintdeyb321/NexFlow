"""No credentials, models or dotnet launched: preflight must stop before them."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

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


if __name__ == "__main__":
    unittest.main()
