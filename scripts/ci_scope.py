#!/usr/bin/env python3
"""Decide whether CI may skip its test jobs. Any doubt runs the full suite.

Tests are skipped only for a branch in SKIPPABLE_TARGETS, and only when:
- the change touches documentation alone; or
- a push merges a pull request whose exact tree already passed CI.
"""
import os
import subprocess
import sys

SKIPPABLE_TARGETS = {"developer", "dev-local"}
# Markdown under these folders can be shipped or imported by the application.
ROOTS_WHERE_MARKDOWN_IS_CODE = ("src/", "public/")


def is_documentation(path):
    if path.startswith(ROOTS_WHERE_MARKDOWN_IS_CODE):
        return False
    return path.startswith("docs/") or path.lower().endswith(".md")


def only_documentation(paths):
    return bool(paths) and all(is_documentation(path) for path in paths)


class Repository:
    def __init__(self, name=None):
        self.name = name

    @staticmethod
    def _run(*command):
        return subprocess.run(command, check=True, capture_output=True, text=True).stdout.strip()

    def changed(self, base, head):
        # Without --no-renames, moving code into docs/ would list only the new path.
        output = self._run("git", "diff", "--no-renames", "--name-only", base, head)
        return output.splitlines()

    def parents(self, commit):
        return self._run("git", "rev-list", "--parents", "-n", "1", commit).split()[1:]

    def tree(self, commit):
        return self._run("git", "rev-parse", f"{commit}^{{tree}}")

    def is_ancestor(self, ancestor, commit):
        return subprocess.run(["git", "merge-base", "--is-ancestor", ancestor, commit]).returncode == 0

    def ci_succeeded(self, sha, event=None):
        query = f"head_sha={sha}&status=success&per_page=1"
        if event:
            query += f"&event={event}"
        total = self._run(
            "gh", "api", f"repos/{self.name}/actions/workflows/ci.yml/runs?{query}", "--jq", ".total_count")
        return int(total) > 0


def decide(event, target, before, repo):
    """Return (run_tests, reason)."""
    if target not in SKIPPABLE_TARGETS:
        return True, f"target {target or 'unknown'} always runs the full suite"

    if event == "pull_request":
        if only_documentation(repo.changed("HEAD^1", "HEAD")):
            return False, "pull request changes documentation only"
        return True, "pull request changes code"

    if event != "push" or not before or set(before) == {"0"}:
        return True, "no previous commit to compare"

    if only_documentation(repo.changed(before, "HEAD")) and repo.ci_succeeded(before):
        return False, "push changes documentation only, on top of a commit that passed CI"

    parents = repo.parents("HEAD")
    if len(parents) == 2:
        base, pull_request_head = parents
        # The base is an ancestor of the PR head, so every merge CI tested had this same tree.
        if (base == before
                and repo.is_ancestor(base, pull_request_head)
                and repo.tree(pull_request_head) == repo.tree("HEAD")
                and repo.ci_succeeded(pull_request_head, "pull_request")):
            return False, "merge has the exact tree that passed pull request CI"

    return True, "push was not proven to be already tested"


def main():
    event = os.environ.get("GITHUB_EVENT_NAME", "")
    target = os.environ.get("GITHUB_BASE_REF") or os.environ.get("GITHUB_REF_NAME", "")
    try:
        run_tests, reason = decide(
            event, target, os.environ.get("EVENT_BEFORE", ""), Repository(os.environ.get("GITHUB_REPOSITORY")))
    except Exception as error:  # A failed check must never skip tests.
        run_tests, reason = True, f"scope check failed ({type(error).__name__})"
    print(f"run_tests={str(run_tests).lower()}: {reason}")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"run_tests={str(run_tests).lower()}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
