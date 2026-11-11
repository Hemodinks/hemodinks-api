#!/usr/bin/env bash
# Passo "Warm up and smoke test candidate" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP, GITHUB_SHA, CANDIDATE_REVISION, CANDIDATE_FQDN; escreve em GITHUB_OUTPUT.
set -euo pipefail
ready=false
deadline=$((SECONDS + 600))
for attempt in $(seq 1 30); do
  if [ "$SECONDS" -ge "$deadline" ]; then break; fi
  revision_json="$(timeout 20s az containerapp revision show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --revision "$CANDIDATE_REVISION" \
    --output json || true)"
  state="$(jq -r '[.properties.healthState,.properties.runningState,.properties.provisioningState] | @tsv' <<< "$revision_json" 2>/dev/null || true)"
  live_status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --connect-timeout 5 --max-time 10 "https://${CANDIDATE_FQDN}/livez" || true)"
  ready_status="$(curl --silent --show-error --output /tmp/readiness.json --write-out '%{http_code}' \
    --connect-timeout 5 --max-time 20 "https://${CANDIDATE_FQDN}/healthz" || true)"
  commit="$(jq -r '.properties.template.containers[0].env[]? | select(.name == "Deployment__CommitSha") | .value' <<< "$revision_json" 2>/dev/null || true)"
  if [[ "$state" == *"Healthy"* && "$state" == *"Running"* && "$live_status" = "200" && "$ready_status" = "200" && "$commit" = "$GITHUB_SHA" ]]; then
    ready=true
    break
  fi
  echo "Candidate warm-up attempt ${attempt}/30 (state=${state}, live=${live_status}, ready=${ready_status})."
  if jq -e '.properties.provisioningState == "Failed" or .properties.runningState == "Failed"' <<<"$revision_json" > /dev/null 2>&1; then
    echo "::error::Candidate startup/provisioning failed. Inspect its Azure console/system logs; promotion is blocked."
    break
  fi
  sleep $(( attempt < 6 ? attempt * 2 : 12 ))
done
rm -f /tmp/readiness.json
if [ "$ready" != "true" ]; then
  echo "::error::Candidate failed readiness, database, or commit smoke validation. Production traffic was not changed."
  exit 7
fi
echo "result=passed" >> "$GITHUB_OUTPUT"
