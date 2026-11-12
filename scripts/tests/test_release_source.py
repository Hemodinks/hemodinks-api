"""Offline checks for the Production release source decision (#278). No GitHub API access."""
import os
import tempfile
import unittest
from pathlib import Path

from scripts import release_source as rs

REPO = "Hemodinks/hemodinks-api"
SHA = "a" * 40
TREE = "b" * 40
DIGEST_IMAGE = "ghcr.io/hemodinks/hemodinks-api@sha256:" + "c" * 64


class FakeGitHub:
    def __init__(self, push_runs=(), artifacts=None, named=None, runs=None, fail=False):
        self.push_runs = list(push_runs)
        self.artifacts = artifacts or {}
        self.named = named or {}
        self.runs = runs or {}
        self.fail = fail
        self.calls = []

    def _check(self, call):
        self.calls.append(call)
        if self.fail:
            raise RuntimeError("api down")

    def successful_push_runs(self, sha):
        self._check("push_runs")
        return self.push_runs

    def run_artifacts(self, run_id):
        self._check("run_artifacts")
        return self.artifacts.get(run_id, [])

    def artifacts_named(self, name):
        self._check("artifacts_named")
        return self.named.get(name, [])

    def run(self, run_id):
        self._check("run")
        return self.runs[run_id]


def release_artifacts(sha=SHA, expired=False, skip=()):
    names = [rs.migration_artifact(sha), rs.image_artifact(sha)]
    return [{"name": n, "expired": expired} for n in names if n not in skip]


def push_run(run_id, run_number, sha=SHA, conclusion="success"):
    return {"id": run_id, "run_number": run_number, "head_sha": sha, "conclusion": conclusion}


def tree_proof(run_id=7, expired=False, tree=TREE):
    return {f"ci-tested-tree-{tree}": [{"name": f"ci-tested-tree-{tree}", "expired": expired,
                                        "workflow_run": {"id": run_id}}]}


def ci_run(path=".github/workflows/ci.yml", event="pull_request", conclusion="success", repo=REPO):
    return {"path": path, "event": event, "conclusion": conclusion, "head_repository": {"full_name": repo}}


class DeployReuseTests(unittest.TestCase):
    def test_deploy_reuses_newest_push_run_with_both_artifacts(self):
        github = FakeGitHub(push_runs=[push_run(10, 1), push_run(20, 2)],
                            artifacts={10: release_artifacts(), 20: release_artifacts()})
        values = rs.decide("workflow_dispatch", True, SHA, TREE, REPO, github)
        self.assertEqual({"mode": "reuse", "run_tests": "false", "source_run_id": "20"},
                         {k: values[k] for k in ("mode", "run_tests", "source_run_id")})

    def test_deploy_skips_runs_with_expired_or_missing_artifacts(self):
        github = FakeGitHub(push_runs=[push_run(10, 1), push_run(20, 2), push_run(30, 3)],
                            artifacts={10: release_artifacts(), 20: release_artifacts(expired=True),
                                       30: release_artifacts(skip=(rs.image_artifact(SHA),))})
        self.assertEqual("10", rs.decide("workflow_dispatch", True, SHA, TREE, REPO, github)["source_run_id"])

    def test_deploy_without_reusable_push_run_fails_closed(self):
        cases = {
            "no run": FakeGitHub(),
            "other sha": FakeGitHub(push_runs=[push_run(10, 1, sha="d" * 40)],
                                    artifacts={10: release_artifacts()}),
            "failed run": FakeGitHub(push_runs=[push_run(10, 1, conclusion="failure")],
                                     artifacts={10: release_artifacts()}),
            "no bundle": FakeGitHub(push_runs=[push_run(10, 1)],
                                    artifacts={10: release_artifacts(skip=(rs.migration_artifact(SHA),))}),
            "expired": FakeGitHub(push_runs=[push_run(10, 1)], artifacts={10: release_artifacts(expired=True)}),
        }
        for name, github in cases.items():
            with self.subTest(case=name), self.assertRaises(rs.ReleaseError):
                rs.decide("workflow_dispatch", True, SHA, TREE, REPO, github)

    def test_deploy_api_failure_is_not_turned_into_a_build(self):
        with self.assertRaises(RuntimeError):
            rs.decide("workflow_dispatch", True, SHA, TREE, REPO, FakeGitHub(fail=True))

    def test_invalid_sha_is_rejected_before_any_api_call(self):
        github = FakeGitHub()
        with self.assertRaises(rs.ReleaseError):
            rs.decide("workflow_dispatch", True, "main", TREE, REPO, github)
        self.assertEqual([], github.calls)

    def test_manual_run_without_deploy_builds_and_tests(self):
        github = FakeGitHub()
        values = rs.decide("workflow_dispatch", False, SHA, TREE, REPO, github)
        self.assertEqual(("build", "true"), (values["mode"], values["run_tests"]))
        self.assertEqual([], github.calls)


class PushTreeProofTests(unittest.TestCase):
    def test_push_with_tree_proven_by_pull_request_ci_skips_tests(self):
        github = FakeGitHub(named=tree_proof(), runs={7: ci_run()})
        values = rs.decide("push", False, SHA, TREE, REPO, github)
        self.assertEqual(("build", "false"), (values["mode"], values["run_tests"]))

    def test_push_runs_tests_when_any_proof_is_missing_or_untrusted(self):
        cases = {
            "no artifact": FakeGitHub(),
            "expired": FakeGitHub(named=tree_proof(expired=True), runs={7: ci_run()}),
            "failed CI": FakeGitHub(named=tree_proof(), runs={7: ci_run(conclusion="failure")}),
            "other workflow": FakeGitHub(named=tree_proof(), runs={7: ci_run(path=".github/workflows/x.yml")}),
            "push CI": FakeGitHub(named=tree_proof(), runs={7: ci_run(event="push")}),
            "fork": FakeGitHub(named=tree_proof(), runs={7: ci_run(repo="someone/fork")}),
            "api down": FakeGitHub(fail=True),
        }
        for name, github in cases.items():
            with self.subTest(case=name):
                values = rs.decide("push", False, SHA, TREE, REPO, github)
                self.assertEqual(("build", "true"), (values["mode"], values["run_tests"]))

    def test_proof_of_another_tree_does_not_count(self):
        github = FakeGitHub(named=tree_proof(tree="e" * 40), runs={7: ci_run()})
        self.assertEqual("true", rs.decide("push", False, SHA, TREE, REPO, github)["run_tests"])

    def test_invalid_tree_runs_tests(self):
        github = FakeGitHub(named=tree_proof(), runs={7: ci_run()})
        self.assertEqual("true", rs.decide("push", False, SHA, "", REPO, github)["run_tests"])


class ImageManifestTests(unittest.TestCase):
    def read(self, content):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "image.txt"
            path.write_text(content, encoding="utf-8")
            return rs.read_image(path)

    def test_exact_digest_is_accepted(self):
        self.assertEqual(DIGEST_IMAGE, self.read(DIGEST_IMAGE + "\n"))

    def test_tags_other_images_and_malformed_digests_are_rejected(self):
        for content in ("ghcr.io/hemodinks/hemodinks-api:sha-" + SHA,
                        "ghcr.io/other/api@sha256:" + "c" * 64,
                        "ghcr.io/hemodinks/hemodinks-api@sha256:" + "c" * 63,
                        "ghcr.io/hemodinks/hemodinks-api@sha256:" + "C" * 64,
                        DIGEST_IMAGE + "\nextra", ""):
            with self.subTest(content=content), self.assertRaises(rs.ReleaseError):
                self.read(content)


class MainEntryTests(unittest.TestCase):
    def run_main(self, env, argv=()):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            old = dict(os.environ)
            try:
                os.environ.clear()
                os.environ.update(env, GITHUB_OUTPUT=str(output))
                code = rs.main(list(argv))
            finally:
                os.environ.clear()
                os.environ.update(old)
            return code, output.read_text(encoding="utf-8") if output.exists() else ""

    def test_reuse_failure_exits_without_outputs(self):
        original = rs.GitHub
        rs.GitHub = lambda repository: FakeGitHub()
        try:
            code, output = self.run_main({"GITHUB_EVENT_NAME": "workflow_dispatch", "DEPLOY_TO_AZURE": "true",
                                          "GITHUB_SHA": SHA, "GITHUB_REPOSITORY": REPO})
        finally:
            rs.GitHub = original
        self.assertEqual(3, code)
        self.assertEqual("", output)

    def test_push_writes_build_outputs(self):
        original = rs.GitHub
        rs.GitHub = lambda repository: FakeGitHub()
        try:
            code, output = self.run_main({"GITHUB_EVENT_NAME": "push", "GITHUB_SHA": SHA,
                                          "TREE_SHA": TREE, "GITHUB_REPOSITORY": REPO})
        finally:
            rs.GitHub = original
        self.assertEqual(0, code)
        self.assertIn("mode=build\n", output)
        self.assertIn("run_tests=true\n", output)

    def test_invalid_image_manifest_exits_without_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "image.txt"
            path.write_text("ghcr.io/hemodinks/hemodinks-api:latest", encoding="utf-8")
            code, output = self.run_main({}, ["--image-file", str(path)])
        self.assertEqual(3, code)
        self.assertEqual("", output)


if __name__ == "__main__":
    unittest.main()
