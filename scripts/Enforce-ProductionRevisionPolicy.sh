#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <current-revision> <previous-revision>" >&2
  exit 2
fi

CURRENT_REVISION="$1"
PREVIOUS_REVISION="$2"
EXPECTED_API_NAME="hemodinks-api-prod"
EXPECTED_RESOURCE_GROUP="rg-hemodinks-prod"

if [ "${API_NAME:-}" != "$EXPECTED_API_NAME" ] || [ "${API_RESOURCE_GROUP:-}" != "$EXPECTED_RESOURCE_GROUP" ]; then
  echo "::error::Revision cleanup is restricted to ${EXPECTED_API_NAME}/${EXPECTED_RESOURCE_GROUP}."
  exit 3
fi

mode="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.activeRevisionsMode \
  --output tsv)"
if [ "${mode,,}" != "multiple" ]; then
  echo "::error::Revision cleanup requires activeRevisionsMode=Multiple."
  exit 4
fi
if [ -z "$CURRENT_REVISION" ] || [ -z "$PREVIOUS_REVISION" ] || [ "$CURRENT_REVISION" = "$PREVIOUS_REVISION" ]; then
  echo "::error::CURRENT_REVISION and PREVIOUS_REVISION must be different, non-empty revisions."
  exit 4
fi

traffic_for_revision() {
  local traffic_json="$1"
  local revision="$2"
  jq -r --arg revision "$revision" \
    '[.[] | select(.revisionName == $revision) | (.weight // 0)] | add // 0' \
    <<<"$traffic_json"
}

load_traffic() {
  az containerapp show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --query properties.configuration.ingress.traffic \
    --output json
}

verify_current_traffic() {
  local traffic_json="$1"
  jq -e --arg current "$CURRENT_REVISION" '
    ([.[] | (.weight // 0)] | add // 0) == 100 and
    ([.[] | select((.weight // 0) > 0)] | length) == 1 and
    any(.[]; .revisionName == $current and (.weight // 0) == 100)
  ' <<<"$traffic_json" >/dev/null
}

traffic_json="$(load_traffic)"
if ! verify_current_traffic "$traffic_json"; then
  echo "::error::Cleanup blocked: CURRENT_REVISION is not the unique revision receiving 100% of traffic."
  exit 5
fi

current_active="$(az containerapp revision show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --revision "$CURRENT_REVISION" \
  --query properties.active \
  --output tsv)"
if [ "${current_active,,}" != "true" ]; then
  echo "::error::Cleanup blocked: CURRENT_REVISION is not active."
  exit 6
fi

revisions_json="$(az containerapp revision list \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --all \
  --output json)"
previous_exists="$(jq -r --arg previous "$PREVIOUS_REVISION" \
  'any(.[]; .name == $previous and .properties.active == true)' <<<"$revisions_json")"
if [ "$previous_exists" != "true" ]; then
  echo "::error::Cleanup blocked: PREVIOUS_REVISION is not active and available for fast rollback."
  exit 7
fi

old_revisions_deactivated=0
mapfile -t active_revisions < <(jq -r '.[] | select(.properties.active == true) | .name' <<<"$revisions_json")

for revision in "${active_revisions[@]}"; do
  traffic_json="$(load_traffic)"
  revision_traffic="$(traffic_for_revision "$traffic_json" "$revision")"
  active="$(az containerapp revision show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --revision "$revision" \
    --query properties.active \
    --output tsv)"

  if [ "$revision" = "$CURRENT_REVISION" ]; then
    action="PRESERVE CURRENT"
  elif [ "$revision" = "$PREVIOUS_REVISION" ]; then
    action="PRESERVE PREVIOUS"
  elif [ "$revision_traffic" != "0" ]; then
    action="BLOCKED: TRAFFIC > 0"
  elif [ "${active,,}" != "true" ]; then
    action="SKIP ALREADY INACTIVE"
  else
    action="DEACTIVATE"
  fi

  printf '%s\n' \
    '[REVISION CLEANUP]' \
    "Revision: $revision" \
    "Traffic: $revision_traffic" \
    "Active: $active" \
    "Action: $action"

  if [ "$action" = "BLOCKED: TRAFFIC > 0" ]; then
    echo "::error::Refusing to deactivate an old revision that still receives traffic."
    exit 8
  fi
  if [ "$action" != "DEACTIVATE" ]; then
    continue
  fi

  # Revalidate the global invariant immediately before every destructive operation.
  traffic_json="$(load_traffic)"
  if ! verify_current_traffic "$traffic_json"; then
    echo "::error::Traffic changed during cleanup; no further revision will be deactivated."
    exit 9
  fi
  revision_traffic="$(traffic_for_revision "$traffic_json" "$revision")"
  if [ "$revision_traffic" != "0" ]; then
    echo "::error::Traffic for '$revision' changed during cleanup; refusing to deactivate it."
    exit 10
  fi

  az containerapp revision deactivate \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --revision "$revision" \
    --output none
  old_revisions_deactivated=$((old_revisions_deactivated + 1))
done

for attempt in $(seq 1 12); do
  final_traffic="$(load_traffic)"
  final_revisions="$(az containerapp revision list \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --all \
    --output json)"
  active_count="$(jq '[.[] | select(.properties.active == true)] | length' <<<"$final_revisions")"
  preserved_active="$(jq -r \
    --arg current "$CURRENT_REVISION" \
    --arg previous "$PREVIOUS_REVISION" \
    '[.[] | select(.properties.active == true and (.name == $current or .name == $previous))] | length' \
    <<<"$final_revisions")"
  unexpected_active="$(jq -r \
    --arg current "$CURRENT_REVISION" \
    --arg previous "$PREVIOUS_REVISION" \
    '[.[] | select(.properties.active == true and .name != $current and .name != $previous)] | length' \
    <<<"$final_revisions")"
  if verify_current_traffic "$final_traffic" && [ "$active_count" -eq 2 ] && [ "$preserved_active" -eq 2 ] && [ "$unexpected_active" -eq 0 ]; then
    break
  fi
  if [ "$attempt" -lt 12 ]; then
    echo "Waiting for revision deactivation confirmation, attempt ${attempt}/12."
    sleep 5
  fi
done

policy_status="OK"
if ! verify_current_traffic "$final_traffic" || [ "$active_count" -ne 2 ] || [ "$preserved_active" -ne 2 ] || [ "$unexpected_active" -ne 0 ]; then
  policy_status="ERROR"
fi

printf '%s\n' \
  '[REVISION POLICY]' \
  "Current: $CURRENT_REVISION" \
  "Previous: $PREVIOUS_REVISION" \
  "Active revisions: $active_count" \
  "Old revisions deactivated: $old_revisions_deactivated" \
  "Status: $policy_status"

az containerapp revision list \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --all \
  --query '[].{Revision:name,Active:properties.active,Replicas:properties.replicas,Traffic:properties.trafficWeight}' \
  --output table

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "active_count=$active_count"
    echo "old_revisions_deactivated=$old_revisions_deactivated"
    echo "status=$policy_status"
  } >> "$GITHUB_OUTPUT"
fi

if [ "$policy_status" != "OK" ]; then
  echo "::error::The final active revision set violates the CURRENT/PREVIOUS policy."
  exit 11
fi
