"""Offline checks for production approval and rollback smoke gates (no Azure access)."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest


ROOT = Path(__file__).resolve().parents[2]
PUBLISH = ROOT / ".github/workflows/publish-container.yml"
ROLLBACK = ROOT / ".github/workflows/rollback-production.yml"


def job_body(path, name):
    match = re.search(rf"^  {re.escape(name)}:\n(.*?)(?=^  [\w-]+:|\Z)",
                      path.read_text(encoding="utf-8"), re.M | re.S)
    if not match:
        raise AssertionError(f"Missing job: {name}")
    return match.group(1)


class ProductionApprovalTests(unittest.TestCase):
    def test_each_production_azure_identity_is_environment_protected(self):
        for path, jobs in [(PUBLISH, ("migrate-production", "deploy-api", "deploy-functions", "deploy-function-app")),
                           (ROLLBACK, ("rollback",))]:
            for name in jobs:
                with self.subTest(workflow=path.name, job=name):
                    body = job_body(path, name)
                    self.assertTrue(re.search(r"(?m)^    environment: production$", body),
                                    f"{name} must use production approval and OIDC context")


class RollbackSmokeTests(unittest.TestCase):
    def run_smoke(self, scenario):
        source = ROLLBACK.read_text(encoding="utf-8")
        # Execute the real workflow run block; replace external commands with shell functions.
        section = source.split("      - name: Label and smoke test rollback target", 1)
        self.assertEqual(2, len(section), "Rollback smoke step must be present")
        match = re.search(r"        run: \|\n((?:          .*\n|\n)+)", section[1])
        self.assertIsNotNone(match)
        script = textwrap.dedent(match.group(1))
        bash = shutil.which("bash") if os.name != "nt" else str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe")
        self.assertTrue(bash and Path(bash).exists(), "Bash is required for offline rollback tests")
        stubs = '''
az() {
  if [ "$SCENARIO" = "label_failure" ]; then return 93; fi
  if [ "$1 $2 $3 $4" = "containerapp revision label add" ]; then return 0; fi
  echo "Unexpected Azure command" >&2; exit 99
}
sleep() { :; }
curl() {
  case "$SCENARIO" in
    healthy) printf 200 ;;
    unavailable) printf 503 ;;
    timeout) printf 000; return 28 ;;
    live_only) if [[ "${!#}" == */livez ]]; then printf 200; else printf 503; fi ;;
    ready_only) if [[ "${!#}" == */healthz ]]; then printf 200; else printf 503; fi ;;
    *) exit 98 ;;
  esac
}
'''
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            env = dict(os.environ, SCENARIO=scenario, TARGET_FQDN="offline.invalid",
                       API_NAME="offline-app", API_RESOURCE_GROUP="offline-group",
                       TARGET_REVISION="offline-previous", TARGET_LABEL="green",
                       GITHUB_OUTPUT=output.as_posix())
            result = subprocess.run([bash, "-e", "-c", stubs + script], env=env,
                                    capture_output=True, text=True, timeout=10)
            return result, output.read_text() if output.exists() else ""

    def test_healthy_target_passes(self):
        result, output = self.run_smoke("healthy")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("result=passed", output)

    def test_unavailable_target_blocks_rollback(self):
        result, output = self.run_smoke("unavailable")
        self.assertEqual(6, result.returncode, result.stderr)
        self.assertNotIn("result=passed", output)
        self.assertIn("Production traffic was not changed", result.stdout)
        self.assertEqual(10, result.stdout.count("Rollback smoke attempt"))

    def test_curl_timeout_blocks_rollback(self):
        result, output = self.run_smoke("timeout")
        self.assertEqual(6, result.returncode, result.stderr)
        self.assertNotIn("result=passed", output)
        self.assertIn("live=000, ready=000", result.stdout)

    def test_both_liveness_and_readiness_are_required(self):
        for scenario in ("live_only", "ready_only"):
            with self.subTest(scenario=scenario):
                result, output = self.run_smoke(scenario)
                self.assertEqual(6, result.returncode, result.stderr)
                self.assertNotIn("result=passed", output)

    def test_label_failure_blocks_smoke(self):
        result, output = self.run_smoke("label_failure")
        self.assertEqual(93, result.returncode, result.stderr)
        self.assertNotIn("result=passed", output)


if __name__ == "__main__":
    unittest.main()
