"""Deletion integration tests use an in-memory GHCR and never contact remote services."""

import copy
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError

from test_ghcr_cleanup import cleanup, digest, manifest, NOW, registry_fixture, snapshot, version


DELETE_ENV = {"EXECUTE_DELETE": "true", "GITHUB_TOKEN": "fake", "GITHUB_ACTOR": "test",
              "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "Hemodinks/hemodinks-api",
              "CLEANUP_CONCURRENCY_GROUP": "production-container-publish", "GITHUB_RUN_ID": "123",
              "WORKFLOW_SHA": "a" * 40, "GITHUB_REF": "refs/heads/main"}

INVALID_DELETE_REFS = (None, "", "main", "refs/tags/main", "refs/pull/1/merge",
                       "refs/heads/Main", "refs/heads/main/", "refs/heads/developer",
                       "refs/heads/feature/test")


class LiveFixture:
    def __init__(self, count=15):
        self.state = snapshot()
        self.state["revisions"][1].update(active=False, images=[f"ghcr.io/hemodinks/hemodinks-api@{digest(2)}"])
        self.inventories = {cleanup.API: [version(i, i) for i in range(1, count + 1)],
                            cleanup.WORKERS: [version(i, i) for i in range(1, 16)]}
        self.manifests = {digest(i): manifest() for i in range(1, count + 1)}
        self.calls = []

    def azure(self, *args):
        return copy.deepcopy(self.state)

    def versions(self, package, token):
        return copy.deepcopy(self.inventories[package])

    def get_version(self, row, token):
        return copy.deepcopy(next((v for v in self.inventories[cleanup.API] if v["id"] == row["id"]), None))

    def delete(self, package, row, token):
        self.calls.append(row["id"])
        self.remove(row["id"])
        return 204

    def remove(self, version_id):
        self.inventories[cleanup.API] = [v for v in self.inventories[cleanup.API] if v["id"] != version_id]


class ExecutionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.output = Path(self.directory.name) / "report.json"
        self.summary = Path(self.directory.name) / "summary.md"
        self.live = LiveFixture(35)
        self.env = patch.dict(os.environ, {**DELETE_ENV, "GITHUB_STEP_SUMMARY": str(self.summary)}, clear=True)
        self.env.start()
        self.addCleanup(self.env.stop)
        patches = [patch.object(cleanup, "azure_snapshot", side_effect=self.live.azure),
                   patch.object(cleanup, "list_versions", side_effect=self.live.versions),
                   patch.object(cleanup, "Registry", side_effect=lambda *args: registry_fixture(self.live.manifests)),
                   patch.object(cleanup, "get_version", side_effect=self.live.get_version),
                   patch.object(cleanup, "delete_version", side_effect=self.live.delete)]
        self.azure, self.versions, self.registry, self.get_version, self.delete = [p.start() for p in patches]
        for item in patches:
            self.addCleanup(item.stop)

    def audit(self):
        return cleanup.audit(NOW)

    def execute(self, report):
        cleanup.execute_deletions(report, self.output)

    def test_default_dry_run_never_deletes(self):
        with patch.dict(os.environ, {"EXECUTE_DELETE": "false"}):
            report = self.audit()
            self.execute(report)
        self.delete.assert_not_called()
        self.get_version.assert_not_called()
        self.assertEqual("DRY_RUN", report["mode"])
        self.assertEqual(0, report["deleted"])

    def test_valid_delete_oldest_first_limit_20_and_complete_audit(self):
        report = self.audit()
        self.execute(report)
        self.assertEqual(list(range(35, 15, -1)), self.live.calls)
        self.assertEqual(20, report["deleted"])
        self.assertEqual(5, report["deferred_by_limit"])
        self.assertEqual(5, report["remaining_delete_candidate"])
        self.assertEqual(report["snapshot_initial"], report["snapshot_pre_delete"])
        stored = json.loads(self.output.read_text())
        self.assertEqual("123", stored["run_id"])
        self.assertEqual("a" * 40, stored["workflow_sha"])
        self.assertEqual(20, len(stored["deleted_versions"]))
        self.assertTrue(all(item["outcome"] == "DELETED" for item in stored["delete_attempts"]))
        self.assertTrue(all(row["status"] in ("KEEP", "SKIPPED_UNSAFE_TO_DELETE")
                            for row in report["packages"][1]["versions"]))

    def test_dry_run_allowed_on_every_branch(self):
        for ref in ("refs/heads/main", "refs/heads/developer", "refs/heads/feature/test"):
            with self.subTest(ref=ref), patch.dict(os.environ, {"EXECUTE_DELETE": "false", "GITHUB_REF": ref}), \
                 patch.object(cleanup.sys, "argv", ["ghcr_cleanup.py", "--output", str(self.output)]), \
                 patch("builtins.print"):
                self.assertEqual(0, cleanup.main())
                report = json.loads(self.output.read_text())
                self.assertTrue(report["audit_complete"])
                self.assertEqual("DRY_RUN", report["mode"])
                self.assertEqual(0, report["deleted"])
                self.assertTrue(report["candidates"])
                self.delete.assert_not_called()
                self.get_version.assert_not_called()

    def test_invalid_ref_aborts_cli_before_audit_and_reports_zero(self):
        for ref in INVALID_DELETE_REFS:
            for flags in ([], ["--check-mode"]):
                with self.subTest(ref=ref, flags=flags), patch.dict(os.environ), \
                     patch.object(cleanup.sys, "argv", ["ghcr_cleanup.py", *flags, "--output", str(self.output)]), \
                     patch("builtins.print") as output:
                    os.environ.pop("GITHUB_REF", None)
                    if ref is not None:
                        os.environ["GITHUB_REF"] = ref
                    self.assertEqual(1, cleanup.main())
                    output.assert_any_call("::error::DELETE is allowed only from refs/heads/main.")
                    output.assert_any_call("Deleted: 0")
                    report = json.loads(self.output.read_text())
                    self.assertEqual(0, report["deleted"])
                    self.assertEqual([], report["delete_attempts"])
                    self.assertFalse(report["audit_complete"])
                    self.assertIn("Deleted: **0**", self.summary.read_text(encoding="utf-8"))
                    self.azure.assert_not_called()
                    self.versions.assert_not_called()
                    self.delete.assert_not_called()

    def test_branch_change_after_audit_blocks_execution(self):
        report = self.audit()
        with patch.dict(os.environ, {"GITHUB_REF": "refs/heads/developer"}):
            with self.assertRaisesRegex(cleanup.UnsafeState, "DELETE is allowed only from refs/heads/main"):
                self.execute(report)
        self.delete.assert_not_called()
        self.assertEqual(0, report["deleted"])

    def test_protected_keep_young_untagged_and_non_sha_never_enter_plan(self):
        for version_id, tags in ((35, []), (34, ["sha-x", "release"]), (33, ["latest"])):
            next(v for v in self.live.inventories[cleanup.API] if v["id"] == version_id)["tags"] = tags
        next(v for v in self.live.inventories[cleanup.API] if v["id"] == 35)["created_at"] = version(35, 14)["created_at"]
        self.live.state["revisions"][1]["images"] = [f"ghcr.io/hemodinks/hemodinks-api@{digest(32)}"]
        report = self.audit()
        self.execute(report)
        self.assertTrue({35, 34, 33, 32}.isdisjoint(self.live.calls))
        self.assertTrue(set(range(1, 11)).isdisjoint(self.live.calls))
        self.assertTrue(all(call.args[0] == cleanup.API for call in self.delete.call_args_list))

    def test_current_previous_and_ghcr_changes_abort_before_first_delete(self):
        for change in ("current", "previous", "ghcr", "workers"):
            with self.subTest(change=change):
                state, inventories = copy.deepcopy(self.live.state), copy.deepcopy(self.live.inventories)
                report = self.audit()
                if change == "current":
                    self.live.state["app"]["traffic"][0]["revisionName"] = "candidate-zero"
                elif change == "previous":
                    self.live.state["app"]["traffic"][1]["revisionName"] = "candidate-zero"
                else:
                    package = cleanup.API if change == "ghcr" else cleanup.WORKERS
                    self.live.inventories[package][0]["tags"].append("sha-new")
                with self.assertRaisesRegex(cleanup.UnsafeState, "inventory changed"):
                    self.execute(report)
                self.delete.assert_not_called()
                self.assertEqual(0, report["deleted"])
                self.assertIn("snapshot_changed", report)
                self.live.state, self.live.inventories = state, inventories

    def test_change_during_graph_inspection_aborts_before_first_delete(self):
        report = self.audit()
        registry = registry_fixture(self.live.manifests)
        real_closure = registry.closure

        def changing_closure(roots):
            result = real_closure(roots)
            self.live.state["app"]["traffic"][1]["revisionName"] = "candidate-zero"
            return result

        registry.closure = changing_closure
        self.registry.side_effect = None
        self.registry.return_value = registry
        with self.assertRaises(cleanup.UnsafeState):
            self.execute(report)
        self.delete.assert_not_called()

    def test_tag_change_on_single_version_read_skips_and_stops(self):
        report = self.audit()

        def changed(row, token):
            item = self.live.get_version(row, token)
            item["tags"] = ["release"]
            return item

        self.get_version.side_effect = changed
        with self.assertRaisesRegex(cleanup.UnsafeState, "Version changed"):
            self.execute(report)
        self.delete.assert_not_called()
        self.assertEqual(35, report["skipped_versions"][0]["id"])

    def test_404_delete_is_recorded_and_remaining_deletes_are_revalidated(self):
        report = self.audit()

        def missing_once(package, row, token):
            result = self.live.delete(package, row, token)
            return 404 if row["id"] == 35 else result

        self.delete.side_effect = missing_once
        self.execute(report)
        self.assertEqual(19, report["deleted"])
        self.assertEqual(35, report["skipped_versions"][0]["id"])
        self.assertTrue(report["skipped_versions"][0]["absent"])
        self.assertEqual(5, report["remaining_delete_candidate"])

    def test_404_get_after_first_request_does_not_delete_absent_version(self):
        report = self.audit()

        def missing_once(row, token):
            if row["id"] == 34:
                self.live.remove(34)
                return None
            return self.live.get_version(row, token)

        self.get_version.side_effect = missing_once
        self.execute(report)
        self.assertEqual(19, report["deleted"])
        self.assertNotIn(34, self.live.calls)

    def test_404_get_before_first_request_prevents_using_changed_initial_snapshot(self):
        report = self.audit()

        def missing_once(row, token):
            self.live.remove(row["id"])
            return None

        self.get_version.side_effect = missing_once
        with self.assertRaisesRegex(cleanup.UnsafeState, "inventory changed"):
            self.execute(report)
        self.delete.assert_not_called()
        self.assertTrue(report["skipped_versions"][0]["absent"])

    def test_404_without_inventory_confirmation_aborts(self):
        report = self.audit()
        self.delete.side_effect = lambda *args: 404
        with self.assertRaisesRegex(cleanup.UnsafeState, "inventory changed"):
            self.execute(report)
        self.assertEqual(1, self.delete.call_count)

    def test_dependency_of_deferred_candidate_is_not_deleted(self):
        # Version 11 is outside the limit and depends on the oldest candidate 35.
        self.live.manifests[digest(11)] = {"schemaVersion": 2, "mediaType": "application/vnd.oci.image.index.v1+json",
                                          "manifests": [{"digest": digest(35)}]}
        report = self.audit()
        self.assertIn(35, [row["id"] for row in report["candidates"]])
        self.execute(report)
        self.assertNotIn(35, self.live.calls)
        self.assertEqual(19, report["deleted"])
        self.assertIn("retained", report["skipped_versions"][0]["reason"])

    def test_unknown_candidate_manifest_blocks_all_deletions(self):
        self.live.manifests[digest(35)] = {"schemaVersion": 2, "mediaType": "unknown"}
        report = self.audit()
        self.assertFalse(report["candidates"])
        self.execute(report)
        self.delete.assert_not_called()

    def test_partial_failure_preserves_confirmed_count_and_summary(self):
        def fail_second(package, row, token):
            if row["id"] == 34:
                raise cleanup.DeleteFailure("DELETE failed with HTTP 403", 403)
            return self.live.delete(package, row, token)

        self.delete.side_effect = fail_second
        with patch.object(cleanup.sys, "argv", ["ghcr_cleanup.py", "--output", str(self.output)]), patch("builtins.print"):
            self.assertEqual(1, cleanup.main())
        stored = json.loads(self.output.read_text())
        self.assertEqual(1, stored["deleted"])
        self.assertEqual("FAILED", stored["delete_attempts"][1]["outcome"])
        self.assertEqual(403, stored["delete_attempts"][1]["http_status"])
        self.assertEqual([35], self.live.calls)
        self.assertIn("Deleted: **1**", self.summary.read_text(encoding="utf-8"))
        self.assertNotIn("Deleted: **0**", self.summary.read_text(encoding="utf-8"))

    def test_azure_change_after_success_stops_remaining_deletes(self):
        report = self.audit()

        def change_after_delete(package, row, token):
            status = self.live.delete(package, row, token)
            self.live.state["app"]["traffic"][1]["revisionName"] = "candidate-zero"
            return status

        self.delete.side_effect = change_after_delete
        with self.assertRaisesRegex(cleanup.UnsafeState, "inventory changed"):
            self.execute(report)
        self.assertEqual([35], self.live.calls)
        self.assertEqual(1, report["deleted"])

    def test_repeated_runs_recalculate_inventory_and_preserve_newest_ten(self):
        for expected_deletes in (20, 5, 0):
            report = self.audit()
            self.execute(report)
            self.assertEqual(expected_deletes, report["deleted"])
        self.assertEqual(25, len(set(self.live.calls)))
        self.assertEqual(list(range(1, 11)), [v["id"] for v in self.live.inventories[cleanup.API]])

    def test_configured_workers_have_only_keep_or_skipped_status(self):
        workers_state = copy.deepcopy(self.live.state)
        workers_image = f"ghcr.io/hemodinks/hemodinks-api-workers@{digest(4)}"
        workers_state["app"]["images"] = [workers_image]
        for revision in workers_state["revisions"]:
            revision["images"] = [workers_image]
        self.azure.side_effect = lambda name, group: copy.deepcopy(
            self.live.state if name == cleanup.API_APP else workers_state)
        with patch.dict(os.environ, {"WORKERS_APP_NAME": "workers", "WORKERS_RESOURCE_GROUP": "workers-rg"}):
            report = self.audit()
            self.execute(report)
        workers = report["packages"][1]["versions"]
        self.assertEqual("KEEP", next(v["status"] for v in workers if v["id"] == 4))
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", next(v["status"] for v in workers if v["id"] == 15))
        self.assertTrue(all(v["status"] in ("KEEP", "SKIPPED_UNSAFE_TO_DELETE") for v in workers))
        self.assertTrue(all(call.args[0] == cleanup.API for call in self.delete.call_args_list))

    def test_interrupted_delete_keeps_unknown_outcome_in_checkpoint(self):
        report = self.audit()
        self.delete.side_effect = KeyboardInterrupt
        with self.assertRaises(KeyboardInterrupt):
            self.execute(report)
        stored = json.loads(self.output.read_text())
        self.assertEqual("PENDING_OR_UNKNOWN", stored["delete_attempts"][0]["outcome"])
        with patch.object(cleanup.sys, "argv", ["ghcr_cleanup.py", "--failure-summary", "--output", str(self.output)]), patch("builtins.print"):
            self.assertEqual(0, cleanup.main())
        self.assertIn("unknown outcome", self.summary.read_text(encoding="utf-8"))


class TransportTests(unittest.TestCase):
    def row(self, **changes):
        return {**version(99), "status": "DELETE_CANDIDATE", "candidate_type": "tagged", **changes}

    def test_invalid_or_missing_ref_never_issues_http_request(self):
        for ref in INVALID_DELETE_REFS:
            env = {key: value for key, value in DELETE_ENV.items() if key != "GITHUB_REF"}
            if ref is not None:
                env["GITHUB_REF"] = ref
            with self.subTest(ref=ref), patch.dict(os.environ, env, clear=True), \
                 patch.object(cleanup, "build_opener") as opener:
                with self.assertRaisesRegex(cleanup.UnsafeState, "DELETE is allowed only from refs/heads/main"):
                    cleanup.delete_version(cleanup.API, self.row(), "fake")
                opener.assert_not_called()

    def test_package_status_and_tag_guards_before_http(self):
        rows = [self.row(status=status) for status in ("PROTECTED", "KEEP", "SKIPPED_UNSAFE_TO_DELETE")]
        rows += [self.row(tags=[]), self.row(tags=["sha-abc", "v1"])]
        with patch.dict(os.environ, DELETE_ENV, clear=True), patch.object(cleanup, "build_opener") as opener:
            for row in rows:
                with self.subTest(row=row), self.assertRaises(cleanup.UnsafeState):
                    cleanup.delete_version(cleanup.API, row, "fake")
            with self.assertRaises(cleanup.UnsafeState):
                cleanup.delete_version(cleanup.WORKERS, self.row(), "fake")
            opener.assert_not_called()

    def test_false_or_missing_concurrency_context_never_issues_delete(self):
        for override in ({"EXECUTE_DELETE": "false"}, {"GITHUB_EVENT_NAME": "schedule"},
                         {"CLEANUP_CONCURRENCY_GROUP": "ghcr-cleanup-dry-run"}, {"GITHUB_REPOSITORY": "other/repo"}):
            with patch.dict(os.environ, {**DELETE_ENV, **override}, clear=True), patch.object(cleanup, "build_opener") as opener:
                with self.assertRaises(cleanup.UnsafeState):
                    cleanup.delete_version(cleanup.API, self.row(), "fake")
                opener.assert_not_called()

    def test_delete_204_404_and_abort_statuses_use_exact_version_endpoint(self):
        for status in (204, 404, 401, 403, 409, 429, 500):
            with self.subTest(status=status), patch.dict(os.environ, DELETE_ENV, clear=True), \
                 patch.object(cleanup, "build_opener") as opener:
                if status == 204:
                    opener.return_value.open.return_value.__enter__.return_value.status = status
                else:
                    opener.return_value.open.side_effect = HTTPError("https://api.github.com", status, "test", {}, io.BytesIO())
                if status in (204, 404):
                    self.assertEqual(status, cleanup.delete_version(cleanup.API, self.row(), "fake"))
                else:
                    with self.assertRaisesRegex(cleanup.DeleteFailure, str(status)):
                        cleanup.delete_version(cleanup.API, self.row(), "fake")
                request = opener.return_value.open.call_args.args[0]
                self.assertEqual("DELETE", request.get_method())
                self.assertEqual("https://api.github.com/orgs/hemodinks/packages/container/hemodinks-api/versions/99", request.full_url)

    def test_network_failure_has_unknown_outcome_and_is_not_retried(self):
        with patch.dict(os.environ, DELETE_ENV, clear=True), patch.object(cleanup, "build_opener") as opener:
            opener.return_value.open.side_effect = URLError("unavailable")
            with self.assertRaisesRegex(cleanup.DeleteFailure, "outcome unknown"):
                cleanup.delete_version(cleanup.API, self.row(), "fake")
            self.assertEqual(1, opener.return_value.open.call_count)


if __name__ == "__main__":
    unittest.main()
