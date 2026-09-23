"""Orphanhood, referrer transport and bounded deletion tests; all services are fake."""

import json
import os
import unittest
from unittest.mock import patch

from test_ghcr_cleanup import cleanup, digest, manifest, NOW, registry_fixture, version
import test_ghcr_cleanup_delete as deletion_tests


def index(*children):
    return {"schemaVersion": 2, "mediaType": "application/vnd.oci.image.index.v1+json",
            "manifests": [{"digest": digest(child)} for child in children]}


class OrphanTests(unittest.TestCase):
    def classify(self, versions, manifests=None, protected=None, unavailable=False):
        registry = registry_fixture(manifests or {v["digest"]: manifest() for v in versions})
        if unavailable:
            registry.referrers.side_effect = cleanup.UnsafeState("HTTP 404: referrers unavailable")
        rows = cleanup.classify(versions, protected or {}, NOW)
        warnings = cleanup.protect_dependencies(rows, protected or {}, registry)
        return {r["id"]: r for r in rows}, registry, warnings

    def test_orphan_older_than_fourteen_days_requires_complete_graph(self):
        rows, _, warnings = self.classify([version(1, 15, [])])
        self.assertFalse(warnings)
        self.assertEqual("DELETE_CANDIDATE", rows[1]["status"])
        self.assertEqual("untagged", rows[1]["candidate_type"])
        self.assertTrue(cleanup.deletion_allowed(cleanup.API, rows[1]))
        self.assertFalse(cleanup.deletion_allowed(cleanup.WORKERS, rows[1]))
        self.assertTrue(rows[1]["evidence"]["outside_retained_graph"])
        self.assertEqual([], rows[1]["evidence"]["incoming_references"])

    def test_age_boundary_and_young_version_dependencies(self):
        versions = [version(1, 14, []), version(2, 0, []), version(3, 30, [])]
        rows, _, _ = self.classify(versions, {digest(1): index(3), digest(2): manifest(), digest(3): manifest()})
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", rows[1]["status"])
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", rows[2]["status"])
        self.assertEqual("KEEP", rows[3]["status"])

    def test_direct_current_previous_and_recursive_dependencies(self):
        versions = [version(i, 30, []) for i in range(1, 6)]
        manifests = {digest(1): index(3), digest(2): index(4), digest(3): index(5),
                     digest(4): manifest(), digest(5): manifest()}
        rows, _, _ = self.classify(versions, manifests, {digest(1): ["CURRENT"], digest(2): ["PREVIOUS inactive"]})
        self.assertTrue(all(r["status"] == "PROTECTED" for r in rows.values()))

    def test_keep_and_ten_newest_sha_dependencies(self):
        for tags in (["sha-keep"], ["release"]):
            with self.subTest(tags=tags):
                rows, _, _ = self.classify([version(1, 30, tags), version(2, 60, [])],
                                          {digest(1): index(2), digest(2): manifest()})
                self.assertEqual("KEEP", rows[2]["status"])

    def test_reverse_subject_preserves_attestation_and_direct_azure_status(self):
        rows, registry, _ = self.classify([version(1), version(2, 30, [])],
                                         {digest(1): manifest(), digest(2): manifest(subject={"digest": digest(1)})},
                                         {digest(1): ["CURRENT"]})
        self.assertEqual("PROTECTED", rows[1]["status"])
        self.assertEqual("PROTECTED", rows[2]["status"])
        self.assertEqual({digest(1), digest(2)}, registry.closure([digest(1)]))

    def test_buildkit_attestation_and_artifact_formats_are_never_candidates(self):
        forms = [manifest(artifactType="application/spdx+json"),
                 manifest(annotations={"vnd.docker.reference.type": "attestation-manifest"}),
                 manifest(layers=[{"mediaType": "application/vnd.in-toto+json"}]),
                 manifest(config={"mediaType": "application/vnd.oci.empty.v1+json"}),
                 manifest(annotations={"vnd.docker.reference.digest": digest(1)})]
        for artifact in forms:
            with self.subTest(artifact=artifact):
                rows, _, _ = self.classify([version(1), version(2, 30, [])],
                                          {digest(1): manifest(), digest(2): artifact})
                self.assertNotEqual("DELETE_CANDIDATE", rows[2]["status"])

    def test_buildkit_descriptor_protects_platform_and_provenance(self):
        root = index(2, 3)
        root["manifests"][1]["annotations"] = {
            "vnd.docker.reference.digest": digest(2), "vnd.docker.reference.type": "attestation-manifest"}
        rows, _, _ = self.classify([version(1), version(2, 30, []), version(3, 30, [])],
                                  {digest(1): root, digest(2): manifest(), digest(3): manifest()},
                                  {digest(1): ["CURRENT"]})
        self.assertTrue(all(r["status"] == "PROTECTED" for r in rows.values()))

    def test_referrer_discovered_outside_rest_inventory_is_followed(self):
        versions = [version(1), version(2, 30, [])]
        registry = registry_fixture({digest(1): manifest(), digest(2): manifest(),
                                     digest(3): manifest(subject={"digest": digest(1)})})
        registry.referrers.side_effect = lambda d: {digest(3)} if d == digest(1) else set()
        rows = cleanup.classify(versions, {digest(1): ["CURRENT"]}, NOW)
        self.assertFalse(cleanup.protect_dependencies(rows, {digest(1): ["CURRENT"]}, registry))
        self.assertIn(digest(3), registry.closure([digest(1)]))
        self.assertEqual("DELETE_CANDIDATE", next(r["status"] for r in rows if r["id"] == 2))

    def test_incoming_reference_even_from_deletion_candidate_prevents_orphan_claim(self):
        versions = [version(i, i) for i in range(1, 12)] + [version(12, 40, [])]
        manifests = {v["digest"]: manifest() for v in versions}
        manifests[digest(11)] = index(12)
        rows, _, _ = self.classify(versions, manifests)
        self.assertEqual("DELETE_CANDIDATE", rows[11]["status"])
        self.assertNotEqual("DELETE_CANDIDATE", rows[12]["status"])

    def test_referrers_unavailable_preserves_untagged_but_keeps_tagged_policy(self):
        versions = [version(i, i) for i in range(1, 12)] + [version(12, 40, [])]
        rows, _, _ = self.classify(versions, unavailable=True)
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", rows[12]["status"])
        self.assertIn("referrers", rows[12]["reason"])
        self.assertEqual("DELETE_CANDIDATE", rows[11]["status"])

    def test_unknown_or_missing_manifest_prevents_all_untagged_deletion(self):
        for manifests in ({digest(1): manifest()},
                          {digest(1): manifest(), digest(2): {"schemaVersion": 2, "mediaType": "unknown"}}):
            rows, _, warnings = self.classify([version(1, 30, []), version(2, 30, [])], manifests)
            self.assertTrue(warnings)
            self.assertTrue(all(r["status"] == "SKIPPED_UNSAFE_TO_DELETE" for r in rows.values()))


class ReferrerTransportTests(unittest.TestCase):
    def setUp(self):
        self.registry = registry_fixture({})
        del self.registry.referrers
        self.registry.package = cleanup.API
        self.registry.headers = {"Authorization": "Bearer fake"}

    def test_empty_success_and_pagination(self):
        empty = {**index(), "manifests": []}
        with patch.object(cleanup, "get", return_value=(json.dumps(empty).encode(), {})):
            self.assertEqual(set(), self.registry.referrers(digest(1)))
        endpoint = f"/v2/hemodinks/hemodinks-api/referrers/{digest(1)}"
        pages = [(json.dumps(index(2)).encode(), {"Link": f'<{endpoint}?last=2>; rel="next"'}),
                 (json.dumps(index(3)).encode(), {})]
        with patch.object(cleanup, "get", side_effect=pages) as get:
            self.assertEqual({digest(2), digest(3)}, self.registry.referrers(digest(1)))
            self.assertEqual(2, get.call_count)

    def test_missing_malformed_filtered_or_unsafe_pagination_is_not_empty(self):
        for raw, headers in [(b"{}", {}), (json.dumps(index(2)).encode(), {"OCI-Filters-Applied": "artifactType"}),
                             (json.dumps(index(2)).encode(), {"Link": '<https://evil.test/>; rel="next"'}),
                             (json.dumps(index(2)).encode(), {"Link": '<https://ghcr.io/other>; rel="next"'})]:
            with self.subTest(raw=raw, headers=headers), patch.object(cleanup, "get", return_value=(raw, headers)) as get:
                with self.assertRaises(cleanup.UnsafeState):
                    self.registry.referrers(digest(1))
                self.assertEqual(1, get.call_count)
        with patch.object(cleanup, "get", side_effect=cleanup.UnsafeState("HTTP 404")):
            with self.assertRaises(cleanup.UnsafeState):
                self.registry.referrers(digest(1))


class UntaggedExecutionTests(unittest.TestCase):
    # Reuse only fixture helpers, without rerunning the tagged test class.
    setUp = deletion_tests.ExecutionTests.setUp
    audit = deletion_tests.ExecutionTests.audit
    execute = deletion_tests.ExecutionTests.execute

    def add_untagged(self, count=60):
        for i in range(100, 100 + count):
            self.live.inventories[cleanup.API].append(version(i, i, []))
            self.live.manifests[digest(i)] = manifest()

    def test_twenty_tagged_then_fifty_oldest_untagged_and_audit_evidence(self):
        self.add_untagged()
        report = self.audit()
        self.execute(report)
        self.assertEqual(list(range(35, 15, -1)) + list(range(159, 109, -1)), self.live.calls)
        self.assertEqual(70, report["deleted"])
        self.assertEqual(20, len(report["deleted_tagged_versions"]))
        self.assertEqual(50, len(report["deleted_untagged_versions"]))
        self.assertEqual(5, report["remaining_tagged"])
        self.assertEqual(10, report["remaining_untagged"])
        stored = json.loads(self.output.read_text())
        for row in stored["deleted_untagged_versions"]:
            self.assertEqual(row["id"], row["version_id"])
            self.assertTrue(row["timestamp"])
            self.assertTrue(row["reason"])
            self.assertTrue(row["created_at"])
            self.assertTrue(row["evidence"]["outside_retained_graph"])
            self.assertTrue(row["evidence"]["referrers_complete"])
            self.assertEqual([], row["evidence"]["incoming_references"])
        with patch("builtins.print"):
            cleanup.write_summary(report)
        summary = self.summary.read_text(encoding="utf-8")
        for text in ("DELETED_TAGGED: **20**", "DELETED_UNTAGGED: **50**", "TOTAL DELETED: **70**",
                     "REMAINING_UNTAGGED: **10**", "Workers: **deletion disabled**"):
            self.assertIn(text, summary)

    def test_dry_run_shows_orphans_and_never_deletes(self):
        self.add_untagged(2)
        with patch.dict(os.environ, {"EXECUTE_DELETE": "false"}):
            report = self.audit()
            self.execute(report)
        self.assertEqual(2, len(report["untagged_candidates"]))
        self.delete.assert_not_called()

    def test_developer_blocks_untagged_before_audit(self):
        self.add_untagged(2)
        with patch.dict(os.environ, {"GITHUB_REF": "refs/heads/developer"}):
            with self.assertRaises(cleanup.UnsafeState):
                self.audit()
        self.delete.assert_not_called()

    def test_missing_referrers_causes_zero_untagged_deletes(self):
        self.add_untagged(2)
        def factory(*args):
            registry = registry_fixture(self.live.manifests)
            registry.referrers.side_effect = cleanup.UnsafeState("HTTP 404")
            return registry
        self.registry.side_effect = factory
        report = self.audit()
        self.execute(report)
        self.assertEqual(20, report["deleted"])
        self.assertFalse(report["deleted_untagged_versions"])
        self.assertEqual(2, len(report["skipped_untagged_versions"]))

    def test_closure_failure_during_audit_preserves_every_untagged(self):
        self.add_untagged(2)
        self.live.manifests[digest(100)] = {"schemaVersion": 2, "mediaType": "unknown"}
        report = self.audit()
        self.execute(report)
        self.assertFalse(report["untagged_candidates"])
        self.assertEqual(2, len(report["skipped_untagged_versions"]))
        self.delete.assert_not_called()

    def test_closure_read_failure_after_audit_aborts_before_delete(self):
        self.add_untagged(2)
        report = self.audit()
        def factory(*args):
            registry = registry_fixture(self.live.manifests)
            registry.manifest.side_effect = cleanup.UnsafeState("Manifest read failed")
            return registry
        self.registry.side_effect = factory
        with self.assertRaisesRegex(cleanup.UnsafeState, "Manifest read failed"):
            self.execute(report)
        self.delete.assert_not_called()

    def test_new_referrer_without_inventory_change_aborts(self):
        self.add_untagged(2)
        report = self.audit()
        self.live.manifests[digest(999)] = manifest(subject={"digest": digest(101)})
        def factory(*args):
            registry = registry_fixture(self.live.manifests)
            registry.referrers.side_effect = lambda d: {digest(999)} if d == digest(101) else set()
            return registry
        self.registry.side_effect = factory
        with self.assertRaisesRegex(cleanup.UnsafeState, "relationships changed"):
            self.execute(report)
        self.delete.assert_not_called()

    def test_referrer_change_between_requests_is_requeried_and_aborts(self):
        self.add_untagged(2)
        report = self.audit()
        def factory(*args):
            registry = registry_fixture(self.live.manifests)
            def referrers(d):
                return {digest(999)} if self.live.calls and d == digest(101) else set()
            registry.referrers.side_effect = referrers
            return registry
        self.live.manifests[digest(999)] = manifest(subject={"digest": digest(101)})
        self.registry.side_effect = factory
        with self.assertRaisesRegex(cleanup.UnsafeState, "relationships changed"):
            self.execute(report)
        self.assertEqual([35], self.live.calls)

    def test_untagged_gains_tag_on_final_get_aborts(self):
        self.add_untagged(1)
        report = self.audit()
        def changed(row, token):
            result = self.live.get_version(row, token)
            if not result["tags"]:
                result["tags"] = ["sha-new"]
            return result
        self.get_version.side_effect = changed
        with self.assertRaisesRegex(cleanup.UnsafeState, "Version changed"):
            self.execute(report)
        self.assertNotIn(100, self.live.calls)
        self.assertEqual(100, report["skipped_untagged_versions"][-1]["id"])

    def test_workers_untagged_never_enter_plan(self):
        self.add_untagged(1)
        self.live.inventories[cleanup.WORKERS] = [version(100, 100, [])]
        report = self.audit()
        self.execute(report)
        self.assertTrue(all(call.args[0] == cleanup.API for call in self.delete.call_args_list))
        self.assertEqual("SKIPPED_UNSAFE_TO_DELETE", report["packages"][1]["versions"][0]["status"])


if __name__ == "__main__":
    unittest.main()
