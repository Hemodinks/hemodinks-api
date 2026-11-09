"""Garante que toda GitHub Action externa esteja pinada por SHA e coberta pelo Dependabot (#222)."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"
DEPENDABOT = ROOT / ".github/dependabot.yml"
USES = re.compile(r"^\s*(?:-\s*)?uses:\s*(\S+)(.*)$", re.MULTILINE)
PINNED = re.compile(r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$")
VERSION_COMMENT = re.compile(r"^\s*#\s*v\d+(\.\d+)*\s*$")


def unpinned(text):
    """Retorna as referências externas sem SHA completo ou sem o comentário de versão."""
    problems = []
    for ref, rest in USES.findall(text):
        ref = ref.strip("'\"")
        if ref.startswith("./"):
            continue
        if not PINNED.match(ref) or not VERSION_COMMENT.match(rest):
            problems.append(ref)
    return problems


class ActionsPinnedTests(unittest.TestCase):
    def test_all_workflow_actions_are_pinned_by_sha(self):
        files = sorted(WORKFLOWS.glob("*.y*ml"))
        self.assertTrue(files)
        problems = {f.name: unpinned(f.read_text(encoding="utf-8")) for f in files}
        self.assertEqual({}, {name: refs for name, refs in problems.items() if refs})

    def test_same_action_uses_a_single_sha(self):
        shas = {}
        for path in WORKFLOWS.glob("*.y*ml"):
            for ref, _ in USES.findall(path.read_text(encoding="utf-8")):
                if not ref.startswith("./"):
                    action, sha = ref.split("@", 1)
                    shas.setdefault(action.lower(), set()).add(sha)
        self.assertEqual({}, {a: s for a, s in shas.items() if len(s) > 1})

    def test_rejects_tags_short_shas_branches_and_missing_version_comment(self):
        for line in ("uses: actions/checkout@v4",
                     "uses: actions/checkout@3d3c42e",
                     "uses: actions/checkout@main",
                     "- uses: azure/login@a641126d1b8aa4d1fa005f4f92df94a3a4c4c906",
                     "uses: docker://alpine:3.20"):
            with self.subTest(line=line):
                self.assertEqual(1, len(unpinned(line)))

    def test_accepts_full_sha_with_version_and_local_workflows(self):
        text = ("      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1\n"
                "    uses: ./.github/workflows/login-e2e.yml\n")
        self.assertEqual([], unpinned(text))

    def test_dependabot_updates_github_actions_into_developer(self):
        config = DEPENDABOT.read_text(encoding="utf-8")
        self.assertIn("package-ecosystem: github-actions", config)
        self.assertIn("target-branch: developer", config)


if __name__ == "__main__":
    unittest.main()
