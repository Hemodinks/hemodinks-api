"""Offline checks for production approval and rollback smoke gates (no Azure access)."""
import os
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest

from scripts import forwarded_headers


ROOT = Path(__file__).resolve().parents[2]
PUBLISH = ROOT / ".github/workflows/publish-container.yml"
ROLLBACK = ROOT / ".github/workflows/rollback-production.yml"
APPLY_MIGRATIONS = ROOT / ".github/workflows/apply-production-migrations.yml"


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


    def test_production_mutating_jobs_require_main_branch(self):
        cases = (
            (PUBLISH, ("migrate-production", "deploy-api", "deploy-functions", "deploy-function-app")),
            (APPLY_MIGRATIONS, ("migrate",)),
            (ROLLBACK, ("rollback",)),
        )
        for path, jobs in cases:
            for name in jobs:
                with self.subTest(workflow=path.name, job=name):
                    conditions = re.findall(r"(?m)^    if: (.+)$", job_body(path, name))
                    self.assertEqual(1, len(conditions), f"{name} must have a job-level guard")
                    self.assertIn("github.ref == 'refs/heads/main'", conditions[0])

    def test_manual_image_and_migration_artifact_publication_requires_main(self):
        for name in ("publish", "prepare-migrations"):
            with self.subTest(job=name):
                conditions = re.findall(r"(?m)^    if: (.+)$", job_body(PUBLISH, name))
                self.assertEqual(1, len(conditions))
                self.assertIn("github.event_name != 'workflow_dispatch'", conditions[0])
                self.assertIn("github.ref == 'refs/heads/main'", conditions[0])
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


class PublishRevisionReuseTests(unittest.TestCase):
    SHA = "a" * 40
    IMAGE = "ghcr.io/test/api@sha256:" + "b" * 64

    def run_step(self, step, *, same_revision=True, color="blue", mismatch=None,
                 scenario="healthy", already_current="true"):
        source = PUBLISH.read_text(encoding="utf-8")
        section = source.split(f"      - name: {step}\n", 1)
        self.assertEqual(2, len(section), f"Missing workflow step {step}")
        match = re.search(r"        run: \|\n((?:          .*\n|\n)+)", section[1])
        self.assertIsNotNone(match)
        bash = (shutil.which("bash") if os.name != "nt" else
                str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"))
        self.assertTrue(shutil.which("jq"), "jq is required to execute the real workflow checks")
        current = "hemodinks-api-prod--sha-" + (self.SHA[:10] if same_revision else "previous")
        candidate = "hemodinks-api-prod--sha-" + self.SHA[:10]
        approved = forwarded_headers.build_environment('["10.20.0.4"]', '[]')
        environment = approved + [{"name": "Deployment__CommitSha", "value": self.SHA}]
        if mismatch == "sha":
            environment[-1]["value"] = self.SHA[:10] + "c" * 30
        if mismatch == "proxy":
            environment = [entry for entry in environment if entry["name"] != "ForwardedHeaders__ForwardLimit"]
        revision = {"properties": {"healthState": "Healthy", "runningState": "Running",
                                  "provisioningState": "Succeeded", "template": {"containers": [{
                                      "name": "api", "image": "wrong-image" if mismatch == "image" else self.IMAGE,
                                      "env": environment}]}}}
        if scenario == "unhealthy":
            revision["properties"]["healthState"] = "Unhealthy"
        app = {"name": "hemodinks-api-prod", "resourceGroup": "rg-hemodinks-prod", "properties": {
            "configuration": {"ingress": {"transport": "Auto", "targetPort": 8080,
                "allowInsecure": False, "additionalPortMappings": None,
                "fqdn": "hemodinks-api-prod.test.brazilsouth.azurecontainerapps.io"}}}}
        traffic = [{"label": color, "revisionName": current, "weight": 100}]
        if scenario == "drift":
            traffic[0]["revisionName"] = "unexpected-revision"
        stubs = r'''
python3() { "$TEST_PYTHON" "$@"; }
sleep() { SECONDS=1000; }
timeout() { shift; "$@"; }
curl() {
  case "$SCENARIO" in
    live_only) if [[ "${!#}" == */livez ]]; then printf 200; else printf 503; fi ;;
    ready_only) if [[ "${!#}" == */healthz ]]; then printf 200; else printf 503; fi ;;
    timeout) printf 000; return 28 ;;
    *) printf 200 ;;
  esac
}
az() {
  case "$1 $2 $3" in
    "containerapp revision show")
      if [ "$SAME_REVISION" = false ] && [[ "$*" == *"--revision $EXPECTED_CANDIDATE"* ]]; then return 1; fi
      cat "$FIXTURE/revision.json" ;;
    "containerapp show --name")
      case "$*" in
        *"properties.configuration.ingress.traffic"*) cat "$FIXTURE/traffic.json" ;;
        *"properties.configuration.ingress.fqdn"*) printf 'hemodinks-api-prod.test.brazilsouth.azurecontainerapps.io' ;;
        *"properties.latestRevisionName"*) printf '%s' "$EXPECTED_CANDIDATE" ;;
        *"properties.provisioningState"*) printf Succeeded ;;
        *"properties.configuration.ingress.targetPort"*) printf 8080 ;;
        *"--query id"*) printf /test/resource ;;
        *) cat "$FIXTURE/app.json" ;;
      esac ;;
    "containerapp secret set"|"containerapp revision label"|"containerapp ingress traffic")
      printf '%s\n' "$*" >> "$FIXTURE/mutations"
      if [[ "$*" == *"--label-weight $CANDIDATE_LABEL=100"* || "$*" == *"$ACTIVE_LABEL=0 $CANDIDATE_LABEL=100"* ]]; then
        printf '[{"label":"%s","revisionName":"%s","weight":100}]' "$CANDIDATE_LABEL" "$CANDIDATE_REVISION" > "$FIXTURE/traffic.json"
      fi ;;
    *) if [ "$1" = rest ]; then printf 'patch\n' >> "$FIXTURE/mutations"; else echo "Unexpected Azure call: $*" >&2; return 99; fi ;;
  esac
}
'''
        with tempfile.TemporaryDirectory() as directory:
            fixture = Path(directory)
            for name, data in (("revision", revision), ("app", app), ("traffic", traffic)):
                (fixture / f"{name}.json").write_text(json.dumps(data), encoding="utf-8")
            env = dict(os.environ, FIXTURE=fixture.as_posix(), TEST_PYTHON=Path(sys.executable).as_posix(),
                       SCENARIO=scenario, SAME_REVISION=str(same_revision).lower(),
                       GITHUB_SHA=self.SHA, API_IMAGE=self.IMAGE, API_NAME=app["name"],
                       API_RESOURCE_GROUP=app["resourceGroup"], NEW_RELIC_LICENSE_KEY="synthetic-key",
                       FORWARDED_HEADERS_ENV=json.dumps(approved), EXPECTED_CANDIDATE=candidate,
                       ACTIVE_LABEL=color, CANDIDATE_LABEL=color if same_revision else "green",
                       PREVIOUS_REVISION=current, CANDIDATE_REVISION=candidate,
                       CANDIDATE_FQDN=app["properties"]["configuration"]["ingress"]["fqdn"],
                       ALREADY_CURRENT=already_current, GITHUB_OUTPUT=(fixture / "output").as_posix())
            script_file = fixture / "step.sh"
            script_file.write_text(stubs + textwrap.dedent(match[1]), encoding="utf-8", newline="\n")
            result = subprocess.run([bash, "-e", script_file.as_posix()],
                                    cwd=ROOT, env=env, capture_output=True, text=True, timeout=20)
            read = lambda name: (fixture / name).read_text() if (fixture / name).exists() else ""
            return result, read("output"), read("mutations")

    def test_current_commit_preserves_both_possible_active_labels_without_any_mutation(self):
        for color in ("blue", "green"):
            with self.subTest(color=color):
                result, output, mutations = self.run_step("Create candidate revision with zero traffic", color=color)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual("", mutations)
                self.assertIn("already_current=true", output)
                self.assertIn(f"candidate_label={color}", output)

    def test_current_commit_rejects_image_full_sha_or_proxy_mismatch_without_mutation(self):
        for mismatch in ("image", "sha", "proxy"):
            with self.subTest(mismatch=mismatch):
                result, output, mutations = self.run_step("Create candidate revision with zero traffic", mismatch=mismatch)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn("already_current=true", output)
                self.assertEqual("", mutations)

    def test_new_commit_still_creates_and_labels_a_zero_traffic_candidate(self):
        result, output, mutations = self.run_step("Create candidate revision with zero traffic", same_revision=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("patch", mutations)
        self.assertIn("revision label add", mutations)
        self.assertIn("--label-weight blue=100 green=0", mutations)
        self.assertIn("already_current=false", output)

    def test_current_commit_promotion_only_verifies_traffic(self):
        result, output, mutations = self.run_step("Promote candidate and verify Production traffic")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", mutations)
        self.assertIn("result=already-current", output)

    def test_current_commit_rejects_traffic_drift_without_mutation(self):
        result, output, mutations = self.run_step("Promote candidate and verify Production traffic", scenario="drift")
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("result=already-current", output)
        self.assertEqual("", mutations)

    def test_inconsistent_reuse_flag_and_revision_are_rejected_without_mutation(self):
        for same_revision, flag in ((True, "false"), (False, "true")):
            with self.subTest(same_revision=same_revision, flag=flag):
                result, output, mutations = self.run_step("Promote candidate and verify Production traffic",
                                                          same_revision=same_revision, already_current=flag)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn("result=already-current", output)
                self.assertEqual("", mutations)

    def test_current_commit_smoke_requires_health_full_sha_liveness_and_readiness(self):
        for scenario, mismatch in (("healthy", None), ("live_only", None), ("ready_only", None),
                                   ("timeout", None), ("unhealthy", None), ("healthy", "sha")):
            with self.subTest(scenario=scenario, mismatch=mismatch):
                result, output, mutations = self.run_step("Warm up and smoke test candidate", scenario=scenario, mismatch=mismatch)
                self.assertEqual(0 if scenario == "healthy" and mismatch is None else 7, result.returncode, result.stderr)
                self.assertEqual("", mutations)
                self.assertEqual(result.returncode == 0, "result=passed" in output)

    def test_new_commit_still_promotes_with_traffic_mutation(self):
        result, output, mutations = self.run_step("Promote candidate and verify Production traffic",
                                                  same_revision=False, already_current="false")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("--label-weight blue=0 green=100", mutations)
        self.assertIn("result=promoted", output)

    def test_revision_cleanup_skips_an_already_current_deployment(self):
        source = PUBLISH.read_text(encoding="utf-8")
        cleanup = source.split("      - name: Enforce CURRENT/PREVIOUS revision policy\n", 1)[1].split("        run:", 1)[0]
        self.assertIn("if: steps.candidate.outputs.already_current != 'true'", cleanup)


if __name__ == "__main__":
    unittest.main()
