"""Read-only GHCR/Azure inventory for weekly audit; never invokes DELETE."""
import argparse
import datetime as dt
import json
import os
import subprocess
import sys
from pathlib import Path

from retention import classify

def run(*args):
    p = subprocess.run(args, capture_output=True, text=True, timeout=90, check=False)
    if p.returncode:
        raise RuntimeError(f"{args[0]} read failed with exit code {p.returncode}")
    return p.stdout

def gh_pages(endpoint):
    # --slurp retains page boundaries; pagination failures give nonzero exit code.
    data = json.loads(run("gh", "api", "--paginate", "--slurp", endpoint))
    if not isinstance(data, list) or any(not isinstance(p, list) for p in data):
        raise ValueError("Invalid paginated GHCR response")
    return [entry for page in data for entry in page]

def collect_ghcr(owner, package):
    from urllib.parse import quote
    endpoint = f"/orgs/{quote(owner, safe='')}/packages/container/{quote(package, safe='')}/versions?per_page=100"
    versions = []
    for v in gh_pages(endpoint):
        name = v.get("name")
        tags = v.get("metadata", {}).get("container", {}).get("tags")
        if not isinstance(name, str) or not name.startswith("sha256:"):
            raise ValueError("GHCR digest not proven")
        if not isinstance(tags, list):
            raise ValueError("GHCR tags absent")
        versions.append({"package": package, "id": v["id"], "digest": name,
                         "created_at": v["created_at"], "tags": tags})
    return versions

def collect_azure(subscription, group, app):
    raw = run("az", "containerapp", "revision", "list", "--all",
              "--subscription", subscription, "--resource-group", group,
              "--name", app, "--output", "json")
    revisions = json.loads(raw)
    if not isinstance(revisions, list) or not revisions:
        raise ValueError("Azure revision inventory empty or malformed")
    refs = []
    unresolved = []
    summary = []
    for revision in revisions:
        props = revision.get("properties", {})
        containers = (props.get("template") or {}).get("containers")
        if not isinstance(containers, list) or not containers:
            raise ValueError("Azure revision missing container references")
        rev = revision.get("name")
        for container in containers:
            image = container.get("image")
            if not isinstance(image, str) or not image:
                raise ValueError("Azure image reference missing")
            summary.append({"revision": rev, "image": image, "active": props.get("active")})
            if "@sha256:" in image:
                digest = "sha256:" + image.rsplit("@sha256:", 1)[1]
                if len(digest) == 71:
                    refs.append(digest)
                else:
                    unresolved.append(image)
            else:
                # Mutable tags cannot establish the historic digest of a revision.
                unresolved.append(image)
    return refs, unresolved, summary

def build_report(result, azure_revisions, errors):
    lines = ["# GHCR retention weekly audit", "",
             "- Mode: **dry-run only** (DELETE unavailable)",
             f"- Inventory complete: **{result['inventory_complete']}**",
             f"- Candidate count: **{result['candidate_count']}**",
             f"- Azure revision image references observed: **{len(azure_revisions)}**",
             f"- Errors: **{len(errors)}**", "",
             "| Package | Version ID | Classification | Reasons |",
             "| --- | --- | --- | --- |"]
    for v in result["results"]:
        lines.append(f"| {v['package']} | {v['id']} | {v['status']} | {', '.join(v['reasons'])} |")
    if errors:
        lines += ["", "## Blocking errors"] + [f"- {e}" for e in errors]
    return "\n".join(lines) + "\n"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path("ghcr-audit-output"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    errors, revisions = [], []
    package = os.environ.get("GHCR_AUDIT_PACKAGE", "")
    owner = os.environ.get("GHCR_AUDIT_OWNER", "")
    subscription = os.environ.get("AZURE_SUBSCRIPTION_ID", "")
    group = os.environ.get("GHCR_AUDIT_RESOURCE_GROUP", "")
    app = os.environ.get("GHCR_AUDIT_CONTAINER_APP", "")
    inventory = {
        "policy": {"dry_run": True, "delete_enabled": False, "retention_days": 30,
                   "keep_latest_versions": 5, "package_allowlist": [package] if package else []},
        "ghcr": {"complete": False, "versions": []},
        "azure": {"complete": False, "protected_digests": [], "unresolved_image_references": []}
    }
    if not all((owner, package, subscription, group, app)):
        errors.append("Missing explicit GHCR/Azure scope variables; audit blocked")
    else:
        try:
            inventory["ghcr"]["versions"] = collect_ghcr(owner, package)
            if not inventory["ghcr"]["versions"]:
                raise ValueError("No GHCR versions returned")
            inventory["ghcr"]["complete"] = True
        except (OSError, RuntimeError, ValueError, KeyError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
            errors.append(f"GHCR inventory blocked: {type(exc).__name__}: {exc}")
        try:
            digests, unknown, revisions = collect_azure(subscription, group, app)
            inventory["azure"]["protected_digests"] = digests
            inventory["azure"]["unresolved_image_references"] = unknown
            # A single app is not enough to assert all protected environments
            # have been scanned, even when every digest is immutable.
            errors.append("Environment/recovery scope not yet independently verified")
        except (OSError, RuntimeError, ValueError, KeyError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
            errors.append(f"Azure inventory blocked: {type(exc).__name__}: {exc}")
    try:
        result = classify(inventory)
    except (ValueError, TypeError, KeyError) as exc:
        errors.append(f"Classification blocked: {type(exc).__name__}: {exc}")
        result = {"dry_run": True, "delete_enabled": False, "inventory_complete": False,
                  "candidate_count": 0, "results": []}
    payload = {"generated_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
               "status": "BLOCKED" if errors else "DRY_RUN", "errors": errors,
               "azure_revision_images": revisions, "classification": result}
    (args.output / "ghcr-retention-report.json").write_text(json.dumps(payload, indent=2) + "\n")
    markdown = build_report(result, revisions, errors)
    (args.output / "ghcr-retention-report.md").write_text(markdown)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as file:
            file.write(markdown)
    return 2 if errors or not result["inventory_complete"] else 0

if __name__ == "__main__":
    sys.exit(main())
