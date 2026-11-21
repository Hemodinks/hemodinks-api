#!/usr/bin/env python3
"""Summarize test coverage per project and enforce floors on high-risk areas (#290).

Input: one or more Cobertura files written by the test run with
    --coverlet --coverlet-output-format cobertura

There is no global gate. Only the areas in FLOORS have a minimum line coverage. The gate is
fail-closed: no readable report, or an area that matches no source line (a move or rename would
otherwise switch its floor off silently), fails. The Markdown summary goes to
GITHUB_STEP_SUMMARY when it is set, and always to stdout.
"""
import argparse
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

# (name, path prefixes relative to the repository root, minimum line coverage %).
# Baseline on 2026-11-21: sessions 93.0%, users 83.7%, tenant middlewares 88.1% (no SQL Server
# tests, so CI is at least this high). Floors sit ~3 points below to absorb small refactors.
# Raise a floor when coverage improves; lowering one needs a reason in the PR.
FLOORS = (
    ("Autenticação: sessões", ("HemodinksAPI.Application/Features/Sessions/",), 90.0),
    ("Autenticação: usuários", ("HemodinksAPI.Application/Features/Users/",), 80.0),
    ("Tenant: middlewares", (
        "HemodinksAPI.Api/Middleware/ClinicaResolutionMiddleware.cs",
        "HemodinksAPI.Api/Middleware/ClinicaResolutionService.cs",
        "HemodinksAPI.Api/Middleware/ClinicaModuleAccessMiddleware.cs",
    ), 85.0),
)
GAPS_PER_AREA = 10
CONDITION = re.compile(r"\((\d+)/(\d+)\)")


def _relative(filename, sources):
    path = filename.replace("\\", "/")
    for source in sources:
        prefix = source.replace("\\", "/").rstrip("/") + "/"
        if path.lower().startswith(prefix.lower()):
            return path[len(prefix):]
    return path.removeprefix("./")


def parse(xml_text):
    """Return {(project, file): {line: (hits, branches_covered, branches_total)}} from one report."""
    root = ET.fromstring(xml_text)
    if root.tag != "coverage":
        raise ValueError("not a Cobertura report")
    sources = [s.text or "" for s in root.iter("source")]
    result = {}
    for package in root.iter("package"):
        project = package.get("name", "?")
        for cls in package.iter("class"):
            key = (project, _relative(cls.get("filename", ""), sources))
            lines = result.setdefault(key, {})
            for line in cls.iter("line"):
                number = int(line.get("number"))
                hits = int(line.get("hits", "0"))
                covered = total = 0
                match = CONDITION.search(line.get("condition-coverage", ""))
                if line.get("branch", "").lower() == "true" and match:
                    covered, total = int(match.group(1)), int(match.group(2))
                lines[number] = _merge(lines.get(number), (hits, covered, total))
    return result


def _merge(old, new):
    if old is None:
        return new
    return (max(old[0], new[0]), max(old[1], new[1]), max(old[2], new[2]))


def combine(reports):
    combined = {}
    for report in reports:
        for key, lines in report.items():
            target = combined.setdefault(key, {})
            for number, value in lines.items():
                target[number] = _merge(target.get(number), value)
    return combined


def _totals(line_maps):
    lines_valid = lines_covered = branches_valid = branches_covered = 0
    for lines in line_maps:
        for hits, covered, total in lines.values():
            lines_valid += 1
            lines_covered += hits > 0
            branches_valid += total
            branches_covered += covered
    return lines_covered, lines_valid, branches_covered, branches_valid


def percent(covered, valid):
    return 100.0 * covered / valid if valid else 0.0


def evaluate(coverage, floors=FLOORS):
    """Return (projects, areas, errors). projects/areas hold totals; errors list broken floors."""
    by_project = {}
    for (project, _), lines in coverage.items():
        by_project.setdefault(project, []).append(lines)
    projects = [(name, _totals(maps)) for name, maps in sorted(by_project.items())]
    projects.append(("Total", _totals(coverage.values())))

    areas, errors = [], []
    for name, prefixes, floor in floors:
        files = {path: lines for (_, path), lines in coverage.items()
                 if path.startswith(prefixes)}
        totals = _totals(files.values())
        rate = percent(totals[0], totals[1])
        gaps = sorted(((sum(1 for hits, _, _ in lines.values() if hits == 0), path)
                       for path, lines in files.items()), reverse=True)
        gaps = [(missing, path) for missing, path in gaps if missing][:GAPS_PER_AREA]
        areas.append((name, floor, totals, gaps))
        if totals[1] == 0:
            errors.append(f"{name}: nenhuma linha encontrada em {', '.join(prefixes)}")
        elif rate + 1e-9 < floor:
            errors.append(f"{name}: {rate:.1f}% de linhas, abaixo do piso de {floor:.1f}%")
    return projects, areas, errors


def _cell(covered, valid):
    return f"{percent(covered, valid):.1f}% ({covered}/{valid})" if valid else "—"


def markdown(projects, areas, errors):
    out = ["## Cobertura de testes", "",
           "| Projeto | Linhas | Branches |", "| --- | ---: | ---: |"]
    for name, (lc, lv, bc, bv) in projects:
        label = f"**{name}**" if name == "Total" else name
        out.append(f"| {label} | {_cell(lc, lv)} | {_cell(bc, bv)} |")
    out += ["", "### Áreas de alto risco (piso de linhas)", "",
            "| Área | Linhas | Branches | Piso | Situação |", "| --- | ---: | ---: | ---: | --- |"]
    for name, floor, (lc, lv, bc, bv), _ in areas:
        ok = lv and percent(lc, lv) + 1e-9 >= floor
        out.append(f"| {name} | {_cell(lc, lv)} | {_cell(bc, bv)} | {floor:.1f}% | "
                   f"{'ok' if ok else '**abaixo**'} |")
    for name, _, _, gaps in areas:
        if gaps:
            out += ["", f"<details><summary>{name}: arquivos com mais linhas descobertas</summary>", "",
                    "| Arquivo | Linhas descobertas |", "| --- | ---: |"]
            out += [f"| `{path}` | {missing} |" for missing, path in gaps]
            out += ["", "</details>"]
    if errors:
        out += ["", "### Falhas", ""] + [f"- {error}" for error in errors]
    return "\n".join(out) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("reports", nargs="+", type=Path)
    args = parser.parse_args(argv)
    try:
        reports = [parse(path.read_text(encoding="utf-8")) for path in args.reports]
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Relatório de cobertura ilegível: {error}", file=sys.stderr)
        return 1
    coverage = combine(reports)
    if not coverage:
        print("Relatório de cobertura sem linhas.", file=sys.stderr)
        return 1
    projects, areas, errors = evaluate(coverage, FLOORS)
    summary = markdown(projects, areas, errors)
    print(summary)
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as handle:
            handle.write(summary)
    for error in errors:
        print(f"::error::Cobertura: {error}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
