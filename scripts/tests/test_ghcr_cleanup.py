"""Offline safety tests; no Azure or GHCR credentials/network required."""

import copy
from datetime import datetime, timedelta, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch


SCRIPT = Path(__file__).resolve().parents[1] / "ghcr_cleanup.py"
spec = importlib.util.spec_from_file_location("ghcr_cleanup", SCRIPT)
cleanup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cleanup)
REGISTRY_CLASS = cleanup.Registry
NOW = datetime(2026, 9, 22, tzinfo=timezone.utc)


def digest(number):
    return f"sha256:{number:064x}"


def version(number, days=30, tags=None):
    return {"id": number, "digest": digest(number),
            "tags": [f"sha-{number:040x}"] if tags is None else tags,
            "created_at": (NOW - timedelta(days=days)).isoformat()}


def api_version(number):
    item = version(number)
    return {"id": item["id"], "name": item["digest"], "created_at": item["created_at"],
            "metadata": {"container": {"tags": item["tags"]}}}


def snapshot():
    image = f"ghcr.io/hemodinks/hemodinks-api@{digest(1)}"
    return {"app": {"name": cleanup.API_APP, "mode": "Multiple", "images": [image],
                    "traffic": [{"label": "blue", "weight": 100, "revisionName": "arbitrary-current"},
                                {"label": "green", "weight": 0, "revisionName": "arbitrary-previous"}]},
            "revisions": [{"name": name, "active": True, "images": [image]}
                          for name in ("arbitrary-current", "arbitrary-previous", "candidate-zero") ]}


def registry_fixture(manifests):
    registry = object.__new__(REGISTRY_CLASS)
    registry.cache = {}
    registry.reset_graph()
    registry.referrers = Mock(return_value=set())
    registry.manifest = Mock(side_effect=lambda ref: (ref, manifests[ref]))
    return registry


def manifest(**extra):
    return {"schemaVersion": 2, "mediaType": "application/vnd.oci.image.manifest.v1+json",
            "config": {"mediaType": "application/vnd.oci.image.config.v1+json"}, "layers": [], **extra}


class RetentionTests(unittest.TestCase):
    def test_ten_newest_plus_old_azure_protection(self):
        versions = [version(i, days=i) for i in range(1, 15)]
        rows = cleanup.classify(list(reversed(versions)), {digest(14): ["PREVIOUS"]}, NOW)
        self.assertEqual(10, sum(r["status"] == "KEEP" for r in rows))
        self.assertEqual({11, 12, 13}, {r["id"] for r in rows if r["status"] == "DELETE_CANDIDATE"})
        self.assertEqual("PROTECTED", rows[-1]["status"])

    def test_untagged_threshold_and_mixed_tags(self):
        rows = cleanup.classify([version(1, 15, []), version(2, 14, []),
                                 version(3, 2, []), version(4, 40, ["sha-abc", "v1.0.0"])], {}, NOW)
        states = {r["id"]: r["status"] for r in rows}
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", states[1])
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", states[2])
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", states[3])
        self.assertEqual("KEEP", states[4])

    def test_mixed_tags_outside_ten_are_kept(self):
        rows = cleanup.classify([version(i, i) for i in range(1, 12)] +
                                [version(99, 99, ["sha-99", "release"])], {}, NOW)
        self.assertEqual("KEEP", next(r["status"] for r in rows if r["id"] == 99))

    def test_multiple_sha_tags_count_as_one_version_and_ties_are_stable(self):
        versions = [version(i) for i in range(1, 13)]
        versions[-1]["tags"].append("sha-other")
        rows = cleanup.classify(versions, {}, NOW)
        self.assertEqual([2, 1], [r["id"] for r in rows if r["status"] == "DELETE_CANDIDATE"])
        self.assertEqual(rows, cleanup.classify(list(reversed(versions)), {}, NOW))

    def test_unconfigured_workers_never_have_candidates(self):
        rows = cleanup.classify([version(i) for i in range(1, 20)], {}, NOW, "Unknown workers deployment")
        self.assertTrue(all(r["status"] == "SKIPPED_UNSAFE_TO_DELETE" for r in rows))


class AzureTests(unittest.TestCase):
    def test_dynamic_labels_and_extra_active_revision(self):
        roles = cleanup.production_revisions(snapshot())
        self.assertEqual("CURRENT", roles["arbitrary-current"])
        self.assertEqual("PREVIOUS", roles["arbitrary-previous"])
        self.assertEqual("ACTIVE_BLUE_GREEN", roles["candidate-zero"])

    def test_green_current_blue_previous_regardless_of_order_or_revision_name(self):
        state = snapshot()
        state["app"]["traffic"][0]["weight"] = 0
        state["app"]["traffic"][1]["weight"] = 100
        state["app"]["traffic"].reverse()
        state["revisions"].reverse()
        roles = cleanup.production_revisions(state)
        self.assertEqual("CURRENT", roles["arbitrary-previous"])
        self.assertEqual("PREVIOUS", roles["arbitrary-current"])
        references = cleanup.image_references(state, roles, cleanup.API)
        protected = cleanup.resolve_protected(cleanup.API, [version(1)], references[cleanup.API], Mock())
        self.assertTrue(any("CURRENT" in reason for reason in protected[digest(1)]))
        self.assertTrue(any("PREVIOUS" in reason for reason in protected[digest(1)]))

    def test_inactive_current_fails_closed(self):
        state = snapshot()
        state["revisions"][0]["active"] = False
        with self.assertRaisesRegex(cleanup.UnsafeState, "CURRENT revision is inactive"):
            cleanup.production_revisions(state)

    def test_missing_previous_fails_closed(self):
        state = snapshot()
        state["revisions"].pop(1)
        with self.assertRaisesRegex(cleanup.UnsafeState, "PREVIOUS revision is missing"):
            cleanup.production_revisions(state)

    def test_ambiguous_blue_green_labels_fail_closed(self):
        state = snapshot()
        state["app"]["traffic"].append(copy.deepcopy(state["app"]["traffic"][1]))
        with self.assertRaisesRegex(cleanup.UnsafeState, "labels are missing or ambiguous"):
            cleanup.production_revisions(state)

    def test_multiple_revisions_with_full_traffic_fail_closed(self):
        for labeled in (True, False):
            state = snapshot()
            if labeled:
                state["app"]["traffic"][1]["weight"] = 100
            else:
                state["app"]["traffic"].append({"revisionName": "candidate-zero", "weight": 100})
            with self.subTest(labeled=labeled), self.assertRaisesRegex(cleanup.UnsafeState, "unique 100/0"):
                cleanup.production_revisions(state)

    def test_missing_labels_split_traffic_and_unresolved_current_fail_closed(self):
        cases = []
        state = snapshot()
        state["app"]["traffic"].pop()
        cases.append(state)
        state = snapshot()
        state["app"]["traffic"][0]["weight"] = 50
        state["app"]["traffic"][1]["weight"] = 50
        cases.append(state)
        state = snapshot()
        state["app"]["traffic"][0]["latestRevision"] = True
        cases.append(state)
        state = snapshot()
        state["revisions"].pop(0)
        cases.append(state)
        for state in cases:
            with self.subTest(state=state), self.assertRaises(cleanup.UnsafeState):
                cleanup.production_revisions(state)

    def test_latest_is_never_a_production_reference(self):
        state = snapshot()
        state["app"]["images"] = ["ghcr.io/hemodinks/hemodinks-api:latest"]
        with self.assertRaises(cleanup.UnsafeState):
            cleanup.image_references(state, cleanup.production_revisions(state), cleanup.API)

    def test_azure_unavailable_blocks(self):
        with patch.object(cleanup.subprocess, "run", return_value=Mock(returncode=1)):
            with self.assertRaises(cleanup.UnsafeState):
                cleanup.azure_snapshot(cleanup.API_APP, cleanup.API_GROUP)

    def test_workers_preserve_inactive_revisions_without_blue_green(self):
        state = snapshot()
        state["app"]["mode"] = "Single"
        state["app"]["traffic"] = None
        state["revisions"][1]["active"] = False
        image = f"ghcr.io/hemodinks/hemodinks-api-workers@{digest(1)}"
        state["app"]["images"] = [image]
        for revision in state["revisions"]:
            revision["images"] = [image]
        refs = cleanup.image_references(state, {r["name"]: "WORKERS_EXISTING" for r in state["revisions"]},
                                        cleanup.WORKERS)
        self.assertTrue(any("arbitrary-previous" in reason for reason in refs[cleanup.WORKERS][image]))


class InventoryTests(unittest.TestCase):
    def test_pagination_over_100_and_exact_multiple(self):
        for total in (101, 200, 205):
            pages = [[api_version(i) for i in range(start, min(start + 100, total + 1))]
                     for start in range(1, total + 2, 100)]
            with self.subTest(total=total), patch.object(cleanup, "get", side_effect=[json.dumps(p) for p in pages]) as get:
                result = cleanup.list_versions(cleanup.API, "fake-token")
                self.assertEqual(total, len(result))
                self.assertEqual(total // 100 + 1, get.call_count)
                self.assertIn("page=2", get.call_args_list[1].args[0])

    def test_missing_tags_and_duplicate_versions_are_not_treated_as_untagged(self):
        missing = api_version(1)
        del missing["metadata"]["container"]["tags"]
        for page in ([missing], [api_version(1), api_version(1)]):
            with patch.object(cleanup, "get", return_value=json.dumps(page)):
                with self.assertRaises((KeyError, cleanup.UnsafeState)):
                    cleanup.list_versions(cleanup.API, "fake-token")

    def test_sha_tag_missing_or_moved_blocks(self):
        registry = Mock()
        registry.manifest.return_value = (digest(99), {})
        refs = {"ghcr.io/hemodinks/hemodinks-api:sha-missing": ["CURRENT"]}
        with self.assertRaises(cleanup.UnsafeState):
            cleanup.resolve_protected(cleanup.API, [version(1)], refs, registry)
        refs = {"ghcr.io/hemodinks/hemodinks-api:" + version(1)["tags"][0]: ["CURRENT"]}
        with self.assertRaises(cleanup.UnsafeState):
            cleanup.resolve_protected(cleanup.API, [version(1)], refs, registry)

    def test_registry_verifies_content_digest(self):
        registry = object.__new__(cleanup.Registry)
        registry.package, registry.cache, registry.headers = cleanup.API, {}, {}
        raw = json.dumps(manifest()).encode()
        actual = "sha256:" + hashlib.sha256(raw).hexdigest()
        with patch.object(cleanup, "get", return_value=raw):
            self.assertEqual(actual, registry.manifest(actual)[0])
            with self.assertRaises(cleanup.UnsafeState):
                registry.manifest(digest(42))


class ManifestTests(unittest.TestCase):
    def test_nested_indexes_provenance_and_tagged_children_are_preserved(self):
        manifests = {
            digest(1): {"schemaVersion": 2, "mediaType": "application/vnd.oci.image.index.v1+json",
                        "manifests": [{"digest": digest(2)}]},
            digest(2): {"schemaVersion": 2, "mediaType": "application/vnd.docker.distribution.manifest.list.v2+json",
                        "manifests": [{"digest": digest(3), "annotations": {"vnd.docker.reference.digest": digest(4)}}]},
            digest(3): manifest(), digest(4): manifest(),
        }
        rows = [{**version(i), "status": "PROTECTED" if i == 1 else "DELETE_CANDIDATE"} for i in range(1, 5)]
        self.assertEqual([], cleanup.protect_dependencies(rows, {digest(1): ["CURRENT"]}, registry_fixture(manifests)))
        self.assertTrue(all(r["status"] == "PROTECTED" for r in rows))

    def test_retained_untagged_subject_preserves_tagged_candidate(self):
        manifests = {digest(1): manifest(subject={"digest": digest(2)}), digest(2): manifest()}
        rows = [{**version(1, tags=[]), "status": "SKIPPED_UNSAFE_TO_DELETE"},
                {**version(2), "status": "DELETE_CANDIDATE"}]
        self.assertEqual([], cleanup.protect_dependencies(rows, {}, registry_fixture(manifests)))
        self.assertEqual("KEEP", rows[1]["status"])

    def test_unknown_or_unavailable_manifest_skips_every_candidate(self):
        for manifests in ({}, {digest(1): {"schemaVersion": 2, "mediaType": "unknown"}}):
            rows = [{**version(1), "status": "KEEP"}, {**version(2), "status": "DELETE_CANDIDATE"}]
            warnings = cleanup.protect_dependencies(rows, {}, registry_fixture(manifests))
            self.assertTrue(warnings)
            self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", rows[1]["status"])


class AuditTests(unittest.TestCase):
    def test_current_and_previous_images_protected_even_when_previous_inactive(self):
        versions = [version(i, i) for i in range(1, 15)]
        for previous_active in (True, False):
            for current_color in ("blue", "green"):
                with self.subTest(previous_active=previous_active, current_color=current_color):
                    state = snapshot()
                    state["app"]["traffic"][0]["label"] = current_color
                    state["app"]["traffic"][1]["label"] = "green" if current_color == "blue" else "blue"
                    state["revisions"][1]["active"] = previous_active
                    # Distinct images outside the newest ten expose lost retention roots.
                    state["revisions"][0]["images"] = [f"ghcr.io/hemodinks/hemodinks-api@{digest(13)}"]
                    state["revisions"][1]["images"] = [f"ghcr.io/hemodinks/hemodinks-api@{digest(14)}"]
                    with tempfile.TemporaryDirectory() as directory:
                        summary = Path(directory) / "summary.md"
                        env = {"GITHUB_TOKEN": "fake", "GITHUB_ACTOR": "test", "EXECUTE_DELETE": "false",
                               "GITHUB_STEP_SUMMARY": str(summary)}
                        registry = registry_fixture({v["digest"]: manifest() for v in versions})
                        with patch.dict(os.environ, env, clear=True), \
                             patch.object(cleanup, "azure_snapshot", return_value=state), \
                             patch.object(cleanup, "list_versions", return_value=versions), \
                             patch.object(cleanup, "Registry", return_value=registry), \
                             patch("builtins.print"):
                            report = cleanup.audit(NOW)
                            cleanup.write_summary(report)
                        rows = {r["id"]: r for r in report["packages"][0]["versions"]}
                        self.assertEqual("PROTECTED", rows[13]["status"])
                        self.assertIn("CURRENT", rows[13]["reason"])
                        self.assertEqual("PROTECTED", rows[14]["status"])
                        self.assertIn("PREVIOUS", rows[14]["reason"])
                        self.assertEqual("PROTECTED", rows[1]["status"])
                        self.assertEqual(0, report["deleted"])
                        self.assertEqual("DRY_RUN", report["mode"])
                        warning = "PREVIOUS revision exists but is inactive; GHCR image remains protected."
                        self.assertEqual(not previous_active, warning in summary.read_text(encoding="utf-8"))

    def test_success_and_concurrent_mutations(self):
        env = {"GITHUB_TOKEN": "fake", "GITHUB_ACTOR": "test", "EXECUTE_DELETE": "false"}
        versions = [version(i, i) for i in range(1, 13)]
        for change in (None, "azure", "ghcr"):
            first, last = snapshot(), snapshot()
            if change == "azure":
                last["revisions"][2]["active"] = False
            inventories = [versions, versions, versions, versions]
            if change == "ghcr":
                inventories[2] = versions[:-1]
            with patch.dict(os.environ, env, clear=True), \
                 patch.object(cleanup, "azure_snapshot", side_effect=[first, last]), \
                 patch.object(cleanup, "list_versions", side_effect=inventories), \
                 patch.object(cleanup, "Registry", return_value=registry_fixture({v["digest"]: manifest() for v in versions})):
                if change:
                    with self.assertRaises(cleanup.UnsafeState):
                        cleanup.audit(NOW)
                else:
                    report = cleanup.audit(NOW)
                    self.assertEqual(0, report["deleted"])
                    self.assertTrue(any(r["status"] == "DELETE_CANDIDATE" for r in report["packages"][0]["versions"]))
                    self.assertTrue(all(r["status"] == "SKIPPED_UNSAFE_TO_DELETE" for r in report["packages"][1]["versions"]))

    def test_invalid_delete_inputs_and_true_without_production_context_fail_before_network(self):
        for value in ("true", "1", "yes", ""):
            with patch.dict(os.environ, {"EXECUTE_DELETE": value}), \
                 patch.object(cleanup, "get") as get, patch.object(cleanup, "az_json") as az:
                with self.assertRaises(cleanup.UnsafeState):
                    cleanup.audit(NOW)
                get.assert_not_called()
                az.assert_not_called()

    def test_http_transport_only_uses_get(self):
        response = Mock()
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        response.read.return_value = b"{}"
        opener = Mock()
        opener.open.return_value = response
        with patch.object(cleanup, "build_opener", return_value=opener):
            cleanup.get("https://api.github.com/example", {})
        self.assertEqual("GET", opener.open.call_args.args[0].get_method())

    def test_failure_outputs_zero_and_no_partial_candidates(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "report.json"
            summary = Path(directory) / "summary.md"
            with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary), "EXECUTE_DELETE": "false"}), \
                 patch.object(cleanup, "audit", side_effect=cleanup.UnsafeState("Azure unavailable")), \
                 patch.object(cleanup.sys, "argv", [str(SCRIPT), "--output", str(report)]):
                self.assertEqual(1, cleanup.main())
            self.assertEqual([], json.loads(report.read_text())["packages"])
            self.assertIn("Deleted: **0**", summary.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
