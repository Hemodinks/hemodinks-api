"""GHCR retention: dry run by default, bounded API-only deletion on explicit dispatch."""

import argparse
import base64
import copy
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
MAX_DELETE_PER_RUN = 20
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


class DeleteFailure(UnsafeState):
    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status


def require(condition, message):
    if not condition:
        raise UnsafeState(message)


def check_mode():
    value = os.environ.get("EXECUTE_DELETE", "false").lower()
    require(value in ("false", "true"), "EXECUTE_DELETE must be false or true.")
    return value == "true"


def validate_delete_context():
    require(check_mode(), "Deletion requires execute_delete=true.")
    require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and os.environ.get("CLEANUP_CONCURRENCY_GROUP") == "production-container-publish"
            and os.environ.get("GITHUB_REPOSITORY", "").lower() == "hemodinks/hemodinks-api",
            "Deletion requires a manual HemoDinks workflow holding the production concurrency group.")


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise UnsafeState("Unexpected HTTP redirect; refusing to forward credentials.")


def get(url, headers, allow_missing=False):
    try:
        with build_opener(NoRedirect()).open(
            Request(url, headers=headers, method="GET"), timeout=45
        ) as response:
            return response.read()
    except HTTPError as exc:
        exc.close()
        if allow_missing and exc.code == 404:
            return None
        raise UnsafeState(f"Read failed: HTTP {exc.code} at {url.split('?')[0]}") from None
    except (URLError, TimeoutError) as exc:
        raise UnsafeState(f"Read unavailable at {url.split('?')[0]} ({type(exc).__name__})") from None


def timestamp(value):
    require(isinstance(value, str), "Missing creation date.")
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(result.tzinfo is not None, "Creation date has no timezone.")
    return result


def github_headers(token):
    return {"Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28"}


def normalize_version(raw):
    version_id, digest = raw["id"], raw["name"]
    tags = raw["metadata"]["container"]["tags"]
    require(type(version_id) is int and version_id > 0, "Invalid version ID.")
    require(isinstance(digest, str) and DIGEST.fullmatch(digest), "Invalid digest.")
    require(isinstance(tags, list) and all(
        isinstance(tag, str) and re.fullmatch(r"[\w][\w.-]{0,127}", tag, re.ASCII)
        for tag in tags) and len(set(tags)) == len(tags), "Missing or invalid tags.")
    timestamp(raw["created_at"])
    return {"id": version_id, "digest": digest, "tags": sorted(tags), "created_at": raw["created_at"]}


def list_versions(package, token):
    headers = github_headers(token)
    versions, ids, digests, tags = [], set(), set(), set()
    page = 1
    while True:
        url = (f"https://api.github.com/orgs/{OWNER}/packages/container/{package}/versions"
               f"?state=active&per_page=100&page={page}")
        batch = json.loads(get(url, headers))
        require(isinstance(batch, list), f"Invalid package inventory: {package}.")
        for raw in batch:
            version = normalize_version(raw)
            version_id, digest, version_tags = version["id"], version["digest"], version["tags"]
            require(version_id not in ids and digest not in digests and not tags.intersection(version_tags),
                    "Duplicate version/digest/tags; inventory may have changed during pagination.")
            versions.append(version)
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
    revision_names = {r["name"] for r in revisions}
    active = {r["name"] for r in revisions if r["active"]}
    require(current[0]["revisionName"] in revision_names, "CURRENT revision is missing from inventory.")
    require(current[0]["revisionName"] in active, "CURRENT revision is inactive.")
    require(previous[0]["revisionName"] in revision_names, "PREVIOUS revision is missing from inventory.")
    require(all(t["revisionName"] in revision_names for t in traffic),
            "Traffic references an unknown revision.")
    # PREVIOUS remains a retention root even when its Azure revision is inactive.
    return {name: roles.get(name, "ACTIVE_BLUE_GREEN") for name in sorted(active | set(roles))}


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
            status = "SKIPPED_UNSAFE_TO_DELETE"
            reason = ("Untagged >14 days; absence of OCI/BuildKit/provenance/referrer dependencies is unproven"
                      if old_untagged else "Untagged <=14 days; untagged deletion disabled")
        else:
            status, reason = "DELETE_CANDIDATE", "sha-* version outside the 10 newest; requires live revalidation"
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


def new_report(now):
    return {"mode": "DELETE" if os.environ.get("EXECUTE_DELETE", "false").lower() == "true" else "DRY_RUN",
            "observed_at": now.isoformat(), "run_id": os.environ.get("GITHUB_RUN_ID"),
            "workflow_sha": os.environ.get("WORKFLOW_SHA", os.environ.get("GITHUB_SHA")),
            "delete_limit": MAX_DELETE_PER_RUN, "deleted": 0, "deleted_versions": [],
            "skipped_versions": [], "delete_attempts": [], "candidates": [], "packages": [],
            "warnings": [], "snapshot_initial": None, "snapshot_pre_delete": None,
            "revalidations": [], "audit_complete": False}


def capture_snapshot(baseline, token):
    return {"azure": [{"name": item["name"], "group": item["group"],
                       "state": azure_snapshot(item["name"], item["group"])} for item in baseline["azure"]],
            "inventories": {package: list_versions(package, token) for package in baseline["inventories"]}}


def audit(now, report=None):
    if check_mode():
        validate_delete_context()
    if report is None:
        report = new_report(now)
    token, actor = os.environ.get("GITHUB_TOKEN"), os.environ.get("GITHUB_ACTOR")
    require(token and actor, "GITHUB_TOKEN and GITHUB_ACTOR are required.")
    snapshots = {(API_APP, API_GROUP): azure_snapshot(API_APP, API_GROUP)}
    api_state = snapshots[(API_APP, API_GROUP)]
    roles = production_revisions(api_state)
    references = image_references(api_state, roles, API)
    workers_name = os.environ.get("WORKERS_APP_NAME", "")
    workers_group = os.environ.get("WORKERS_RESOURCE_GROUP", "")
    warnings = report["warnings"]
    for revision in api_state["revisions"]:
        if roles.get(revision["name"]) == "PREVIOUS" and not revision["active"]:
            warnings.append("PREVIOUS revision exists but is inactive; GHCR image remains protected. "
                            f"Revision: {revision['name']}")
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
    inventories, reports = {}, report["packages"]
    report["snapshot_initial"] = {
        "azure": [{"name": name, "group": group, "state": state} for (name, group), state in snapshots.items()],
        "inventories": inventories}
    for package in PACKAGES:
        versions = list_versions(package, token)
        inventories[package] = versions
        blocked = workers_block if package == WORKERS else None
        registry = Registry(package, token, actor) if not blocked or references[package] else None
        protected = resolve_protected(package, versions, references[package], registry)
        rows = classify(versions, protected, now, blocked)
        if registry:
            warnings.extend(f"{package}: {w}" for w in protect_dependencies(rows, protected, registry))
        if package == WORKERS:
            for row in rows:
                if row["status"] == "PROTECTED":
                    row.update(status="KEEP", reason="Workers deletion disabled; protected: " + row["reason"])
                elif row["status"] == "DELETE_CANDIDATE":
                    row.update(status="SKIPPED_UNSAFE_TO_DELETE", reason="Workers deletion disabled")
        reports.append({"package": package, "azure_images": references[package], "versions": rows})
    # Concurrent deploys/pushes invalidate this report; do not issue stale proposals.
    report["snapshot_revalidated"] = capture_snapshot(report["snapshot_initial"], token)
    require(report["snapshot_revalidated"] == report["snapshot_initial"],
            "Azure or GHCR changed during audit; retry after deployment/publishing.")
    report["production_revisions"] = roles
    report["candidates"] = sorted(
        [row for package in reports if package["package"] == API for row in package["versions"]
         if row["status"] == "DELETE_CANDIDATE"], key=lambda row: (timestamp(row["created_at"]), row["id"]))
    report["audit_complete"] = True
    return report


def version_identity(row):
    return {key: row[key] for key in ("id", "digest", "tags", "created_at")}


def deletion_allowed(package, row):
    return (package == API and row["status"] == "DELETE_CANDIDATE" and bool(row["tags"])
            and all(SHA_TAG.fullmatch(tag) for tag in row["tags"]))


def version_url(package, version_id):
    require(package == API and type(version_id) is int and version_id > 0,
            "Version endpoint restricted to hemodinks-api and a positive version ID.")
    return f"https://api.github.com/orgs/{OWNER}/packages/container/{API}/versions/{version_id}"


def get_version(row, token):
    raw = get(version_url(API, row["id"]), github_headers(token), allow_missing=True)
    return normalize_version(json.loads(raw)) if raw is not None else None


def delete_version(package, row, token):
    validate_delete_context()
    require(deletion_allowed(package, row), "Version is not an API-only tagged SHA deletion candidate.")
    request = Request(version_url(package, row["id"]), headers=github_headers(token), method="DELETE")
    try:
        with build_opener(NoRedirect()).open(request, timeout=45) as response:
            status = response.status
    except HTTPError as exc:
        status = exc.code
        exc.close()
    except (URLError, TimeoutError):
        raise DeleteFailure("DELETE response unavailable; outcome unknown. Stop and inspect the version before retrying.") from None
    if status not in (204, 404):
        raise DeleteFailure(f"DELETE failed with HTTP {status}; no further deletions allowed.", status)
    return status


def checkpoint(report, output):
    report["updated_at"] = datetime.now(timezone.utc).isoformat()
    report["remaining_delete_candidate"] = len(report["candidates"]) - report["deleted"] - sum(
        row.get("absent", False) for row in report["skipped_versions"])
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".tmp")
    temporary.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(output)


def record_skip(report, row, reason, absent=False):
    report["skipped_versions"].append({**version_identity(row), "reason": reason, "absent": absent,
                                       "timestamp": datetime.now(timezone.utc).isoformat()})


def revalidate_for_delete(report, expected, token, row=None, pre_delete=False):
    actual = capture_snapshot(expected, token)
    before_first_request = pre_delete and not report["delete_attempts"]
    if before_first_request:
        report["snapshot_pre_delete"] = actual
    report["revalidations"].append({"version_id": row["id"] if row else None,
                                    "timestamp": datetime.now(timezone.utc).isoformat(),
                                    "snapshot_sha256": hashlib.sha256(json.dumps(actual, sort_keys=True).encode()).hexdigest(),
                                    "matches_expected": actual == expected})
    if actual != expected or (before_first_request and actual != report["snapshot_initial"]):
        report["snapshot_changed"] = actual
        if row:
            fresh = next((v for v in actual["inventories"][API] if v["id"] == row["id"]), None)
            if fresh != version_identity(row):
                record_skip(report, row, "Version changed/disappeared during inventory revalidation")
        raise UnsafeState("Azure or GHCR inventory changed before DELETE; execution aborted.")
    api_state = next(item["state"] for item in actual["azure"]
                     if (item["name"], item["group"]) == (API_APP, API_GROUP))
    require(production_revisions(api_state) == report["production_revisions"],
            "CURRENT/PREVIOUS protection changed before DELETE.")


def execute_deletions(report, output):
    if not check_mode():
        return
    validate_delete_context()
    require(report["audit_complete"] and report["mode"] == "DELETE", "Complete live audit required before DELETE.")
    token, actor = os.environ["GITHUB_TOKEN"], os.environ["GITHUB_ACTOR"]
    expected = copy.deepcopy(report["snapshot_initial"])
    package = next(item for item in report["packages"] if item["package"] == API)
    candidates = sorted(report["candidates"], key=lambda row: (timestamp(row["created_at"]), row["id"]))
    require(all(deletion_allowed(API, row) for row in candidates), "Invalid candidate plan.")
    report["deferred_by_limit"] = max(0, len(candidates) - MAX_DELETE_PER_RUN)
    plan = candidates[:MAX_DELETE_PER_RUN]
    revalidate_for_delete(report, expected, token, pre_delete=True)
    if not plan:
        report["snapshot_pre_delete"] = copy.deepcopy(expected)
        checkpoint(report, output)
        return
    registry = Registry(API, token, actor)
    # Deferred candidates also remain stored. Validate their dependency graphs before any mutation.
    registry.closure(v["digest"] for v in expected["inventories"][API])
    for row in plan:
        require(report["deleted"] < MAX_DELETE_PER_RUN, "Per-run deletion limit reached.")
        revalidate_for_delete(report, expected, token, row)
        versions = expected["inventories"][API]
        # Digests are content-addressed; tags must be resolved afresh on every iteration.
        registry.cache = {key: value for key, value in registry.cache.items() if DIGEST.fullmatch(key)}
        protected = resolve_protected(API, versions, package["azure_images"], registry)
        fresh_rows = classify(versions, protected, timestamp(report["observed_at"]))
        fresh = next((v for v in fresh_rows if v["id"] == row["id"]), None)
        others = registry.closure(v["digest"] for v in versions if v["id"] != row["id"])
        if not fresh or not deletion_allowed(API, fresh) or row["digest"] in others:
            record_skip(report, row, "No longer eligible or referenced by a version retained in this run")
            checkpoint(report, output)
            continue
        # All inventories must still match, including changes caused by our confirmed prior operations.
        revalidate_for_delete(report, expected, token, row, pre_delete=True)
        current = get_version(row, token)
        if current is None:
            record_skip(report, row, "GET returned 404; version already absent", absent=True)
        elif current != version_identity(row):
            record_skip(report, row, "Version metadata/tags changed immediately before DELETE")
            raise UnsafeState("Version changed before DELETE; skipped and remaining execution aborted.")
        else:
            attempt = {**version_identity(row), "outcome": "PENDING_OR_UNKNOWN",
                       "timestamp": datetime.now(timezone.utc).isoformat()}
            report["delete_attempts"].append(attempt)
            checkpoint(report, output)
            try:
                status = delete_version(API, fresh, token)
            except DeleteFailure as exc:
                if exc.status is not None:
                    attempt.update(http_status=exc.status, outcome="FAILED")
                raise
            attempt.update(http_status=status, outcome="DELETED" if status == 204 else "ALREADY_ABSENT")
            if status == 204:
                report["deleted_versions"].append({**version_identity(row), "reason": row["reason"],
                                                    "timestamp": datetime.now(timezone.utc).isoformat()})
                report["deleted"] += 1
            else:
                record_skip(report, row, "DELETE returned 404; version already absent", absent=True)
        expected["inventories"][API] = [v for v in versions if v["id"] != row["id"]]
        checkpoint(report, output)
        # A 404 is not an authorization bypass: verify readable, consistent inventories before continuing.
        revalidate_for_delete(report, expected, token)
    report["execution_complete"] = True


def cell(value):
    return html.escape(str(value)).replace("|", "&#124;").replace("\n", " ")


def write_summary(report):
    remaining = len(report.get("candidates", [])) - report.get("deleted", 0) - sum(
        row.get("absent", False) for row in report.get("skipped_versions", []))
    lines = ["## GHCR Cleanup", "", f"Mode: **{report.get('mode', 'DRY_RUN')}**", "",
             f"Deleted: **{report.get('deleted', 0)}**", f"DELETE LIMIT: **{MAX_DELETE_PER_RUN}**",
             f"REMAINING DELETE_CANDIDATE: **{remaining}**",
             f"Deferred by limit: **{report.get('deferred_by_limit', 0)}**", "",
             "Workers: **deletion disabled**", "Untagged: **deletion disabled**", ""]
    if "error" in report:
        lines += ["**STOPPED — no further deletions. Counts reflect confirmed results only.**", cell(report["error"]), ""]
    unknown = [item for item in report.get("delete_attempts", []) if item["outcome"] == "PENDING_OR_UNKNOWN"]
    if unknown:
        lines += [f"**{len(unknown)} DELETE request(s) have an unknown outcome; inspect the artifact and GHCR.**", ""]
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
    for key, title in (("deleted_versions", "DELETED"), ("skipped_versions", "SKIPPED during deletion")):
        lines += ["", f"### {title}", "", "| Version ID | Digest | Tags | Created at | Reason |",
                  "| --- | --- | --- | --- | --- |"]
        for row in report.get(key, []):
            lines.append("| " + " | ".join(cell(row[k]) for k in
                         ("id", "digest", "tags", "created_at", "reason")) + " |")
            print(json.dumps({"action": title, **row}, sort_keys=True))
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
    parser.add_argument("--output", default="artifacts/ghcr-cleanup-report.json")
    args = parser.parse_args()
    report = new_report(datetime.now(timezone.utc))
    if args.failure_summary:
        if Path(args.output).exists():
            report = json.loads(Path(args.output).read_text(encoding="utf-8"))
        if not report.get("summary_written"):
            report.setdefault("error", "A workflow step failed or was interrupted; inspect confirmed and unknown outcomes.")
            write_summary(report)
            checkpoint(report, args.output)
        return 0
    try:
        execute_delete = check_mode()
        if execute_delete:
            validate_delete_context()
        if args.check_mode:
            print(f"Mode: {report['mode']}. API only; limit {MAX_DELETE_PER_RUN}. Workers/untagged deletion disabled.")
            return 0
        checkpoint(report, args.output)
        audit(timestamp(report["observed_at"]), report)
        checkpoint(report, args.output)
        execute_deletions(report, args.output)
        result = 0
    except (UnsafeState, KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError) as exc:
        message = str(exc) if isinstance(exc, UnsafeState) else f"Incomplete audit ({type(exc).__name__})"
        report["error"] = message
        print("::error::" + message)
        result = 1
    checkpoint(report, args.output)
    write_summary(report)
    report["summary_written"] = True
    checkpoint(report, args.output)
    return result


if __name__ == "__main__":
    sys.exit(main())
