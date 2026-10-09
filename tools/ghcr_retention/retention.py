"""Fail-closed GHCR retention classifier. Offline dry-run only; no DELETE capability.

Input is a prevalidated inventory JSON. This module never authenticates to Azure
or GitHub and must not be treated as evidence that live inventory is complete.
"""
import argparse
import datetime as dt
import json
from pathlib import Path

UTC = dt.timezone.utc

def classify(inventory, now=None):
    now = now or dt.datetime.now(UTC)
    if now.tzinfo is None:
        raise ValueError("now must be timezone aware")
    policy = inventory.get("policy", {})
    if policy.get("dry_run") is not True or policy.get("delete_enabled") is not False:
        raise ValueError("Only dry-run with DELETE disabled is supported")
    if policy.get("retention_days") != 30 or policy.get("keep_latest_versions") != 5:
        raise ValueError("Unexpected retention policy")
    allowed = policy.get("package_allowlist")
    if not isinstance(allowed, list) or not allowed or not all(isinstance(p, str) and p for p in allowed):
        raise ValueError("Explicit non-empty package allowlist required")
    ghcr = inventory.get("ghcr", {})
    azure = inventory.get("azure", {})
    complete = ghcr.get("complete") is True and azure.get("complete") is True
    refs = azure.get("protected_digests", [])
    unresolved = azure.get("unresolved_image_references", [])
    if not isinstance(refs, list) or not isinstance(unresolved, list):
        complete = False
        refs, unresolved = [], ["invalid reference inventory"]
    if unresolved:
        complete = False
    refs = set(refs)
    versions = ghcr.get("versions")
    if not isinstance(versions, list):
        versions, complete = [], False
    grouped = {}
    for version in versions:
        package = version.get("package")
        if not isinstance(package, str) or not package:
            complete = False
            continue
        grouped.setdefault(package, []).append(version)
    results = []
    for package, entries in sorted(grouped.items()):
        try:
            ordered = sorted(entries, key=lambda v: (v["created_at"], str(v["id"])), reverse=True)
        except (KeyError, TypeError):
            ordered, complete = entries, False
        recent_ids = {str(v.get("id")) for v in ordered[:5]}
        for v in entries:
            reasons = []
            digest = v.get("digest")
            if package not in allowed:
                reasons.append("PROTECTED_ALLOWLIST")
            if str(v.get("id")) in recent_ids:
                reasons.append("PROTECTED_LATEST")
            if isinstance(digest, str) and digest in refs:
                reasons.append("PROTECTED_AZURE_REVISION")
            if not complete:
                reasons.append("BLOCKED_INVENTORY")
            try:
                created = dt.datetime.fromisoformat(v["created_at"].replace("Z", "+00:00"))
                if created.tzinfo is None or created > now:
                    raise ValueError("Invalid date")
                if (now - created) <= dt.timedelta(days=30):
                    reasons.append("PROTECTED_RECENT")
                if not (isinstance(digest, str) and digest.startswith("sha256:") and len(digest) == 71):
                    reasons.append("BLOCKED_DIGEST_RESOLUTION")
                if not isinstance(v.get("tags"), list):
                    reasons.append("BLOCKED_METADATA")
            except (ValueError, TypeError, KeyError, AttributeError):
                reasons.append("BLOCKED_METADATA")
            status = "DELETE_CANDIDATE" if not reasons else next((x for x in reasons if x.startswith("BLOCKED")), reasons[0])
            results.append({"package": package, "id": v.get("id"), "digest": digest,
                            "status": status, "reasons": reasons})
    return {"dry_run": True, "delete_enabled": False, "inventory_complete": complete,
            "results": results, "candidate_count": sum(r["status"] == "DELETE_CANDIDATE" for r in results)}

def main():
    parser = argparse.ArgumentParser(description="Offline dry-run GHCR retention classifier")
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    result = classify(json.loads(args.inventory.read_text(encoding="utf-8")))
    args.report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"dry-run: {result['candidate_count']} candidates, inventory_complete={result['inventory_complete']}")
    return 0 if result["inventory_complete"] else 2

if __name__ == "__main__":
    raise SystemExit(main())
