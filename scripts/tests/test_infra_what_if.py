"""Testes do avaliador de what-if da infraestrutura (#288)."""
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import infra_what_if as w  # noqa: E402

SUB = "/subscriptions/00000000-0000-0000-0000-000000000000"
SITE = f"{SUB}/resourceGroups/rg/providers/Microsoft.Web/sites/func"
SWA = f"{SUB}/resourceGroups/rg/providers/Microsoft.Web/staticSites/front"
DB = f"{SUB}/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/HemodinksDB"


def result(*changes, status="Succeeded"):
    return {"status": status, "error": None, "changes": list(changes)}


def modify(resource_id, *delta):
    return {"changeType": "Modify", "resourceId": resource_id, "delta": list(delta)}


def prop(path, kind, value="segredo-que-nao-pode-vazar"):
    return {"path": path, "propertyChangeType": kind, "before": None, "after": value}


class FindDriftTests(unittest.TestCase):
    def test_no_change_and_ignore_are_clean(self):
        data = result({"changeType": "NoChange", "resourceId": DB},
                      {"changeType": "Ignore", "resourceId": SITE})
        self.assertEqual([], w.find_drift(data))

    def test_known_noise_is_ignored(self):
        data = result(
            modify(SITE, prop("properties.siteConfig.localMySqlEnabled", "Create"),
                   prop("properties.siteConfig.netFrameworkVersion", "Create")),
            modify(SWA, prop("properties.stableInboundIP", "Delete"),
                   prop("properties.trafficSplitting", "Delete")))
        self.assertEqual([], w.find_drift(data))

    def test_noeffect_is_ignored(self):
        data = result(modify(DB, prop("sku.capacity", "NoEffect")))
        self.assertEqual([], w.find_drift(data))

    def test_real_property_change_is_drift(self):
        data = result(modify(DB, prop("sku.name", "Modify")))
        self.assertEqual(["Modify Microsoft.Sql/servers/databases/srv/HemodinksDB: sku.name (Modify)"],
                         w.find_drift(data))

    def test_noise_path_with_other_change_type_is_drift(self):
        # A allowlist cobre o tipo de mudanca exato: apagar netFrameworkVersion nao e ruido.
        data = result(modify(SITE, prop("properties.siteConfig.netFrameworkVersion", "Delete")))
        self.assertEqual(1, len(w.find_drift(data)))

    def test_noise_path_on_other_resource_type_is_drift(self):
        data = result(modify(DB, prop("properties.stableInboundIP", "Delete")))
        self.assertEqual(1, len(w.find_drift(data)))

    def test_create_delete_and_unsupported_are_drift(self):
        for kind in ("Create", "Delete", "Deploy", "Unsupported"):
            with self.subTest(kind=kind):
                drift = w.find_drift(result({"changeType": kind, "resourceId": DB}))
                self.assertEqual([f"{kind} Microsoft.Sql/servers/databases/srv/HemodinksDB"], drift)

    def test_nested_children_are_flattened(self):
        data = result(modify(SITE, {"path": "properties.siteConfig", "propertyChangeType": "Modify",
                                    "children": [prop("alwaysOn", "Modify")]}))
        self.assertEqual(["Modify Microsoft.Web/sites/func: properties.siteConfig.alwaysOn (Modify)"],
                         w.find_drift(data))

    def test_failed_what_if_is_an_error(self):
        with self.assertRaises(w.WhatIfError):
            w.find_drift(result(status="Failed"))
        with self.assertRaises(w.WhatIfError):
            w.find_drift({"status": "Succeeded", "error": {"code": "AuthorizationFailed"}, "changes": []})
        with self.assertRaises(w.WhatIfError):
            w.find_drift({"status": "Succeeded"})


class MainTests(unittest.TestCase):
    def run_main(self, data):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as handle:
            json.dump(data, handle)
        out, err = io.StringIO(), io.StringIO()
        try:
            with redirect_stdout(out), redirect_stderr(err):
                code = w.main(["infra_what_if.py", handle.name])
        finally:
            Path(handle.name).unlink()
        return code, out.getvalue() + err.getvalue()

    def test_exit_codes(self):
        self.assertEqual(0, self.run_main(result())[0])
        self.assertEqual(1, self.run_main(result(modify(DB, prop("sku.name", "Modify"))))[0])
        self.assertEqual(2, self.run_main({"changes": [], "status": "Failed"})[0])

    def test_output_never_contains_values(self):
        code, output = self.run_main(result(modify(DB, prop("properties.x", "Modify"))))
        self.assertEqual(1, code)
        self.assertIn("properties.x", output)
        self.assertNotIn("segredo-que-nao-pode-vazar", output)


if __name__ == "__main__":
    unittest.main()
