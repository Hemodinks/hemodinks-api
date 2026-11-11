#!/usr/bin/env bash
# Passo "Deployment summary" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP, API_IMAGE, GITHUB_SHA e resultados dos passos anteriores; escreve em GITHUB_STEP_SUMMARY.
# Mesmas opcoes do shell padrao de 'run:' no GitHub Actions (bash -e -o pipefail).
set -eo pipefail
final_traffic="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.ingress.traffic -o json 2>/dev/null | jq -c '.' || echo unavailable)"
{
  echo "## Production API Blue/Green"
  echo ""
  echo "- Commit: \`${GITHUB_SHA}\`"
  echo "- Image: \`${API_IMAGE}\`"
  echo "- Revision active before this run: \`${PREVIOUS_REVISION:-unavailable}\`"
  echo "- Requested revision: \`${CANDIDATE_REVISION:-unavailable}\`"
  echo "- Active label after success: \`${ACTIVE_LABEL:-unchanged}\`"
  echo "- Migration: **passed**"
  echo "- Smoke test: **${SMOKE_RESULT}**"
  echo "- Promotion: **${PROMOTION_RESULT}**"
  echo "- Automatic traffic rollback: **${ROLLBACK_RESULT}**"
  echo "- Revision policy: **${REVISION_POLICY}**"
  echo "- Active revisions: **${ACTIVE_REVISION_COUNT}**"
  echo "- Old revisions deactivated: **${OLD_REVISIONS_DEACTIVATED}**"
  echo "- Deployment: **${DEPLOYMENT_RESULT}**"
  echo "- Final traffic: \`${final_traffic}\`"
} >> "$GITHUB_STEP_SUMMARY"
