#!/usr/bin/env bash
# Passo "Ensure Production Container App is running" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP.
set -euo pipefail
app_id="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query id -o tsv)"
running_status="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.runningStatus -o tsv)"
if [ "$running_status" != "Running" ]; then
  az rest \
    --method post \
    --uri "https://management.azure.com${app_id}/start?api-version=2025-07-01" \
    --output none
fi
for attempt in $(seq 1 30); do
  running_status="$(az containerapp show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --query properties.runningStatus -o tsv)"
  if [ "$running_status" = "Running" ]; then
    exit 0
  fi
  echo "Container App start attempt ${attempt}/30 (status=${running_status})."
  sleep 5
done
echo "::error::Container App did not reach Running state."
exit 5
