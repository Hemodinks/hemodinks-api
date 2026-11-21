"""Regras do resumo de cobertura e dos pisos das áreas de alto risco (#290)."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("coverage_gate", ROOT / "scripts/check_coverage.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)

FLOORS = (
    ("Sessões", ("App/Features/Sessions/",), 50.0),
    ("Tenant", ("Api/Middleware/Clinica.cs",), 50.0),
)


def report(*classes, source="/repo/"):
    """classes: (package, filename, [(line, hits, condition or None)])."""
    packages = {}
    for package, filename, lines in classes:
        rows = "".join(
            f'<line number="{n}" hits="{h}" branch="True" condition-coverage="{c}" />' if c
            else f'<line number="{n}" hits="{h}" branch="False" />'
            for n, h, c in lines)
        packages.setdefault(package, []).append(
            f'<class name="C" filename="{filename}"><lines>{rows}</lines></class>')
    body = "".join(f'<package name="{name}"><classes>{"".join(items)}</classes></package>'
                   for name, items in packages.items())
    return (f'<?xml version="1.0"?><coverage><sources><source>{source}</source></sources>'
            f'<packages>{body}</packages></coverage>')


def healthy():
    return report(
        ("App", "App/Features/Sessions/Login.cs", [(1, 1, None), (2, 0, None)]),
        ("Api", "Api/Middleware/Clinica.cs", [(1, 3, "50% (1/2)"), (2, 1, None)]),
        ("Api", "Api/Other.cs", [(1, 0, None), (2, 0, None), (3, 0, None)]),
    )


class CoverageGateTests(unittest.TestCase):
    def evaluate(self, xml, floors=FLOORS):
        return gate.evaluate(gate.combine([gate.parse(xml)]), floors)

    def test_reports_each_project_and_total(self):
        projects, _, errors = self.evaluate(healthy())
        self.assertEqual([], errors)
        self.assertEqual([("Api", (2, 5, 1, 2)), ("App", (1, 2, 0, 0)), ("Total", (3, 7, 1, 2))],
                         projects)

    def test_area_below_floor_fails(self):
        xml = report(
            ("App", "App/Features/Sessions/Login.cs", [(1, 1, None), (2, 0, None), (3, 0, None)]),
            ("Api", "Api/Middleware/Clinica.cs", [(1, 1, None)]))
        _, _, errors = self.evaluate(xml)
        self.assertEqual(1, len(errors))
        self.assertIn("Sessões", errors[0])

    def test_area_without_lines_fails_closed(self):
        xml = report(("App", "App/Features/Sessions/Login.cs", [(1, 1, None)]))
        _, _, errors = self.evaluate(xml)
        self.assertEqual(["Tenant: nenhuma linha encontrada em Api/Middleware/Clinica.cs"], errors)

    def test_prefix_does_not_match_sibling_folder(self):
        xml = report(
            ("App", "App/Features/SessionsLegacy/X.cs", [(1, 0, None)]),
            ("App", "App/Features/Sessions/Login.cs", [(1, 1, None)]),
            ("Api", "Api/Middleware/Clinica.cs", [(1, 1, None)]))
        _, areas, errors = self.evaluate(xml)
        self.assertEqual([], errors)
        self.assertEqual((1, 1, 0, 0), areas[0][2])

    def test_absolute_filenames_are_made_relative_to_source(self):
        xml = report(
            ("App", "/repo/App/Features/Sessions/Login.cs", [(1, 1, None)]),
            ("Api", "C:\\repo\\Api\\Middleware\\Clinica.cs", [(1, 1, None)]),
            source="C:\\repo\\")
        xml = xml.replace("<source>C:\\repo\\</source>",
                          "<source>C:\\repo\\</source><source>/repo/</source>")
        _, _, errors = self.evaluate(xml)
        self.assertEqual([], errors)

    def test_reports_merge_by_best_hit(self):
        first = report(("App", "App/Features/Sessions/Login.cs", [(1, 0, "0% (0/2)"), (2, 1, None)]))
        second = report(("App", "App/Features/Sessions/Login.cs", [(1, 2, "50% (1/2)"), (2, 0, None)]))
        coverage = gate.combine([gate.parse(first), gate.parse(second)])
        self.assertEqual({1: (2, 1, 2), 2: (1, 0, 0)},
                         coverage[("App", "App/Features/Sessions/Login.cs")])

    def test_summary_lists_gaps_and_failures(self):
        projects, areas, errors = self.evaluate(healthy(), floors=(("Tudo", ("Api/",), 90.0),))
        text = gate.markdown(projects, areas, errors)
        self.assertIn("| **Total** | 42.9% (3/7) | 50.0% (1/2) |", text)
        self.assertIn("| `Api/Other.cs` | 3 |", text)
        self.assertIn("**abaixo**", text)
        self.assertIn("### Falhas", text)

    def test_main_fails_on_unreadable_or_empty_report(self):
        with tempfile.TemporaryDirectory() as folder:
            bad = Path(folder, "bad.xml")
            bad.write_text("<not-xml", encoding="utf-8")
            empty = Path(folder, "empty.xml")
            empty.write_text(report(), encoding="utf-8")
            with mock.patch.dict(os.environ, {}, clear=True):
                self.assertEqual(1, gate.main([str(bad)]))
                self.assertEqual(1, gate.main([str(empty)]))
                self.assertEqual(1, gate.main([str(Path(folder, "missing.xml"))]))

    def test_main_writes_step_summary(self):
        with tempfile.TemporaryDirectory() as folder:
            xml = Path(folder, "coverage.cobertura.xml")
            xml.write_text(healthy(), encoding="utf-8")
            summary = Path(folder, "summary.md")
            with mock.patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)}), \
                    mock.patch.object(gate, "FLOORS", FLOORS):
                self.assertEqual(0, gate.main([str(xml)]))
            self.assertIn("## Cobertura de testes", summary.read_text(encoding="utf-8"))

    def test_real_floors_cover_the_issue_areas(self):
        prefixes = [prefix for _, items, _ in gate.FLOORS for prefix in items]
        self.assertIn("HemodinksAPI.Application/Features/Sessions/", prefixes)
        self.assertIn("HemodinksAPI.Application/Features/Users/", prefixes)
        self.assertIn("HemodinksAPI.Api/Middleware/ClinicaResolutionMiddleware.cs", prefixes)
        for _, items, floor in gate.FLOORS:
            self.assertGreater(floor, 0, "piso zero não protege nada")
            for prefix in items:
                self.assertTrue((ROOT / prefix).exists(), f"{prefix} não existe no repositório")


if __name__ == "__main__":
    unittest.main()
