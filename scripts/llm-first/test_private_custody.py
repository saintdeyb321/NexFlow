"""Exercise owner-only custody preparation with opaque synthetic files."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("Backup-ExposedEvaluation.ps1")
NAMES = ("NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json", "NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md")


class PrivateCustodyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "synthetic-repository"; self.root.mkdir()
        self.private = Path(self.temp.name) / "private-custody"
        for name in NAMES: (self.root / name).write_bytes(b"opaque synthetic fixture; NOT an evaluation")

    def invoke(self, backup=None):
        executable = shutil.which("pwsh") or shutil.which("powershell")
        self.assertIsNotNone(executable, "PowerShell is required to verify the custody script")
        return subprocess.run([executable, "-NoLogo", "-NoProfile", "-File", str(SCRIPT), "-RepositoryRoot", str(self.root),
            "-BackupDirectory", str(backup or self.private)], text=True, capture_output=True, timeout=20)

    def test_verified_copy_preserves_originals_and_is_repeatable_without_deleting(self):
        for _ in range(2):
            result = self.invoke(); self.assertEqual(0, result.returncode, result.stderr)
        for name in NAMES:
            self.assertEqual((self.root / name).read_bytes(), (self.private / name).read_bytes())
        self.assertNotIn("opaque synthetic", result.stdout)

    def test_custody_inside_repository_is_rejected_without_copy(self):
        result = self.invoke(self.root / "private")
        self.assertNotEqual(0, result.returncode); self.assertFalse((self.root / "private").exists())
        self.assertTrue(all((self.root / name).exists() for name in NAMES))

    def test_mismatched_existing_private_copy_is_never_overwritten(self):
        self.private.mkdir(); target = self.private / NAMES[0]; target.write_bytes(b"preserve private evidence")
        result = self.invoke(); self.assertNotEqual(0, result.returncode)
        self.assertEqual(b"preserve private evidence", target.read_bytes())
        self.assertTrue(all((self.root / name).exists() for name in NAMES))


if __name__ == "__main__":
    unittest.main()
