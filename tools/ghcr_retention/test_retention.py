"""Offline tests: python -m unittest discover -s tools/ghcr_retention -p 'test_*.py'"""
import datetime as dt
import unittest
from retention import classify

NOW = dt.datetime(2026, 10, 9, tzinfo=dt.timezone.utc)
DIGEST = "sha256:" + "a" * 64

def inventory(n=7):
    return {
        "policy": {"dry_run": True, "delete_enabled": False,
                   "retention_days": 30, "keep_latest_versions": 5,
                   "package_allowlist": ["hemodinks-api"]},
        "ghcr": {"complete": True, "versions": [
            {"package": "hemodinks-api", "id": i, "digest": "sha256:" + format(i, "064x"),
             "tags": [], "created_at": f"2026-0{8 if i < 6 else 7}-01T00:00:00Z"}
            for i in range(1, n + 1)]},
        "azure": {"complete": True, "protected_digests": [], "unresolved_image_references": []}
    }

class RetentionTests(unittest.TestCase):
    def test_five_latest_protected(self):
        result = classify(inventory(), NOW)
        self.assertEqual(result["candidate_count"], 2)
        self.assertEqual(sum("PROTECTED_LATEST" in x["reasons"] for x in result["results"]), 5)

    def test_azure_digest_protected(self):
        data = inventory()
        data["azure"]["protected_digests"] = [data["ghcr"]["versions"][0]["digest"]]
        self.assertEqual(classify(data, NOW)["candidate_count"], 1)

    def test_incomplete_azure_blocks_everything(self):
        data = inventory()
        data["azure"]["complete"] = False
        self.assertEqual(classify(data, NOW)["candidate_count"], 0)

    def test_mutable_tag_unresolved_blocks_everything(self):
        data = inventory()
        data["azure"]["unresolved_image_references"] = ["ghcr.io/org/image:latest"]
        self.assertEqual(classify(data, NOW)["candidate_count"], 0)

    def test_unknown_package_preserved(self):
        data = inventory()
        for v in data["ghcr"]["versions"]:
            v["package"] = "worker"
        self.assertEqual(classify(data, NOW)["candidate_count"], 0)

    def test_delete_cannot_be_enabled(self):
        data = inventory()
        data["policy"]["delete_enabled"] = True
        with self.assertRaises(ValueError):
            classify(data, NOW)

    def test_invalid_digest_blocked(self):
        data = inventory()
        data["ghcr"]["versions"][0]["digest"] = "unknown"
        self.assertEqual(classify(data, NOW)["candidate_count"], 1)

    def test_ghcr_incomplete_blocks_all(self):
        data = inventory()
        data["ghcr"]["complete"] = False
        self.assertEqual(classify(data, NOW)["candidate_count"], 0)

if __name__ == "__main__":
    unittest.main()
