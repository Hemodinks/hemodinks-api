"""Regras do gate de vulnerabilidades NuGet e cobertura do Dependabot (#286)."""
from datetime import date
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("gate", ROOT / "scripts/check_nuget_vulnerabilities.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)

TODAY = date(2026, 11, 16)


def report(*packages, transitive=()):
    def entry(package_id, severity, advisory):
        return {"id": package_id, "resolvedVersion": "1.0.0",
                "vulnerabilities": [{"severity": severity,
                                     "advisoryurl": f"https://github.com/advisories/{advisory}"}]}
    return {"version": 1, "projects": [{
        "path": "C:/repo/HemodinksAPI.Api/HemodinksAPI.Api.csproj",
        "frameworks": [{"framework": "net10.0",
                        "topLevelPackages": [entry(*p) for p in packages],
                        "transitivePackages": [entry(*p) for p in transitive]}]}]}


def exception(**overrides):
    entry = {"advisory": "GHSA-aaaa-bbbb-cccc", "package": "Pkg.A", "reason": "sem correção",
             "issue": "#999", "expires": "2026-12-01"}
    entry.update(overrides)
    return entry


class NuGetVulnerabilityGateTests(unittest.TestCase):
    def test_clean_report_passes(self):
        self.assertEqual(([], [], []), gate.evaluate(report(), [], TODAY))

    def test_high_and_critical_block_including_transitive(self):
        blocking, _, _ = gate.evaluate(
            report(("Pkg.A", "High", "GHSA-aaaa-bbbb-cccc"),
                   transitive=[("Pkg.B", "Critical", "GHSA-dddd-eeee-ffff")]), [], TODAY)
        self.assertEqual({"Pkg.A", "Pkg.B"}, {finding[1] for finding in blocking})

    def test_low_and_moderate_do_not_block(self):
        blocking, _, _ = gate.evaluate(
            report(("Pkg.A", "Low", "GHSA-1"), transitive=[("Pkg.B", "Moderate", "GHSA-2")]), [], TODAY)
        self.assertEqual([], blocking)

    def test_valid_exception_accepts_only_that_advisory_and_package(self):
        blocking, accepted, errors = gate.evaluate(
            report(("Pkg.A", "High", "GHSA-aaaa-bbbb-cccc"), ("Pkg.C", "High", "GHSA-aaaa-bbbb-cccc")),
            [exception()], TODAY)
        self.assertEqual([], errors)
        self.assertEqual(["Pkg.A"], [finding[1] for finding in accepted])
        self.assertEqual(["Pkg.C"], [finding[1] for finding in blocking])

    def test_expired_exception_blocks(self):
        blocking, accepted, errors = gate.evaluate(
            report(("Pkg.A", "High", "GHSA-aaaa-bbbb-cccc")), [exception(expires="2026-11-15")], TODAY)
        self.assertEqual(1, len(blocking))
        self.assertEqual([], accepted)
        self.assertEqual(1, len(errors))

    def test_incomplete_long_or_malformed_exception_is_an_error(self):
        for entry in (exception(reason=""), exception(issue=None), exception(expires="01/12/2026"),
                      exception(expires="2027-06-01")):
            with self.subTest(entry=entry):
                _, _, errors = gate.evaluate(report(), [entry], TODAY)
                self.assertEqual(1, len(errors))

    def test_report_without_projects_fails_closed(self):
        with self.assertRaises(ValueError):
            gate.evaluate({"version": 1, "projects": []}, [], TODAY)

    def test_main_exit_codes(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            allowlist = folder / "allowlist.json"
            allowlist.write_text("[]", encoding="utf-8")
            cases = {
                "clean": (report(), 0),
                "vulnerable": (report(("Pkg.A", "Critical", "GHSA-x")), 1),
                "problems": ({**report(), "problems": [{"level": "error", "text": "offline"}]}, 1),
            }
            for name, (content, expected) in cases.items():
                with self.subTest(name=name):
                    path = folder / f"{name}.json"
                    path.write_text(json.dumps(content), encoding="utf-8")
                    self.assertEqual(expected, gate.main(["gate", str(path), str(allowlist)]))
            unreadable = folder / "broken.json"
            unreadable.write_text("not json", encoding="utf-8")
            self.assertEqual(1, gate.main(["gate", str(unreadable), str(allowlist)]))

    def test_committed_allowlist_is_valid(self):
        entries = json.loads((ROOT / ".github/nuget-vulnerability-allowlist.json").read_text(encoding="utf-8"))
        self.assertIsInstance(entries, list)
        _, errors = gate.load_allowlist(entries, date.today())
        self.assertEqual([], errors)

    def test_dependabot_updates_nuget_into_developer(self):
        config = (ROOT / ".github/dependabot.yml").read_text(encoding="utf-8")
        nuget = config.split("package-ecosystem: nuget", 1)
        self.assertEqual(2, len(nuget), "dependabot.yml must declare the nuget ecosystem")
        block = nuget[1].split("- package-ecosystem:", 1)[0]
        self.assertIn("target-branch: developer", block)
        self.assertIn("interval: weekly", block)
        self.assertIn("groups:", block)


if __name__ == "__main__":
    unittest.main()
