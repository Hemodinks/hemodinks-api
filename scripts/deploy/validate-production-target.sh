#!/usr/bin/env bash
# Passo "Validate exact Production target" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP (env do workflow).
# Mesmas opcoes do shell padrao de 'run:' no GitHub Actions (bash -e -o pipefail).
set -eo pipefail
az group show --name "$API_RESOURCE_GROUP" --output none
actual_name="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query name -o tsv)"
mode="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.activeRevisionsMode -o tsv)"
if [ "$actual_name" != "hemodinks-api-prod" ]; then
  echo "::error::The Azure resource does not match hemodinks-api-prod."
  exit 3
fi
if [ "${mode,,}" != "multiple" ]; then
  echo "::error::Run scripts/Bootstrap-ProductionBlueGreen.ps1 manually before deploying."
  exit 4
fi
