#!/usr/bin/env python3
"""Decide de onde vem uma release de Producao (#278).

- Deploy manual (workflow_dispatch com deploy_to_azure): reaproveita o run de push bem-sucedido do mesmo
  SHA (imagem por digest e bundle de migrations). Sem esse run, falha: nunca reconstroi no deploy.
- Push na main: pula validacao e E2E so quando um CI de pull request do proprio repositorio, bem-sucedido,
  registrou exatamente a mesma arvore git. Qualquer duvida roda a suite completa.
- Demais eventos: build e testes completos, como antes.
"""
import argparse
import json
import os
import re
import subprocess
import sys

PUBLISH_WORKFLOW = "publish-container.yml"
CI_WORKFLOW_PATH = ".github/workflows/ci.yml"
TREE_ARTIFACT_PREFIX = "ci-tested-tree-"
IMAGE_PATTERN = re.compile(r"^ghcr\.io/hemodinks/hemodinks-api@sha256:[0-9a-f]{64}$")
SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")


class ReleaseError(Exception):
    pass


def migration_artifact(sha):
    return f"production-migrations-{sha}"


def image_artifact(sha):
    return f"release-image-{sha}"


class GitHub:
    def __init__(self, repository):
        self.repository = repository

    def _get(self, path):
        output = subprocess.run(["gh", "api", f"repos/{self.repository}/{path}"],
                                check=True, capture_output=True, text=True).stdout
        return json.loads(output)

    def successful_push_runs(self, sha):
        data = self._get(f"actions/workflows/{PUBLISH_WORKFLOW}/runs"
                         f"?event=push&head_sha={sha}&status=success&per_page=20")
        return data.get("workflow_runs", [])

    def run_artifacts(self, run_id):
        return self._get(f"actions/runs/{run_id}/artifacts?per_page=100").get("artifacts", [])

    def artifacts_named(self, name):
        return self._get(f"actions/artifacts?name={name}&per_page=20").get("artifacts", [])

    def run(self, run_id):
        return self._get(f"actions/runs/{run_id}")


def find_reusable_push_run(sha, github):
    """Return the newest successful push run of this SHA that still has both release artifacts."""
    if not SHA_PATTERN.match(sha or ""):
        raise ReleaseError("Invalid commit SHA.")
    required = {migration_artifact(sha), image_artifact(sha)}
    runs = sorted(github.successful_push_runs(sha), key=lambda run: run.get("run_number", 0), reverse=True)
    for run in runs:
        if run.get("head_sha") != sha or run.get("conclusion") != "success":
            continue
        available = {a.get("name") for a in github.run_artifacts(run["id"]) if not a.get("expired")}
        if required <= available:
            return run["id"]
    raise ReleaseError(
        f"No successful Publish Production push run with release artifacts for {sha}. "
        "Re-run the push run of this commit (or push the commit to main) before deploying.")


def tree_passed_pull_request_ci(tree, repository, github):
    """True only when a successful same-repository pull request CI run recorded this exact tree."""
    if not SHA_PATTERN.match(tree or ""):
        return False
    for artifact in github.artifacts_named(f"{TREE_ARTIFACT_PREFIX}{tree}"):
        if artifact.get("expired") or artifact.get("name") != f"{TREE_ARTIFACT_PREFIX}{tree}":
            continue
        run_id = (artifact.get("workflow_run") or {}).get("id")
        if not run_id:
            continue
        run = github.run(run_id)
        if (run.get("path") == CI_WORKFLOW_PATH
                and run.get("event") == "pull_request"
                and run.get("conclusion") == "success"
                and (run.get("head_repository") or {}).get("full_name") == repository):
            return True
    return False


def decide(event, deploy, sha, tree, repository, github):
    """Return the job outputs as a dict; raise ReleaseError when a deploy cannot reuse a release."""
    if event == "workflow_dispatch" and deploy:
        run_id = find_reusable_push_run(sha, github)
        return {"mode": "reuse", "run_tests": "false", "source_run_id": str(run_id),
                "reason": f"deploy reuses push run {run_id}"}
    if event == "push":
        try:
            proven = tree_passed_pull_request_ci(tree, repository, github)
        except Exception as error:  # A failed check must never skip tests.
            return {"mode": "build", "run_tests": "true", "source_run_id": "",
                    "reason": f"tree check failed ({type(error).__name__})"}
        if proven:
            return {"mode": "build", "run_tests": "false", "source_run_id": "",
                    "reason": "pull request CI already passed with this exact tree"}
        return {"mode": "build", "run_tests": "true", "source_run_id": "",
                "reason": "tree was not proven to be already tested"}
    return {"mode": "build", "run_tests": "true", "source_run_id": "", "reason": f"{event or 'unknown'} event runs the full pipeline"}


def read_image(path):
    """Validate the image manifest of a reused release; only an exact digest reference is accepted."""
    with open(path, encoding="utf-8") as handle:
        image = handle.read().strip()
    if not IMAGE_PATTERN.match(image):
        raise ReleaseError("Release image manifest is not an exact ghcr.io/hemodinks/hemodinks-api digest.")
    return image


def write_outputs(values):
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for key, value in values.items():
            if key != "reason":
                output.write(f"{key}={value}\n")


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--image-file")
    args = parser.parse_args(argv)
    try:
        if args.image_file:
            image = read_image(args.image_file)
            print(f"api_image={image}")
            write_outputs({"api_image": image})
            return 0
        values = decide(
            os.environ.get("GITHUB_EVENT_NAME", ""),
            os.environ.get("DEPLOY_TO_AZURE", "").lower() == "true",
            os.environ.get("GITHUB_SHA", ""),
            os.environ.get("TREE_SHA", ""),
            os.environ.get("GITHUB_REPOSITORY", ""),
            GitHub(os.environ.get("GITHUB_REPOSITORY", "")))
    except ReleaseError as error:
        print(f"::error::{error}")
        return 3
    except Exception as error:  # Reuse must fail closed; build decisions are handled inside decide().
        print(f"::error::Release source check failed ({type(error).__name__}).")
        return 3
    print(f"mode={values['mode']} run_tests={values['run_tests']}: {values['reason']}")
    write_outputs(values)
    return 0


if __name__ == "__main__":
    sys.exit(main())
