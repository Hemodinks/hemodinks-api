"""Exercise the TRX gate with real subprocesses, including false-green inputs."""
from pathlib import Path
import importlib.util
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
GATE = ROOT / "scripts/check-test-results.py"
SPEC = importlib.util.spec_from_file_location("test_results_gate", GATE)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)
SCENARIOS = ("individual", "selection", "pin", "invalid-pin", "anonymous",
             "anonymous-write", "cross-tenant", "operator-swap", "replay", "cancel",
             "layout", "bootstrap-lock")


class TestResultsGateTests(unittest.TestCase):
    def run_gate(self, outcomes, *, exists=True, suite="login-e2e", duplicate_ids=False):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "results.trx"
            if exists:
                root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
                results = ET.SubElement(root, "Results")
                for index, (scenario, outcome) in enumerate(outcomes):
                    name = (f'HemodinksAPI.Tests.LoginBrowserTests.Browser_UsesRealApi(scenario: "{scenario}")'
                            if suite == "login-e2e" else scenario)
                    ET.SubElement(results, "UnitTestResult", testName=name, outcome=outcome,
                                  testId=f"test-{0 if duplicate_ids else index}")
                ET.ElementTree(root).write(path)
            return subprocess.run([sys.executable, str(GATE), "--suite", suite, str(path)],
                                  capture_output=True, text=True)

    def test_complete_browser_suite_passes(self):
        self.assertEqual(0, self.run_gate([(s, "Passed") for s in SCENARIOS]).returncode)

    def test_missing_file_fails(self):
        self.assertNotEqual(0, self.run_gate([], exists=False).returncode)

    def test_zero_tests_fails(self):
        self.assertNotEqual(0, self.run_gate([]).returncode)

    def test_skipped_test_fails(self):
        self.assertNotEqual(0, self.run_gate([(s, "NotExecuted" if s == "replay" else "Passed") for s in SCENARIOS]).returncode)

    def test_failed_test_fails(self):
        self.assertNotEqual(0, self.run_gate([(s, "Failed" if s == "cross-tenant" else "Passed") for s in SCENARIOS]).returncode)

    def test_missing_scenario_fails(self):
        self.assertNotEqual(0, self.run_gate([(s, "Passed") for s in SCENARIOS[:-1]]).returncode)

    def test_duplicate_cannot_replace_required_scenario(self):
        self.assertNotEqual(0, self.run_gate([(s, "Passed") for s in (*SCENARIOS[:-1], "pin")]).returncode)

    def test_api_requires_the_five_sql_tests(self):
        self.assertEqual(0, self.run_gate([(s, "Passed") for s in gate.SQL_REQUIRED], suite="api").returncode)
        self.assertNotEqual(0, self.run_gate([("SomeOtherPassingTest", "Passed")], suite="api").returncode)

    def test_api_rejects_skips_outside_the_five_required_tests(self):
        cases = [(s, "Passed") for s in gate.SQL_REQUIRED] + [("AnotherCriticalTest", "NotExecuted")]
        self.assertNotEqual(0, self.run_gate(cases, suite="api").returncode)

    def test_api_cannot_silently_include_browser_skips(self):
        cases = [(s, "Passed") for s in gate.SQL_REQUIRED] + [(next(iter(gate.BROWSER_REQUIRED)), "Passed")]
        self.assertNotEqual(0, self.run_gate(cases, suite="api").returncode)

    def test_api_allows_truncated_theory_display_names_with_distinct_ids(self):
        cases = [(s, "Passed") for s in gate.SQL_REQUIRED] + [("LongInlineData(...)", "Passed")] * 2
        self.assertEqual(0, self.run_gate(cases, suite="api").returncode)

    def test_duplicate_ids_fail_even_with_all_required_names(self):
        self.assertNotEqual(0, self.run_gate([(s, "Passed") for s in SCENARIOS], duplicate_ids=True).returncode)


if __name__ == "__main__":
    unittest.main()
