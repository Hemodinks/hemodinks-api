import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from weekly_audit import collect_azure, collect_ghcr, main

class InventoryTests(unittest.TestCase):
    @patch("weekly_audit.gh_pages")
    def test_ghcr_requires_digest(self, pages):
        pages.return_value = [{"id": 1, "name": "latest", "created_at": "2026-01-01T00:00:00Z", "metadata": {"container": {"tags": ["latest"]}}}]
        with self.assertRaises(ValueError):
            collect_ghcr("Hemodinks", "hemodinks-api")

    @patch("weekly_audit.run")
    def test_azure_inactive_tag_fails_closed(self, command):
        command.return_value = json.dumps([{"name": "revision-inactive", "properties": {
            "active": False, "template": {"containers": [{"image": "ghcr.io/hemodinks/api:latest"}]}
        }}])
        refs, unresolved, revisions = collect_azure("sub", "rg", "app")
        self.assertEqual(refs, [])
        self.assertEqual(unresolved, ["ghcr.io/hemodinks/api:latest"])
        self.assertEqual(revisions[0]["active"], False)

    @patch("weekly_audit.run")
    def test_azure_digest_protected(self, command):
        command.return_value = json.dumps([{"name": "green", "properties": {
            "active": True, "template": {"containers": [{"image": "ghcr.io/hemodinks/api@sha256:" + "f"*64}]}
        }}])
        refs, unknown, _ = collect_azure("sub", "rg", "app")
        self.assertEqual(refs, ["sha256:" + "f"*64])
        self.assertFalse(unknown)

    @patch("weekly_audit.run")
    def test_azure_missing_containers_rejected(self, command):
        command.return_value = json.dumps([{"name": "bad", "properties": {"template": {}}}])
        with self.assertRaises(ValueError):
            collect_azure("sub", "rg", "app")

    @patch.dict(os.environ, {}, clear=True)
    def test_missing_configuration_outputs_blocked_report(self):
        with tempfile.TemporaryDirectory() as temp:
            with patch("sys.argv", ["weekly_audit", "--output", temp]):
                self.assertEqual(main(), 2)
            report = json.loads((Path(temp) / "ghcr-retention-report.json").read_text())
            self.assertEqual(report["status"], "BLOCKED")
            self.assertEqual(report["classification"]["candidate_count"], 0)
            self.assertFalse(report["classification"]["inventory_complete"])

    def test_no_delete_commands_exist_in_script(self):
        source = Path(__file__).with_name("weekly_audit.py").read_text()
        self.assertNotIn('"DELETE"', source)
        self.assertNotIn('"delete"', source)
        self.assertNotIn("gh api --method DELETE", source)

if __name__ == "__main__":
    unittest.main()
