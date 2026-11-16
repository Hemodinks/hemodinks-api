#!/usr/bin/env python3
"""Fail CI on High/Critical NuGet vulnerabilities (#286).

Input: the JSON written by
    dotnet list ./HemodinksAPI.slnx package --vulnerable --include-transitive --format json

An advisory may be accepted only through .github/nuget-vulnerability-allowlist.json, with a
reason, a tracking issue and an expiry date. Expired, incomplete or unreadable entries fail
the gate. Any doubt about the report (unreadable, no projects, problems) also fails.
"""
from datetime import date
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ALLOWLIST = ROOT / ".github/nuget-vulnerability-allowlist.json"
BLOCKING = {"high", "critical"}
MAX_EXCEPTION_DAYS = 90
REQUIRED_FIELDS = ("advisory", "package", "reason", "issue", "expires")


def advisory_id(url):
    return url.rstrip("/").rsplit("/", 1)[-1].upper()


def findings(report):
    """Return sorted (advisory, package, version, severity, project) for every vulnerable package."""
    projects = report.get("projects")
    if not projects:
        raise ValueError("report has no projects")
    found = set()
    for project in projects:
        name = Path(project.get("path", "?").replace("\\", "/")).name
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for vulnerability in package.get("vulnerabilities", []):
                        found.add((
                            advisory_id(vulnerability["advisoryurl"]),
                            package["id"],
                            package["resolvedVersion"],
                            vulnerability["severity"],
                            name,
                        ))
    return sorted(found)


def load_allowlist(entries, today):
    """Return ({(advisory, package)}, errors). Every entry must be complete, short-lived and unexpired."""
    allowed, errors = set(), []
    for index, entry in enumerate(entries):
        missing = [field for field in REQUIRED_FIELDS
                   if not isinstance(entry.get(field), str) or not entry[field].strip()]
        if missing:
            errors.append(f"allowlist[{index}] missing {', '.join(missing)}")
            continue
        try:
            expires = date.fromisoformat(entry["expires"])
        except ValueError:
            errors.append(f"allowlist[{index}] has invalid expires {entry['expires']!r} (use YYYY-MM-DD)")
            continue
        if expires < today:
            errors.append(f"allowlist[{index}] {entry['advisory']} expired on {expires}")
            continue
        if (expires - today).days > MAX_EXCEPTION_DAYS:
            errors.append(f"allowlist[{index}] {entry['advisory']} expires more than {MAX_EXCEPTION_DAYS} days ahead")
            continue
        allowed.add((entry["advisory"].strip().upper(), entry["package"].strip().lower()))
    return allowed, errors


def evaluate(report, allowlist_entries, today):
    """Return (blocking findings, accepted findings, allowlist errors)."""
    allowed, errors = load_allowlist(allowlist_entries, today)
    blocking, accepted = [], []
    for finding in findings(report):
        advisory, package, _, severity, _ = finding
        if severity.lower() not in BLOCKING:
            continue
        if (advisory, package.lower()) in allowed:
            accepted.append(finding)
        else:
            blocking.append(finding)
    return blocking, accepted, errors


def main(argv):
    if len(argv) not in (2, 3):
        print("usage: check_nuget_vulnerabilities.py <report.json> [allowlist.json]", file=sys.stderr)
        return 2
    allowlist_path = Path(argv[2]) if len(argv) == 3 else DEFAULT_ALLOWLIST
    try:
        report = json.loads(Path(argv[1]).read_text(encoding="utf-8-sig"))
        if report.get("problems"):
            raise ValueError(f"dotnet reported problems: {report['problems']}")
        allowlist = json.loads(allowlist_path.read_text(encoding="utf-8-sig")) if allowlist_path.exists() else []
        if not isinstance(allowlist, list):
            raise ValueError("allowlist must be a JSON array")
        blocking, accepted, errors = evaluate(report, allowlist, date.today())
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        print(f"::error::NuGet vulnerability report unusable: {error}")
        return 1

    for advisory, package, version, severity, project in accepted:
        print(f"::warning::Accepted by allowlist: {severity} {advisory} in {package} {version} ({project})")
    for error in errors:
        print(f"::error::{error}")
    for advisory, package, version, severity, project in blocking:
        print(f"::error::{severity} {advisory} in {package} {version} ({project})")

    if blocking or errors:
        print("Fix the package (Directory.Packages.props) or, with no fix available, add a dated "
              "exception to .github/nuget-vulnerability-allowlist.json (docs/nuget-vulnerability-gate.md).")
        return 1
    print(f"No blocking NuGet vulnerabilities ({len(accepted)} accepted by allowlist).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
