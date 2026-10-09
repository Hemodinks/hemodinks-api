# GHCR retention — isolated offline proof of concept

This module implements **classification only**. There is deliberately no GitHub Actions workflow, network client, Azure authentication, or DELETE path.

Reason: the GitHub connector did not provide a complete listing of existing workflow files, so existing image-cleanup/rollback rules could not be audited. Wiring an operational workflow before inspecting those rules would risk duplication or missed dependencies.

## Safety invariant

The sole supported policy is 30 days, keep latest 5, dry-run enabled and deletion disabled. The input must contain explicitly authorized packages, fully paginated GHCR versions and **verified** Azure digest inventory covering all required environments and active/inactive revisions. Never mark an inventory complete unless a separate trusted collector has confirmed this, including mutable-tag resolution and OCI manifest identity. Unresolved references block all candidates.

**This classifier is not an inventory collector and must never receive synthetic inventory for a live cleanup decision.**

## Local test

`python -m unittest discover -s tools/ghcr_retention -p 'test_*.py'`

## Offline command

`python tools/ghcr_retention/retention.py --inventory inventory.json --report report.json`

Sample shape (values illustrative only):

```json
{
  "policy": {"dry_run": true, "delete_enabled": false, "retention_days": 30, "keep_latest_versions": 5, "package_allowlist": ["VALIDATED_PACKAGE"]},
  "ghcr": {"complete": false, "versions": []},
  "azure": {"complete": false, "protected_digests": [], "unresolved_image_references": ["inventory has not been verified"]}
}
```

## Blockers before live weekly automation

1. List and review every existing `.github/workflows/` file, cleanup policy, image tag convention, rollback policy and Azure subscription/resource scope.
2. Build authenticated, fully paginated **read-only** GHCR/Azure collectors, including all revisions, envs, workers, digest/OCI index and mutable-tag reconciliation. A historic tag-to-digest mapping cannot be inferred from its current registry value.
3. Integrate verified collectors with this policy and produce Markdown/JSON audit artifacts, workflow summary, timeouts, permissions and tests for partial API responses.
4. Validate candidate classifications against actual protected deployments. Only then propose a separate PR adding a weekly read-only workflow.

**Never add DELETE in this change.**
