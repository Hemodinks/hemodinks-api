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


DEPLOY_SCRIPTS = ROOT / "scripts/deploy"


def deploy_step_script(step):
    """Return the versioned script that a deploy-api step runs (#229)."""
    section = job_body(PUBLISH, "deploy-api").split(f"      - name: {step}\n", 1)
    if len(section) != 2:
        raise AssertionError(f"Missing deploy-api step {step}")
    match = re.search(r"^        run: bash (scripts/deploy/[\w.-]+\.sh)$", section[1].split("      - name:", 1)[0], re.M)
    if not match:
        raise AssertionError(f"Step {step} must run a script from scripts/deploy")
    return (ROOT / match.group(1)).read_text(encoding="utf-8")


class DeployScriptExtractionTests(unittest.TestCase):
    """#229: deploy-api bash lives in scripts/deploy, linted by shellcheck in CI."""

    def test_deploy_api_has_no_long_inline_bash(self):
        body = job_body(PUBLISH, "deploy-api")
        for block in re.findall(r"        run: \|\n((?:          .*\n)+)", body):
            with self.subTest(block=block.splitlines()[0]):
                self.assertLessEqual(len(block.splitlines()), 3, "Move inline bash to scripts/deploy")

    def test_every_deploy_script_is_used_exactly_once_and_exists(self):
        body = job_body(PUBLISH, "deploy-api")
        referenced = re.findall(r"run: bash scripts/deploy/([\w.-]+\.sh)$", body, re.M)
        self.assertEqual(sorted(set(referenced)), sorted(referenced))
        self.assertEqual(sorted(p.name for p in DEPLOY_SCRIPTS.glob("*.sh")), sorted(referenced))

    def test_scripts_keep_github_run_shell_options_and_no_expressions(self):
        for script in sorted(DEPLOY_SCRIPTS.glob("*.sh")):
            with self.subTest(script=script.name):
                text = script.read_text(encoding="utf-8")
                self.assertTrue(text.startswith("#!/usr/bin/env bash\n"))
                self.assertNotIn("\r", text)
                self.assertNotIn("${{", text)
                first_command = next(line for line in text.splitlines() if line and not line.startswith("#"))
                self.assertRegex(first_command, r"^set -e(u)?o pipefail$")

    def test_missing_step_or_inline_step_is_rejected(self):
        with self.assertRaises(AssertionError):
            deploy_step_script("Step that does not exist")
        with self.assertRaises(AssertionError):
            deploy_step_script("Enforce CURRENT/PREVIOUS revision policy")

    def test_ci_runs_shellcheck_on_deploy_scripts(self):
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("shellcheck scripts/deploy/*.sh scripts/Enforce-ProductionRevisionPolicy.sh", ci)


class ProductionApprovalTests(unittest.TestCase):
    def test_each_production_azure_identity_is_environment_protected(self):
        for path, jobs in [(PUBLISH, ("migrate-production", "deploy-api", "deploy-function-app")),
                           (ROLLBACK, ("rollback",))]:
            for name in jobs:
                with self.subTest(workflow=path.name, job=name):
                    body = job_body(path, name)
                    self.assertTrue(re.search(r"(?m)^    environment: production$", body),
                                    f"{name} must use production approval and OIDC context")


    def test_production_mutating_jobs_require_main_branch(self):
        cases = (
            (PUBLISH, ("migrate-production", "deploy-api", "deploy-function-app")),
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


def job_condition(name):
    conditions = re.findall(r"(?m)^    if: (.+)$", job_body(PUBLISH, name))
    if len(conditions) != 1:
        raise AssertionError(f"{name} must have exactly one job-level guard")
    return conditions[0]


class ReleaseReuseWorkflowTests(unittest.TestCase):
    """#278: deploy reuses the validated push run; push skips tests only with a tree proof."""

    def test_deploy_jobs_depend_only_on_the_reused_release(self):
        migrate = job_body(PUBLISH, "migrate-production")
        self.assertRegex(migrate, r"(?m)^    needs: release-source$")
        self.assertIn("needs.release-source.outputs.mode == 'reuse'", job_condition("migrate-production"))
        self.assertIn("run-id: ${{ needs.release-source.outputs.source_run_id }}", migrate)
        self.assertRegex(migrate, r"(?m)^      actions: read$")
        self.assertRegex(migrate, r"(?m)^      id-token: write$")
        deploy = job_body(PUBLISH, "deploy-api")
        self.assertIn("needs: [release-source, migrate-production]", deploy)
        self.assertIn("API_IMAGE: ${{ needs.release-source.outputs.api_image }}", deploy)

    def test_no_mutable_tag_is_deployed(self):
        source = PUBLISH.read_text(encoding="utf-8")
        self.assertNotRegex(source, r"(?m)^  API_IMAGE:")
        self.assertIn("API_IMAGE must be the validated", deploy_step_script("Create candidate revision with zero traffic"))

    def test_build_jobs_never_run_in_reuse_mode_and_require_tests_or_proof(self):
        for name in ("publish", "prepare-migrations"):
            with self.subTest(job=name):
                condition = job_condition(name)
                self.assertIn("needs.release-source.outputs.mode == 'build'", condition)
                self.assertIn("needs.release-source.result == 'success'", condition)
                self.assertIn("needs.validate.result == 'success'", condition)
                self.assertIn("needs.release-source.outputs.run_tests == 'false' && needs.validate.result == 'skipped'",
                              condition)
        self.assertIn("needs.login-e2e.result == 'success'", job_condition("publish"))

    def test_tests_are_skipped_only_by_release_source(self):
        for name in ("validate", "login-e2e"):
            with self.subTest(job=name):
                self.assertEqual("${{ needs.release-source.outputs.run_tests == 'true' }}", job_condition(name))

    def test_release_source_has_minimal_permissions_and_validates_digest(self):
        body = job_body(PUBLISH, "release-source")
        self.assertRegex(body, r"(?m)^      contents: read\n      actions: read\n")
        self.assertNotIn("id-token", body)
        self.assertNotIn("packages", body)
        self.assertIn("scripts/release_source.py --image-file release/image.txt", body)
        self.assertIn("persist-credentials: false", body)
        publish = job_body(PUBLISH, "publish")
        self.assertIn("name: release-image-${{ github.sha }}", publish)
        self.assertIn("scripts/release_source.py --image-file release/image.txt", publish)

    def test_ci_records_tree_only_for_main_pull_requests_after_tests(self):
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        steps = ci.split("  build-test:\n", 1)[1]
        record = steps.index("      - name: Record tested tree for Publish Production")
        # O portao so grava a prova depois de exigir as duas partes de teste verdes e o resultado combinado.
        self.assertIn("    needs: [scope, build-test-part]\n", steps)
        self.assertIn('if [ "$PARTS_RESULT" != "success" ]; then', steps)
        for earlier in ("      - name: Require both test parts", "      - name: Require critical SQL tests and zero silent skips",
                        "      - name: Test result gate regressions"):
            with self.subTest(step=earlier.strip()):
                self.assertLess(steps.index(earlier), record)
        parts = ci.split("  build-test-part:\n", 1)[1].split("  build-test:\n", 1)[0]
        self.assertIn("      - name: Test\n", parts)
        self.assertEqual(2, ci.count("if: github.event_name == 'pull_request' && github.base_ref == 'main'"))
        self.assertIn("name: ci-tested-tree-${{ env.TESTED_TREE }}", ci)

    def test_ci_covers_every_offline_check_of_publish_validation(self):
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        validate = job_body(PUBLISH, "validate")
        for check in re.findall(r"-p (test_[\w]+\.py)|(\./scripts/tests/[\w.-]+\.ps1)", validate):
            name = check[0] or check[1]
            with self.subTest(check=name):
                self.assertIn(name, ci)
        self.assertIn("Test-Migrations.ps1 -NoBuild -Configuration Release", ci)


class WorkersSingleDeployRouteTests(unittest.TestCase):
    """#228: Workers are deployed only as a code package to the Function App."""

    def test_container_apps_workers_route_is_removed(self):
        source = PUBLISH.read_text(encoding="utf-8")
        self.assertIsNone(re.search(r"(?m)^  deploy-functions:$", source))
        for removed in ("hemodinks-api-workers", "HemodinksAPI.Workers/Dockerfile",
                        "AZURE_CONTAINER_APP_FUNCTIONS_NAME", "AZURE_CONTAINER_APPS_DEPLOY_ENABLED"):
            with self.subTest(removed=removed):
                self.assertNotIn(removed, source)

    def test_exactly_one_job_deploys_workers(self):
        source = PUBLISH.read_text(encoding="utf-8")
        jobs = re.findall(r"(?m)^  ([\w-]+):\n", source.split("\njobs:\n", 1)[1])
        markers = ("HemodinksAPI.Workers", "Azure/functions-action@", "api-workers", "containerapp update")
        workers_jobs = [job for job in jobs if any(m in job_body(PUBLISH, job) for m in markers)]
        self.assertEqual(["deploy-function-app"], workers_jobs)
        self.assertEqual(1, source.count("Azure/functions-action@"))

    def test_function_app_job_is_not_silently_skipped_by_missing_target(self):
        condition = re.findall(r"(?m)^    if: (.+)$", job_body(PUBLISH, "deploy-function-app"))[0]
        self.assertNotIn("AZURE_FUNCTION_APP_WORKERS_NAME", condition)
        self.assertIn("migrate-production", job_body(PUBLISH, "deploy-function-app"))

    def run_guard(self, name):
        section = job_body(PUBLISH, "deploy-function-app").split("      - name: Require Workers Function App target\n", 1)
        self.assertEqual(2, len(section), "Workers target guard must be present")
        match = re.search(r"        run: \|\n((?:          .*\n)+)", section[1])
        self.assertIsNotNone(match)
        bash = (shutil.which("bash") if os.name != "nt" else
                str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"))
        self.assertTrue(bash and Path(bash).exists(), "Bash is required for offline workflow tests")
        env = dict(os.environ, WORKERS_FUNCTION_APP_NAME=name)
        return subprocess.run([bash, "-e", "-c", textwrap.dedent(match.group(1))], env=env,
                              capture_output=True, text=True, timeout=10)

    def test_guard_fails_when_function_app_target_is_missing(self):
        result = self.run_guard("")
        self.assertEqual(4, result.returncode, result.stderr)
        self.assertIn("AZURE_FUNCTION_APP_WORKERS_NAME is not set", result.stdout)

    def test_guard_passes_when_function_app_target_is_set(self):
        result = self.run_guard("offline-workers")
        self.assertEqual(0, result.returncode, result.stderr)


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


class ImageScanGateTests(unittest.TestCase):
    """#372: o publish escaneia o digest publicado, gera SBOM/SARIF e bloqueia CVE alta/critica corrigivel."""

    DIGEST_PIN = re.compile(r"TRIVY_IMAGE: aquasec/trivy@sha256:[0-9a-f]{64} # v\d+\.\d+\.\d+$", re.M)

    def publish(self):
        return job_body(PUBLISH, "publish")

    def test_trivy_runs_from_digest_pinned_image_not_third_party_action(self):
        publish = self.publish()
        self.assertEqual(2, len(self.DIGEST_PIN.findall(publish)), "scan and gate must pin the Trivy image by digest")
        self.assertNotIn("aquasecurity/trivy-action", PUBLISH.read_text(encoding="utf-8"))
        self.assertIn("IMAGE_REF: ${{ matrix.image }}@${{ steps.build.outputs.digest }}", publish)

    def test_gate_blocks_fixable_high_and_critical_before_digest_is_released(self):
        publish = self.publish()
        gate_at = publish.index("- name: Fail on fixable HIGH/CRITICAL image vulnerabilities")
        gate = publish[gate_at:publish.index("- name:", gate_at + 10)]
        for flag in ("--severity HIGH,CRITICAL", "--ignore-unfixed", "--ignorefile /src/.trivyignore", "--exit-code 1"):
            with self.subTest(flag=flag):
                self.assertIn(flag, gate)
        self.assertNotIn("continue-on-error", gate)
        self.assertLess(publish.index("- name: Upload Trivy results to Code Scanning"), gate_at)
        self.assertLess(gate_at, publish.index("- name: Record validated image digest"))

    def test_sbom_and_sarif_are_published_with_minimal_permissions(self):
        publish = self.publish()
        self.assertIn("--format spdx-json --output /out/sbom.spdx.json", publish)
        self.assertIn("name: sbom-${{ matrix.service }}-${{ github.sha }}", publish)
        self.assertIn("github/codeql-action/upload-sarif@", publish)
        permissions = re.search(r"\n    permissions:\n((?:      .*\n)+)", publish)
        self.assertIsNotNone(permissions)
        self.assertEqual(
            {"contents: read", "packages: write", "security-events: write"},
            {line.strip() for line in permissions.group(1).splitlines()})

    def test_trivy_allowlist_entries_are_justified(self):
        lines = (ROOT / ".trivyignore").read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if line.strip() and not line.lstrip().startswith("#"):
                with self.subTest(entry=line):
                    self.assertTrue(index > 0 and lines[index - 1].lstrip().startswith("#"),
                                    "each .trivyignore entry needs a justification comment right above it")
class ContainerImageHardeningTests(unittest.TestCase):
    """#371: a API roda sem root e a imagem nao leva logs ou arquivos locais do build."""

    def final_stage(self):
        source = (ROOT / "Dockerfile").read_text(encoding="utf-8")
        parts = re.split(r"(?m)^FROM .* AS final$", source)
        self.assertEqual(2, len(parts), "Dockerfile must keep a single 'final' stage")
        return parts[1]

    def test_final_stage_drops_root_after_preparing_writable_dirs(self):
        final = self.final_stage()
        users = re.findall(r"(?m)^USER\s+(\S+)\s*$", final)
        self.assertEqual(["$APP_UID"], users, "final stage must switch to the base image non-root user once")
        user_at = final.index("USER $APP_UID")
        chown_at = final.index('chown "$APP_UID" /app/logs /app/local-storage')
        self.assertLess(chown_at, user_at, "writable dirs must be prepared (as root) before dropping privileges")
        self.assertLess(user_at, final.index("ENTRYPOINT"), "the API process must start as the non-root user")
        # O chown so pode tocar os diretorios gravaveis, nunca /app inteiro (binario e agente New Relic).
        chown_lines = [line for line in final.splitlines() if "chown" in line]
        self.assertEqual(1, len(chown_lines))
        self.assertNotRegex(chown_lines[0], r"\s/app(\s|$)", "binaries and New Relic agent must stay root-owned")

    def test_build_context_excludes_logs_and_local_storage_at_any_depth(self):
        ignored = (ROOT / ".dockerignore").read_text(encoding="utf-8").splitlines()
        for pattern in ("**/logs", "**/*.log", "**/local-storage", "**/.env"):
            with self.subTest(pattern=pattern):
                self.assertIn(pattern, ignored)


class RollbackRuntimeCredentialTests(unittest.TestCase):
    """#204: rollback never targets a revision whose SQL connection is not the restricted runtime secret."""

    TRAFFIC = [
        {"revisionName": "app--current", "label": "blue", "weight": 100},
        {"revisionName": "app--previous", "label": "green", "weight": 0},
    ]
    REVISIONS = [
        {"name": "app--current", "properties": {"active": True, "healthState": "Healthy"}},
        {"name": "app--previous", "properties": {"active": True, "healthState": "Healthy"}},
    ]

    @staticmethod
    def template(*db_entries, extra_container=None):
        env = [{"name": "ASPNETCORE_ENVIRONMENT", "value": "Production"}, *db_entries]
        containers = [{"name": "api", "env": env}]
        if extra_container is not None:
            containers.append(extra_container)
        return {"containers": containers}

    def run_validation(self, target_template):
        source = ROLLBACK.read_text(encoding="utf-8").replace("\r\n", "\n")
        section = source.split("      - name: Validate target and select revision", 1)
        self.assertEqual(2, len(section), "Rollback validation step must be present")
        match = re.search(r"        run: \|\n((?:          .*\n|\n)+)", section[1])
        self.assertIsNotNone(match)
        script = textwrap.dedent(match.group(1))
        prefix = re.search(r"^  RUNTIME_SQL_SECRET_PREFIX: (\S+)$", source, re.M)
        self.assertIsNotNone(prefix, "Runtime SQL secret prefix must be pinned in the workflow env")
        bash = shutil.which("bash") if os.name != "nt" else str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe")
        self.assertTrue(bash and Path(bash).exists(), "Bash is required for offline rollback tests")
        jq = shutil.which("jq")
        self.assertTrue(jq, "jq is required to execute the real workflow checks")
        stubs = '''
az() {
  case "$*" in
    "containerapp show"*"--query name -o tsv") echo hemodinks-api-prod ;;
    "containerapp show"*"activeRevisionsMode"*) echo Multiple ;;
    "containerapp show"*"ingress.traffic"*) printf '%s' "$TRAFFIC" ;;
    "containerapp show"*"ingress.fqdn"*) echo hemodinks-api-prod.region.azurecontainerapps.io ;;
    "containerapp revision list"*) printf '%s' "$REVISIONS" ;;
    "containerapp revision show"*"--revision app--previous"*) printf '%s' "$TARGET_TEMPLATE" ;;
    *) echo "Unexpected Azure command: $*" >&2; exit 99 ;;
  esac
}
'''
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            env = dict(os.environ, API_NAME="hemodinks-api-prod", API_RESOURCE_GROUP="offline-group",
                       REQUESTED_REVISION="", RUNTIME_SQL_SECRET_PREFIX=prefix.group(1),
                       TRAFFIC=json.dumps(self.TRAFFIC), REVISIONS=json.dumps(self.REVISIONS),
                       TARGET_TEMPLATE=json.dumps(target_template), GITHUB_OUTPUT=output.as_posix(),
                       PATH=os.pathsep.join([str(Path(jq).parent), os.environ.get("PATH", "")]))
            result = subprocess.run([bash, "-e", "-c", stubs + script], env=env,
                                    capture_output=True, text=True, timeout=20)
            return result, output.read_text() if output.exists() else ""

    def test_runtime_secret_target_is_allowed(self):
        result, output = self.run_validation(self.template(
            {"name": "ConnectionStrings__DefaultConnection", "secretRef": "connectionstrings-runtime-prod-v2"}))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("target_revision=app--previous", output)

    def test_legacy_or_unsafe_sql_credentials_block_rollback_before_any_mutation(self):
        runtime = {"name": "ConnectionStrings__DefaultConnection", "secretRef": "connectionstrings-runtime-prod-v2"}
        legacy = {"name": "ConnectionStrings__DefaultConnection", "secretRef": "connectionstrings-defaultconnection"}
        cases = {
            "legacy administrative secret": self.template(legacy),
            "inline connection string": self.template(
                {"name": "ConnectionStrings__DefaultConnection", "value": "Server=x;User ID=admin;Password=unused"}),
            "missing connection": self.template(),
            "second container with legacy secret": self.template(
                runtime, extra_container={"name": "sidecar", "env": [legacy]}),
            "no containers": {"containers": []},
        }
        for name, target_template in cases.items():
            with self.subTest(name):
                result, output = self.run_validation(target_template)
                self.assertEqual(9, result.returncode, result.stdout + result.stderr)
                self.assertEqual("", output, "No target may be emitted when the credential is rejected")
                self.assertIn("Production traffic was not changed", result.stdout)
                self.assertNotIn("Password=unused", result.stdout + result.stderr)


class PublishRevisionReuseTests(unittest.TestCase):
    SHA = "a" * 40
    IMAGE = "ghcr.io/hemodinks/hemodinks-api@sha256:" + "b" * 64

    CPF_SECRETS = ("cpf-protection-certificate", "cpf-protection-certificate-password", "cpf-protection-lookup-hash-key")
    CPF_ENV = [{"name": "CpfProtection__CertificateBase64", "secretRef": "cpf-protection-certificate"},
               {"name": "CpfProtection__CertificatePassword", "secretRef": "cpf-protection-certificate-password"},
               {"name": "CpfProtection__LookupHashKey", "secretRef": "cpf-protection-lookup-hash-key"}]
    # #288: Storage e function key deixam de ser copiadas como value da revisao ativa.
    STORAGE_SECRETS = ("azurestorage-connectionstring", "storagefunctions-functionkey")
    STORAGE_ENV = [{"name": "AzureStorage__ConnectionString", "secretRef": "azurestorage-connectionstring"},
                   {"name": "StorageFunctions__FunctionKey", "secretRef": "storagefunctions-functionkey"}]
    REQUIRED_SECRETS = CPF_SECRETS + STORAGE_SECRETS
    REQUIRED_ENV = CPF_ENV + STORAGE_ENV

    def run_step(self, step, *, same_revision=True, color="blue", mismatch=None,
                 scenario="healthy", already_current="true", cpf_secrets=REQUIRED_SECRETS, existing_candidate_env=None,
                 active_extra_env=()):
        script = deploy_step_script(step)
        bash = (shutil.which("bash") if os.name != "nt" else
                str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"))
        self.assertTrue(shutil.which("jq"), "jq is required to execute the real workflow checks")
        current = "hemodinks-api-prod--sha-" + (self.SHA[:10] if same_revision else "previous")
        candidate = "hemodinks-api-prod--sha-" + self.SHA[:10]
        approved = forwarded_headers.build_environment('["10.20.0.4"]', '[]')
        environment = approved + [*active_extra_env, {"name": "Deployment__CommitSha", "value": self.SHA}]
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
      if [ "$SAME_REVISION" = false ] && [[ "$*" == *"--revision $EXPECTED_CANDIDATE"* ]]; then
        if [ -f "$FIXTURE/candidate.json" ]; then cat "$FIXTURE/candidate.json"; return 0; fi
        return 1
      fi
      cat "$FIXTURE/revision.json" ;;
    "containerapp secret list")
      if [[ "$*" == *"--show-values"* ]]; then echo "Secret values must never be read" >&2; return 97; fi
      printf '%s\n' $CPF_SECRETS new-relic-license-key ;;
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
    *) if [ "$1" = rest ]; then
         printf 'patch\n' >> "$FIXTURE/mutations"
         while [ "$#" -gt 0 ]; do if [ "$1" = --body ]; then printf '%s' "$2" > "$FIXTURE/patch.json"; fi; shift; done
       else echo "Unexpected Azure call: $*" >&2; return 99; fi ;;
  esac
}
'''
        with tempfile.TemporaryDirectory() as directory:
            fixture = Path(directory)
            for name, data in (("revision", revision), ("app", app), ("traffic", traffic)):
                (fixture / f"{name}.json").write_text(json.dumps(data), encoding="utf-8")
            if existing_candidate_env is not None:
                candidate_json = {"properties": {"template": {"containers": [{
                    "name": "api", "image": self.IMAGE, "env": approved + existing_candidate_env}]}}}
                (fixture / "candidate.json").write_text(json.dumps(candidate_json), encoding="utf-8")
            env = dict(os.environ, FIXTURE=fixture.as_posix(), TEST_PYTHON=Path(sys.executable).as_posix(),
                       CPF_SECRETS=" ".join(cpf_secrets),
                       SCENARIO=scenario, SAME_REVISION=str(same_revision).lower(),
                       GITHUB_SHA=self.SHA, API_IMAGE=self.IMAGE, API_NAME=app["name"],
                       API_RESOURCE_GROUP=app["resourceGroup"], NEW_RELIC_LICENSE_KEY="synthetic-key",
                       FORWARDED_HEADERS_ENV=json.dumps(approved), EXPECTED_CANDIDATE=candidate,
                       ACTIVE_LABEL=color, CANDIDATE_LABEL=color if same_revision else "green",
                       PREVIOUS_REVISION=current, CANDIDATE_REVISION=candidate,
                       CANDIDATE_FQDN=app["properties"]["configuration"]["ingress"]["fqdn"],
                       ALREADY_CURRENT=already_current, GITHUB_OUTPUT=(fixture / "output").as_posix())
            script_file = fixture / "step.sh"
            script_file.write_text(stubs + script, encoding="utf-8", newline="\n")
            result = subprocess.run([bash, "-e", script_file.as_posix()],
                                    cwd=ROOT, env=env, capture_output=True, text=True, timeout=20)
            read = lambda name: (fixture / name).read_text() if (fixture / name).exists() else ""
            self.last_patch = json.loads(read("patch.json")) if read("patch.json") else None
            return result, read("output"), read("mutations")

    def test_candidate_rejects_tag_or_foreign_image_before_any_azure_call(self):
        original = self.IMAGE
        for image in ("ghcr.io/hemodinks/hemodinks-api:sha-" + self.SHA, "", "ghcr.io/other/api@sha256:" + "b" * 64):
            with self.subTest(image=image):
                self.IMAGE = image
                try:
                    result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                              same_revision=False)
                finally:
                    self.IMAGE = original
                self.assertEqual(11, result.returncode, result.stderr)
                self.assertEqual("", mutations)
                self.assertEqual("", output)

    def candidate_env(self):
        return self.last_patch["properties"]["template"]["containers"][0]["env"]

    def test_new_candidate_references_cpf_protection_secrets(self):
        stale = [{"name": "CpfProtection__CertificateBase64", "value": "stale-plain-value"},
                 {"name": "CpfProtection__PreviousLookupHashKeys__0", "secretRef": "cpf-protection-previous-lookup-hash-key"}]
        result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                  same_revision=False, active_extra_env=stale)
        self.assertEqual(0, result.returncode, result.stderr)
        env = self.candidate_env()
        cpf = [entry for entry in env if entry["name"] in {e["name"] for e in self.CPF_ENV}]
        self.assertEqual(sorted(self.CPF_ENV, key=lambda e: e["name"]), sorted(cpf, key=lambda e: e["name"]))
        self.assertNotIn("stale-plain-value", json.dumps(self.last_patch))
        # Variaveis de rotacao configuradas na revisao ativa continuam sendo copiadas.
        self.assertIn({"name": "CpfProtection__PreviousLookupHashKeys__0",
                       "secretRef": "cpf-protection-previous-lookup-hash-key"}, env)

    def test_new_candidate_replaces_plaintext_storage_values_with_secret_references(self):
        plaintext = [{"name": "AzureStorage__ConnectionString",
                      "value": "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=plain-storage-key"},
                     {"name": "StorageFunctions__FunctionKey", "value": "plain-function-key"},
                     {"name": "AzureStorage__ContainerName", "value": "profile-photos"}]
        result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                  same_revision=False, active_extra_env=plaintext)
        self.assertEqual(0, result.returncode, result.stderr)
        env = self.candidate_env()
        names = [entry["name"] for entry in env]
        self.assertEqual(len(names), len(set(names)), "variavel duplicada")
        for expected in self.STORAGE_ENV:
            self.assertIn(expected, env)
        patch = json.dumps(self.last_patch)
        self.assertNotIn("plain-storage-key", patch)
        self.assertNotIn("plain-function-key", patch)
        # Variaveis sem segredo continuam sendo copiadas da revisao ativa.
        self.assertIn({"name": "AzureStorage__ContainerName", "value": "profile-photos"}, env)

    def test_new_candidate_limits_new_relic_log_forwarding_and_enables_session_snapshot(self):
        stale = [{"name": "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_LOG_LEVEL_DENYLIST", "value": ""},
                 {"name": "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_CONTEXT_DATA_INCLUDE", "value": "UserId"}]
        result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                  same_revision=False, active_extra_env=stale)
        self.assertEqual(0, result.returncode, result.stderr)
        env = {}
        for entry in self.candidate_env():
            self.assertNotIn(entry["name"], env, "variavel duplicada: " + entry["name"])
            env[entry["name"]] = entry.get("value")
        self.assertEqual("true", env["NEW_RELIC_APPLICATION_LOGGING_FORWARDING_ENABLED"])
        denied = env["NEW_RELIC_APPLICATION_LOGGING_FORWARDING_LOG_LEVEL_DENYLIST"].split(",")
        for level in ("VERBOSE", "TRACE", "DEBUG", "INFORMATION", "INFO"):
            self.assertIn(level, denied)
        self.assertNotIn("WARNING", denied)
        self.assertNotIn("ERROR", denied)
        self.assertEqual("true", env["NEW_RELIC_APPLICATION_LOGGING_FORWARDING_CONTEXT_DATA_ENABLED"])
        included = set(env["NEW_RELIC_APPLICATION_LOGGING_FORWARDING_CONTEXT_DATA_INCLUDE"].split(","))
        self.assertFalse(included & {"UserId", "GlobalUserId", "RequestPath", "UserEmail", "UserName"})
        self.assertEqual("true", env["ActiveSessionsSnapshot__Enabled"])

    def test_missing_required_secret_blocks_deploy_before_any_mutation(self):
        for missing in self.REQUIRED_SECRETS:
            with self.subTest(missing=missing):
                secrets = tuple(s for s in self.REQUIRED_SECRETS if s != missing)
                result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                          same_revision=False, cpf_secrets=secrets)
                self.assertEqual(10, result.returncode, result.stderr)
                self.assertIn(missing, result.stdout)
                self.assertEqual("", mutations)
                self.assertNotIn("already_current", output)

    def test_already_current_reuse_does_not_require_secret_listing(self):
        result, output, mutations = self.run_step("Create candidate revision with zero traffic", cpf_secrets=())
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", mutations)
        self.assertIn("already_current=true", output)

    def test_existing_candidate_is_reused_only_with_required_secret_references(self):
        result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                  same_revision=False, existing_candidate_env=self.REQUIRED_ENV)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("patch", mutations)
        self.assertIn("revision label add", mutations)

        plaintext_storage = [{"name": "AzureStorage__ConnectionString", "value": "AccountKey=plain"},
                             self.STORAGE_ENV[1]]
        for broken in ([], self.CPF_ENV[:2], [{**self.CPF_ENV[0], "secretRef": "other"}, *self.CPF_ENV[1:]],
                       self.CPF_ENV, self.CPF_ENV + plaintext_storage,
                       self.CPF_ENV + [{**self.STORAGE_ENV[0], "secretRef": "other"}, self.STORAGE_ENV[1]]):
            with self.subTest(env=[e["name"] for e in broken]):
                result, output, mutations = self.run_step("Create candidate revision with zero traffic",
                                                          same_revision=False, existing_candidate_env=broken)
                self.assertEqual(8, result.returncode, result.stderr)
                self.assertIn("does not reference the required secrets", result.stdout)
                self.assertNotIn("patch", mutations)
                self.assertNotIn("revision label", mutations)
                self.assertNotIn("ingress traffic", mutations)

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
