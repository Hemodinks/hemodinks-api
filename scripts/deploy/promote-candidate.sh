#!/usr/bin/env bash
# Passo "Promote candidate and verify Production traffic" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP, ALREADY_CURRENT, ACTIVE_LABEL, CANDIDATE_LABEL, PREVIOUS_REVISION, CANDIDATE_REVISION; escreve em GITHUB_OUTPUT.
set -euo pipefail
az containerapp show --name "$API_NAME" --resource-group "$API_RESOURCE_GROUP" \
  --query '{name:name,resourceGroup:resourceGroup,properties:{configuration:{ingress:properties.configuration.ingress}}}' -o json \
  | python3 -B scripts/forwarded_headers.py --validate-azure-app-config

traffic_matches() {
  local label="$1"
  local revision="$2"
  local traffic
  traffic="$(az containerapp show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --query properties.configuration.ingress.traffic -o json 2>/dev/null || echo '[]')"
  jq -e --arg label "$label" --arg revision "$revision" '
    ([.[] | (.weight // 0)] | add // 0) == 100 and
    ([.[] | select((.weight // 0) > 0)] | length) == 1 and
    any(.[]; .label == $label and .revisionName == $revision and (.weight // 0) == 100)
  ' <<<"$traffic" >/dev/null
}

if [ "$ALREADY_CURRENT" = "true" ] || [ "$CANDIDATE_REVISION" = "$PREVIOUS_REVISION" ]; then
  if [ "$ALREADY_CURRENT" != "true" ] || [ "$CANDIDATE_REVISION" != "$PREVIOUS_REVISION" ] \
    || [ "$CANDIDATE_LABEL" != "$ACTIVE_LABEL" ] \
    || ! traffic_matches "$ACTIVE_LABEL" "$PREVIOUS_REVISION"; then
    echo "::error::Already-current API traffic could not be confirmed. No labels or traffic were changed."
    exit 8
  fi
  echo "result=already-current" >> "$GITHUB_OUTPUT"
  echo "rollback_result=not-needed" >> "$GITHUB_OUTPUT"
  exit 0
fi

if ! az containerapp ingress traffic set \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --label-weight "$ACTIVE_LABEL=0" "$CANDIDATE_LABEL=100" \
  --output none; then
  echo "::warning::The traffic command failed; the observed Azure state will determine whether rollback is required."
fi

promoted=false
for attempt in $(seq 1 12); do
  if traffic_matches "$CANDIDATE_LABEL" "$CANDIDATE_REVISION"; then
    promoted=true
    break
  fi
  echo "Waiting for candidate traffic confirmation, attempt ${attempt}/12."
  sleep 5
done

if [ "$promoted" = "true" ]; then
  echo "result=promoted" >> "$GITHUB_OUTPUT"
  echo "rollback_result=not-needed" >> "$GITHUB_OUTPUT"
  exit 0
fi

echo "::error::Candidate was not confirmed as the unique revision receiving 100% of traffic. Restoring PREVIOUS_REVISION."
echo "result=failed" >> "$GITHUB_OUTPUT"
if ! az containerapp ingress traffic set \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --label-weight "$ACTIVE_LABEL=100" "$CANDIDATE_LABEL=0" \
  --output none; then
  echo "::warning::The rollback traffic command returned an error; validating the observed state."
fi

rolled_back=false
for attempt in $(seq 1 12); do
  if traffic_matches "$ACTIVE_LABEL" "$PREVIOUS_REVISION"; then
    rolled_back=true
    break
  fi
  echo "Waiting for rollback traffic confirmation, attempt ${attempt}/12."
  sleep 5
done
if [ "$rolled_back" = "true" ]; then
  echo "rollback_result=passed" >> "$GITHUB_OUTPUT"
  echo "::error::Promotion failed and Production traffic was safely restored. Cleanup was skipped."
  exit 8
fi

echo "rollback_result=failed" >> "$GITHUB_OUTPUT"
echo "::error::URGENT: neither promotion nor rollback traffic could be confirmed. Cleanup was skipped."
exit 9
