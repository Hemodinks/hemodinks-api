import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location(
    "ci_scope", Path(__file__).resolve().parents[1] / "ci_scope.py")
ci_scope = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ci_scope)

BEFORE = "a" * 40
PR_HEAD = "b" * 40


class FakeRepository:
    def __init__(self, changed=(), parents=(BEFORE,), trees=None, ancestor=True, succeeded=()):
        self._changed = list(changed)
        self._parents = list(parents)
        self._trees = trees or {}
        self._ancestor = ancestor
        self._succeeded = set(succeeded)

    def changed(self, base, head):
        return self._changed

    def parents(self, commit):
        return self._parents

    def tree(self, commit):
        return self._trees[commit]

    def is_ancestor(self, ancestor, commit):
        return self._ancestor

    def ci_succeeded(self, sha, event=None):
        return (sha, event) in self._succeeded


def merge(**overrides):
    values = dict(
        changed=["src/a.cs"],
        parents=[BEFORE, PR_HEAD],
        trees={"HEAD": "same", PR_HEAD: "same"},
        succeeded={(PR_HEAD, "pull_request")},
    )
    values.update(overrides)
    return FakeRepository(**values)


class DocumentationPathTests(unittest.TestCase):
    def test_documentation_paths(self):
        for path in ("docs/a.txt", "docs/x/y.png", "README.md", "AGENTS.MD", ".codex/ORCHESTRATION.md",
                     "HemodinksAPI.Infrastructure/Data/Migrations/README.md"):
            self.assertTrue(ci_scope.is_documentation(path), path)

    def test_code_and_shipped_markdown_are_not_documentation(self):
        for path in ("src/App.tsx", "src/content/help.md", "public/tutorials/a.md", "scripts/ci_scope.py",
                     ".github/workflows/ci.yml", "package.json", "documents/a.txt", "mdfile", "a.md.cs"):
            self.assertFalse(ci_scope.is_documentation(path), path)

    def test_empty_or_mixed_changes_are_not_documentation_only(self):
        self.assertFalse(ci_scope.only_documentation([]))
        self.assertFalse(ci_scope.only_documentation(["docs/a.md", "src/App.tsx"]))
        self.assertTrue(ci_scope.only_documentation(["docs/a.md", "README.md"]))


class DecisionTests(unittest.TestCase):
    def run_tests(self, event, target, before, repo):
        return ci_scope.decide(event, target, before, repo)[0]

    def test_main_and_unknown_targets_always_run(self):
        docs = FakeRepository(changed=["docs/a.md"], succeeded={(BEFORE, None)})
        for target in ("main", "", "release", "feature/x"):
            self.assertTrue(self.run_tests("pull_request", target, "", docs), target)
            self.assertTrue(self.run_tests("push", target, BEFORE, docs), target)
        self.assertTrue(self.run_tests("push", "main", BEFORE, merge()))

    def test_manual_runs_always_run(self):
        docs = FakeRepository(changed=["docs/a.md"], succeeded={(BEFORE, None)})
        self.assertTrue(self.run_tests("workflow_dispatch", "developer", BEFORE, docs))

    def test_pull_request_skips_only_documentation(self):
        self.assertFalse(self.run_tests("pull_request", "developer", "", FakeRepository(changed=["docs/a.md"])))
        self.assertTrue(self.run_tests("pull_request", "developer", "", FakeRepository(changed=["src/a.cs"])))
        self.assertTrue(self.run_tests("pull_request", "developer", "", FakeRepository(changed=[])))

    def test_push_needs_a_comparable_previous_commit(self):
        docs = FakeRepository(changed=["docs/a.md"], succeeded={(BEFORE, None)})
        self.assertTrue(self.run_tests("push", "developer", "", docs))
        self.assertTrue(self.run_tests("push", "developer", "0" * 40, docs))

    def test_documentation_push_requires_green_previous_commit(self):
        green = FakeRepository(changed=["docs/a.md"], succeeded={(BEFORE, None)})
        red = FakeRepository(changed=["docs/a.md"])
        self.assertFalse(self.run_tests("push", "developer", BEFORE, green))
        self.assertTrue(self.run_tests("push", "developer", BEFORE, red))

    def test_merge_with_tested_identical_tree_skips(self):
        self.assertFalse(self.run_tests("push", "developer", BEFORE, merge()))

    def test_merge_runs_when_any_proof_is_missing(self):
        cases = {
            "direct push": merge(parents=[BEFORE]),
            "several commits pushed": merge(parents=["c" * 40, PR_HEAD]),
            "base moved after branching": merge(ancestor=False),
            "tree differs": merge(trees={"HEAD": "merged", PR_HEAD: "branch"}),
            "pull request CI did not pass": merge(succeeded=set()),
            "only a push run passed": merge(succeeded={(PR_HEAD, "push")}),
        }
        for name, repo in cases.items():
            self.assertTrue(self.run_tests("push", "developer", BEFORE, repo), name)


class FailureHandlingTests(unittest.TestCase):
    def test_failed_check_runs_tests(self):
        class Broken:
            def __init__(self, name=None):
                pass

            def changed(self, base, head):
                raise RuntimeError("git failed")

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            environment = {"GITHUB_EVENT_NAME": "pull_request", "GITHUB_BASE_REF": "developer",
                           "GITHUB_OUTPUT": str(output)}
            original_repository = ci_scope.Repository
            original_environ = dict(ci_scope.os.environ)
            ci_scope.Repository = Broken
            ci_scope.os.environ.update(environment)
            try:
                self.assertEqual(0, ci_scope.main())
            finally:
                ci_scope.Repository = original_repository
                ci_scope.os.environ.clear()
                ci_scope.os.environ.update(original_environ)
            self.assertEqual("run_tests=true\n", output.read_text(encoding="utf-8"))


class RealGitTests(unittest.TestCase):
    def test_moving_code_into_docs_is_seen_as_a_code_change(self):
        with tempfile.TemporaryDirectory() as directory:
            def git(*args):
                return subprocess.run(
                    ["git", "-c", "user.name=t", "-c", "user.email=t@example.invalid", "-c", "commit.gpgsign=false",
                     *args], cwd=directory, check=True, capture_output=True, text=True).stdout.strip()

            git("init", "-q")
            source = Path(directory) / "src"
            source.mkdir()
            (source / "a.cs").write_text("class A {}\n", encoding="utf-8")
            git("add", ".")
            git("commit", "-q", "-m", "one")
            first = git("rev-parse", "HEAD")
            (Path(directory) / "docs").mkdir()
            git("mv", "src/a.cs", "docs/a.md")
            git("commit", "-q", "-m", "two")
            current = os.getcwd()
            os.chdir(directory)
            try:
                changed = ci_scope.Repository().changed(first, "HEAD")
            finally:
                os.chdir(current)

            self.assertIn("src/a.cs", changed)
            self.assertFalse(ci_scope.only_documentation(changed))


if __name__ == "__main__":
    unittest.main()
