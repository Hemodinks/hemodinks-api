"""Read-only GHCR retention audit. There is deliberately no deletion implementation."""

import argparse
import base64
from datetime import datetime, timedelta, timezone
import hashlib
import html
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode
from urllib.request import HTTPRedirectHandler, Request, build_opener


OWNER = "hemodinks"
API = "hemodinks-api"
WORKERS = "hemodinks-api-workers"
PACKAGES = (API, WORKERS)
API_APP = "hemodinks-api-prod"
API_GROUP = "rg-hemodinks-prod"
DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
SHA_TAG = re.compile(r"sha-[A-Za-z0-9_.-]+\Z")
INDEX_TYPES = {
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
}
MANIFEST_TYPES = {
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.v2+json",
}
STATUSES = ("PROTECTED", "KEEP", "DELETE_CANDIDATE", "SKIPPED_UNSAFE_TO_DELETE")


class UnsafeState(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise UnsafeState(message)


def check_mode():
    require(os.environ.get("EXECUTE_DELETE", "false").lower() == "false",
            "execute_delete is disabled in this version. Run again with false. Deleted: 0.")


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise UnsafeState("Unexpected HTTP redirect; refusing to forward credentials.")


def get(url, headers):
    # Only GET exists in the transport; no caller can supply another HTTP method.
    try:
        with build_opener(NoRedirect()).open(
            Request(url, headers=headers, method="GET"), timeout=45
        ) as response:
            return response.read()
    except HTTPError as exc:
        raise UnsafeState(f"Read failed: HTTP {exc.code} at {url.split('?')[0]}") from None
    except (URLError, TimeoutError) as exc:
        raise UnsafeState(f"Read unavailable at {url.split('?')[0]} ({type(exc).__name__})") from None


def timestamp(value):
    require(isinstance(value, str), "Missing creation date.")
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(result.tzinfo is not None, "Creation date has no timezone.")
    return result


def list_versions(package, token):
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json",
               "X-GitHub-Api-Version": "2022-11-28"}
    versions, ids, digests, tags = [], set(), set(), set()
    page = 1
    while True:
        url = (f"https://api.github.com/orgs/{OWNER}/packages/container/{package}/versions"
               f"?state=active&per_page=100&page={page}")
        batch = json.loads(get(url, headers))
        require(isinstance(batch, list), f"Invalid package inventory: {package}.")
        for raw in batch:
            version_id = raw["id"]
            digest = raw["name"]
            version_tags = raw["metadata"]["container"]["tags"]
            require(type(version_id) is int and version_id > 0 and version_id not in ids,
                    "Invalid/duplicate version ID; pagination may have changed.")
            require(isinstance(digest, str) and DIGEST.fullmatch(digest) and digest not in digests,
                    "Invalid/duplicate digest.")
            require(isinstance(version_tags, list) and all(
                isinstance(tag, str) and re.fullmatch(r"[\w][\w.-]{0,127}", tag, re.ASCII)
                for tag in version_tags), "Missing or invalid tags; cannot infer untagged status.")
            require(len(set(version_tags)) == len(version_tags) and not tags.intersection(version_tags),
                    "Duplicate tags; inventory may have changed during pagination.")
            timestamp(raw["created_at"])
            versions.append({"id": version_id, "digest": digest, "tags": sorted(version_tags),
                             "created_at": raw["created_at"]})
            ids.add(version_id)
            digests.add(digest)
            tags.update(version_tags)
        if len(batch) < 100:
            break
        page += 1
    require(versions, f"Empty inventory for {package}; verify package access.")
    return sorted(versions, key=lambda v: (timestamp(v["created_at"]), v["id"]), reverse=True)


class Registry:
    def __init__(self, package, token, actor):
        self.package = package
        basic = base64.b64encode(f"{actor}:{token}".encode()).decode()
        query = urlencode({"service": "ghcr.io", "scope": f"repository:{OWNER}/{package}:pull"})
        auth = json.loads(get(f"https://ghcr.io/token?{query}", {"Authorization": f"Basic {basic}"}))
        bearer = auth.get("token") or auth.get("access_token")
        require(isinstance(bearer, str) and bearer, "Registry pull token unavailable.")
        self.headers = {"Authorization": f"Bearer {bearer}",
                        "Accept": ", ".join(sorted(INDEX_TYPES | MANIFEST_TYPES))}
        self.cache = {}

    def manifest(self, reference):
        if reference not in self.cache:
            raw = get(f"https://ghcr.io/v2/{OWNER}/{self.package}/manifests/{quote(reference, safe=':')}",
                      self.headers)
            digest = "sha256:" + hashlib.sha256(raw).hexdigest()
            require(not DIGEST.fullmatch(reference) or reference == digest, "Manifest digest mismatch.")
            result = (digest, json.loads(raw))
            self.cache[reference] = result
            self.cache[digest] = result
        return self.cache[reference]

    def closure(self, roots):
        pending, visited = list(roots), set()
        while pending:
            digest = pending.pop()
            if digest in visited:
                continue
            visited.add(digest)
            _, manifest = self.manifest(digest)
            require(manifest.get("schemaVersion") == 2, "Unsupported manifest schema.")
            media_type = manifest.get("mediaType")
            if media_type in INDEX_TYPES:
                descriptors = manifest["manifests"]
                require(isinstance(descriptors, list) and descriptors, "Empty/invalid image index.")
            elif media_type in MANIFEST_TYPES:
                require(isinstance(manifest["layers"], list) and isinstance(manifest["config"], dict),
                        "Invalid image manifest.")
                descriptors = []
            else:
                raise UnsafeState("Unknown manifest type; dependency relationships are unsafe.")
            descriptors = list(descriptors)
            if "subject" in manifest:
                descriptors.append(manifest["subject"])
            for descriptor in descriptors:
                child = descriptor["digest"]
                require(isinstance(child, str) and DIGEST.fullmatch(child), "Invalid manifest dependency.")
                pending.append(child)
                # BuildKit indexes can link an attestation to a platform manifest here.
                related = descriptor.get("annotations", {}).get("vnd.docker.reference.digest")
                if related:
                    require(isinstance(related, str) and DIGEST.fullmatch(related),
                            "Invalid BuildKit attestation reference.")
                    pending.append(related)
        return visited


def az_json(arguments):
    result = subprocess.run(["az", *arguments, "--output", "json", "--only-show-errors"],
                            capture_output=True, text=True, timeout=120, check=False)
    require(result.returncode == 0, "Azure read failed; check OIDC, RBAC and resource availability.")
    return json.loads(result.stdout)


def azure_snapshot(name, group):
    target = ["--name", name, "--resource-group", group]
    # Project images only: do not collect environment variables or secret references.
    app = az_json(["containerapp", "show", *target, "--query",
                   "{name:name,mode:properties.configuration.activeRevisionsMode,"
                   "traffic:properties.configuration.ingress.traffic,"
                   "images:properties.template.containers[].image}"])
    revisions = az_json(["containerapp", "revision", "list", *target, "--all", "--query",
                         "[].{name:name,active:properties.active,images:properties.template.containers[].image}"])
    require(app["name"] == name and isinstance(revisions, list) and revisions,
            "Azure target/revision inventory unavailable.")
    require(all(isinstance(r["name"], str) and r["name"] and type(r["active"]) is bool
                for r in revisions), "Incomplete revision metadata.")
    require(len({r["name"] for r in revisions}) == len(revisions), "Duplicate Azure revisions.")
    return {"app": app, "revisions": sorted(revisions, key=lambda r: r["name"])}


def production_revisions(snapshot):
    app, revisions = snapshot["app"], snapshot["revisions"]
    require(str(app["mode"]).lower() == "multiple", "Production must use Multiple revisions.")
    traffic = app["traffic"]
    require(isinstance(traffic, list) and traffic, "Production traffic is unavailable.")
    require(all(type(t.get("weight", 0)) is int and 0 <= t.get("weight", 0) <= 100
                and not t.get("latestRevision", False) and t.get("revisionName") for t in traffic),
            "Traffic has unresolved revision references or invalid weights.")
    labels = [t for t in traffic if t.get("label") in ("blue", "green")]
    require(len(labels) == 2 and {t["label"] for t in labels} == {"blue", "green"},
            "CURRENT/PREVIOUS labels are missing or ambiguous.")
    current = [t for t in labels if t.get("weight", 0) == 100]
    previous = [t for t in labels if t.get("weight", 0) == 0]
    require(sum(t.get("weight", 0) for t in traffic) == 100 and len(current) == len(previous) == 1,
            "CURRENT/PREVIOUS must have unique 100/0 traffic.")
    roles = {current[0]["revisionName"]: "CURRENT", previous[0]["revisionName"]: "PREVIOUS"}
    require(len(roles) == 2, "CURRENT and PREVIOUS must differ.")
    active = {r["name"] for r in revisions if r["active"]}
    require(set(roles) <= active, "CURRENT or PREVIOUS is missing/inactive.")
    require(all(t["revisionName"] in {r["name"] for r in revisions} for t in traffic),
            "Traffic references an unknown revision.")
    return {name: roles.get(name, "ACTIVE_BLUE_GREEN") for name in active}


def image_references(snapshot, roles, expected_package):
    references = {package: {} for package in PACKAGES}
    selected = [(r["images"], f"{roles[r['name']]} revision {r['name']}")
                for r in snapshot["revisions"] if r["name"] in roles]
    selected.append((snapshot["app"]["images"], "Container App template"))
    for images, reason in selected:
        require(isinstance(images, list) and images and all(isinstance(i, str) for i in images),
                "Revision/template image list unavailable.")
        found = False
        for image in images:
            for package in PACKAGES:
                base = f"ghcr.io/{OWNER}/{package}"
                if image == base or image.startswith(base + ":") or image.startswith(base + "@"):
                    require(image.startswith(base + ":sha-") or image.startswith(base + "@sha256:"),
                            "Production image must use sha-* or a digest; mutable aliases are unsafe.")
                    references[package].setdefault(image, []).append(reason)
                    found |= package == expected_package
        require(found, f"Expected {expected_package} image missing from {reason}.")
    return references


def resolve_protected(package, versions, references, registry):
    by_digest = {v["digest"]: v for v in versions}
    by_tag = {tag: v["digest"] for v in versions for tag in v["tags"]}
    protected = {}
    base = f"ghcr.io/{OWNER}/{package}"
    for image, reasons in references.items():
        reference = image[len(base) + 1:]
        if image.startswith(base + "@"):
            require(DIGEST.fullmatch(reference), "Invalid Azure digest reference.")
            digest = reference
        else:
            require(SHA_TAG.fullmatch(reference) and reference in by_tag,
                    "Azure SHA tag is missing from GHCR inventory.")
            digest, _ = registry.manifest(reference)
            require(digest == by_tag[reference], "Azure image tag changed or disagrees with package metadata.")
        require(digest in by_digest, "Azure digest is missing from package inventory.")
        protected.setdefault(digest, []).extend(reasons)
    return protected


def classify(versions, protected, now, blocked_reason=None):
    versions = sorted(versions, key=lambda v: (timestamp(v["created_at"]), v["id"]), reverse=True)
    # Count versions, not tags, with a deterministic tie-breaker for equal dates.
    sha_versions = [v for v in versions if any(SHA_TAG.fullmatch(t) for t in v["tags"])]
    newest = {v["id"] for v in sha_versions[:10]}
    rows = []
    for version in versions:
        old_untagged = not version["tags"] and timestamp(version["created_at"]) < now - timedelta(days=14)
        if version["digest"] in protected:
            status, reason = "PROTECTED", "; ".join(sorted(protected[version["digest"]]))
        elif blocked_reason:
            status, reason = "SKIPPED_UNSAFE_TO_DELETE", blocked_reason
        elif version["id"] in newest:
            status, reason = "KEEP", "One of the 10 newest sha-* versions by created_at"
        elif any(not SHA_TAG.fullmatch(tag) for tag in version["tags"]):
            status, reason = "KEEP", "Has a tag outside sha-*; deleting a version removes all its tags"
        elif not version["tags"]:
            status = "SKIPPED_UNSAFE_TO_DELETE" if old_untagged else "KEEP"
            reason = ("Untagged >14 days; absence of OCI/BuildKit/provenance/referrer dependencies is unproven"
                      if old_untagged else "Untagged <=14 days")
        else:
            status, reason = "DELETE_CANDIDATE", "sha-* version outside the 10 newest; dry-run proposal only"
        rows.append({**version, "status": status, "reason": reason, "old_untagged": old_untagged})
    return rows


def protect_dependencies(rows, protected, registry):
    try:
        protected_graph = registry.closure(protected)
        # Untagged and non-SHA versions also remain stored: preserve their dependencies.
        retained_graph = registry.closure(r["digest"] for r in rows if r["status"] != "DELETE_CANDIDATE")
    except (UnsafeState, KeyError, TypeError, ValueError) as exc:
        for row in rows:
            if row["status"] == "DELETE_CANDIDATE":
                row.update(status="SKIPPED_UNSAFE_TO_DELETE", reason="Manifest relationships could not be verified")
        return [f"Manifest inspection incomplete ({type(exc).__name__}); all potential deletions skipped."]
    for row in rows:
        if row["digest"] in protected_graph and row["digest"] not in protected:
            row.update(status="PROTECTED", reason="OCI/BuildKit dependency of an Azure-protected image")
        elif row["digest"] in retained_graph and row["status"] == "DELETE_CANDIDATE":
            row.update(status="KEEP", reason="Manifest dependency of a retained version")
    return []


def audit(now):
    check_mode()
    token, actor = os.environ.get("GITHUB_TOKEN"), os.environ.get("GITHUB_ACTOR")
    require(token and actor, "GITHUB_TOKEN and GITHUB_ACTOR are required.")
    snapshots = {(API_APP, API_GROUP): azure_snapshot(API_APP, API_GROUP)}
    api_state = snapshots[(API_APP, API_GROUP)]
    roles = production_revisions(api_state)
    references = image_references(api_state, roles, API)
    workers_name = os.environ.get("WORKERS_APP_NAME", "")
    workers_group = os.environ.get("WORKERS_RESOURCE_GROUP", "")
    warnings = []
    workers_block = None
    if workers_name and workers_group:
        require((workers_name, workers_group) != (API_APP, API_GROUP), "Workers target equals API target.")
        state = azure_snapshot(workers_name, workers_group)
        snapshots[(workers_name, workers_group)] = state
        # Workers use direct update, not CURRENT/PREVIOUS. Preserve every extant revision.
        workers_roles = {r["name"]: "WORKERS_EXISTING" for r in state["revisions"]}
        extra = image_references(state, workers_roles, WORKERS)
        for package in PACKAGES:
            for ref, reasons in extra[package].items():
                references[package].setdefault(ref, []).extend(reasons)
    else:
        workers_block = "Workers Container App inventory not configured; GHCR consumers cannot be proven absent"
        warnings.append(workers_block)
    if os.environ.get("WORKERS_FUNCTION_APP"):
        warnings.append("Function App workflow deploys a code package, not a GHCR image; no Blue/Green policy inferred.")
    inventories, reports = {}, []
    for package in PACKAGES:
        versions = list_versions(package, token)
        inventories[package] = versions
        blocked = workers_block if package == WORKERS else None
        registry = Registry(package, token, actor) if not blocked or references[package] else None
        protected = resolve_protected(package, versions, references[package], registry)
        rows = classify(versions, protected, now, blocked)
        if registry:
            warnings.extend(f"{package}: {w}" for w in protect_dependencies(rows, protected, registry))
        reports.append({"package": package, "azure_images": references[package], "versions": rows})
    # Concurrent deploys/pushes invalidate this report; do not issue stale proposals.
    for target, snapshot in snapshots.items():
        require(azure_snapshot(*target) == snapshot, "Azure changed during audit; retry after deployment.")
    for package, versions in inventories.items():
        require(list_versions(package, token) == versions, "GHCR inventory changed during audit; retry after publishing.")
    return {"mode": "DRY_RUN", "deleted": 0, "observed_at": now.isoformat(),
            "production_revisions": roles, "warnings": warnings, "packages": reports}


def cell(value):
    return html.escape(str(value)).replace("|", "&#124;").replace("\n", " ")


def write_summary(report):
    lines = ["## GHCR Cleanup Dry Run", "", "Deleted: **0**", "", "Deletion is not implemented.", ""]
    if "error" in report:
        lines += ["**BLOCKED — incomplete audit; no deletion candidates approved.**", cell(report["error"]), ""]
    for warning in report.get("warnings", []):
        lines += [f"- Warning: {cell(warning)}"]
        print("::warning::" + warning)
    for package in report.get("packages", []):
        lines += ["", f"### Package: {package['package']}", "", "Protected by Azure:", ""]
        lines += [f"- {cell(ref)}: {cell('; '.join(reasons))}" for ref, reasons in package["azure_images"].items()]
        old_count = sum(r["old_untagged"] for r in package["versions"])
        lines += ["", f"Old untagged (>14 days): **{old_count}**; all preserved."]
        for status in STATUSES:
            rows = [r for r in package["versions"] if r["status"] == status]
            lines += ["", f"#### {status} ({len(rows)})", "",
                      "| Version ID | Digest | Tags | Created at | Reason |",
                      "| --- | --- | --- | --- | --- |"]
            # Stay under the Actions summary size limit; artifact/logs include every row.
            for row in rows[:100]:
                lines.append("| " + " | ".join(cell(row[k]) for k in
                             ("id", "digest", "tags", "created_at", "reason")) + " |")
            if len(rows) > 100:
                lines += ["", "First 100 shown; every version is included in the JSON artifact and logs."]
        for row in package["versions"]:
            print(json.dumps({"package": package["package"], **row}, sort_keys=True))
    summary = "\n".join(lines) + "\n"
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary)
    else:
        print(summary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-mode", action="store_true")
    parser.add_argument("--failure-summary", action="store_true")
    parser.add_argument("--output", default="artifacts/ghcr-cleanup-dry-run.json")
    args = parser.parse_args()
    if args.failure_summary:
        write_summary({"error": "A workflow step failed or was interrupted (including Azure login). Deleted: 0."})
        return 0
    try:
        check_mode()
        if args.check_mode:
            print("DRY RUN enforced. No deletion code exists. Deleted: 0.")
            return 0
        report = audit(datetime.now(timezone.utc))
        result = 0
    except (UnsafeState, KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError) as exc:
        message = str(exc) if isinstance(exc, UnsafeState) else f"Incomplete audit ({type(exc).__name__})"
        report = {"mode": "DRY_RUN", "deleted": 0, "error": message, "packages": []}
        print("::error::" + message)
        result = 1
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    write_summary(report)
    return result


if __name__ == "__main__":
    sys.exit(main())
