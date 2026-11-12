#!/usr/bin/env bash
# Passo "Create candidate revision with zero traffic" do job deploy-api (publish-container.yml), extraido sem mudanca de comportamento (#229).
# Entradas: API_NAME, API_RESOURCE_GROUP, API_IMAGE, GITHUB_SHA, NEW_RELIC_LICENSE_KEY, FORWARDED_HEADERS_ENV; escreve em GITHUB_OUTPUT.
set -euo pipefail
printf '[]' | python3 -B scripts/forwarded_headers.py --merge "$FORWARDED_HEADERS_ENV" > /dev/null
az containerapp show --name "$API_NAME" --resource-group "$API_RESOURCE_GROUP" \
  --query '{name:name,resourceGroup:resourceGroup,properties:{configuration:{ingress:properties.configuration.ingress}}}' -o json \
  | python3 -B scripts/forwarded_headers.py --validate-azure-app-config
traffic_json="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.ingress.traffic -o json)"
total_weight="$(jq '[.[] | (.weight // 0)] | add // 0' <<<"$traffic_json")"
production_count="$(jq '[.[] | select((.weight // 0) == 100 and (.label == "blue" or .label == "green"))] | length' <<<"$traffic_json")"
active_label="$(jq -r '[.[] | select(.weight == 100 and (.label == "blue" or .label == "green"))][0].label // empty' <<<"$traffic_json")"
previous_revision="$(jq -r --arg label "$active_label" '[.[] | select(.label == $label)][0].revisionName // empty' <<<"$traffic_json")"
if [ "$total_weight" -ne 100 ] || [ "$production_count" -ne 1 ] || [ -z "$active_label" ] || [ -z "$previous_revision" ]; then
  echo "::error::Could not uniquely identify the 100% blue/green CURRENT_REVISION. Run the bootstrap/recovery procedure."
  exit 5
fi

if [ "$active_label" = "blue" ]; then candidate_label="green"; else candidate_label="blue"; fi
short_sha="${GITHUB_SHA:0:10}"
suffix="sha-${short_sha}"
candidate_revision="${API_NAME}--${suffix}"

active_revision_json="$(az containerapp revision show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --revision "$previous_revision" \
  --output json)"
if [ "$candidate_revision" = "$previous_revision" ]; then
  if ! jq -e --arg image "$API_IMAGE" --arg commit "$GITHUB_SHA" '
    .properties.template.containers[0].image == $image and
    ([.properties.template.containers[0].env[]? | select(.name == "Deployment__CommitSha") | .value] == [$commit])
  ' <<<"$active_revision_json" >/dev/null; then
    echo "::error::CURRENT revision does not match the requested image and full commit. Reuse is blocked."
    exit 8
  fi
  if ! jq -c '.properties.template.containers[0].env // []' <<<"$active_revision_json" \
    | python3 -B scripts/forwarded_headers.py --merge "$FORWARDED_HEADERS_ENV" --verify-existing; then
    echo "::error::CURRENT revision proxy policy differs from the approved preflight. Reuse is blocked."
    exit 8
  fi
  app_fqdn="$(az containerapp show \
    --name "$API_NAME" --resource-group "$API_RESOURCE_GROUP" \
    --query properties.configuration.ingress.fqdn -o tsv)"
  {
    echo "already_current=true"
    echo "active_label=$active_label"
    echo "candidate_label=$active_label"
    echo "previous_revision=$previous_revision"
    echo "candidate_revision=$candidate_revision"
    echo "candidate_fqdn=$app_fqdn"
  } >> "$GITHUB_OUTPUT"
  echo "::notice::Requested API revision is already CURRENT. Labels, secrets and revisions are preserved; smoke and traffic validation are still required."
  exit 0
fi

# CPF em repouso (#227): a API nao sobe sem estes secrets fora de Development/Testing.
# As variaveis nao sao copiadas da revisao ativa; a candidata sempre as referencia por secretRef.
# Confere so os nomes (nunca os valores) e falha antes de qualquer mutacao.
secret_names="$(az containerapp secret list \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query "[].name" -o tsv)"
for required_secret in cpf-protection-certificate cpf-protection-certificate-password cpf-protection-lookup-hash-key; do
  if ! grep -qx -- "$required_secret" <<<"$secret_names"; then
    echo "::error::Container App secret '$required_secret' is missing. Configure CPF protection secrets before deploying (docs/issue-227-cpf-em-repouso.md)."
    exit 10
  fi
done

new_relic_enabled=false
if [ -n "$NEW_RELIC_LICENSE_KEY" ]; then
  az containerapp secret set \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --secrets "new-relic-license-key=$NEW_RELIC_LICENSE_KEY" \
    --output none
  new_relic_enabled=true
else
  echo "::notice::New Relic APM configuration was not changed because NEW_RELIC_LICENSE_KEY is not configured."
fi

target_port="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.ingress.targetPort -o tsv)"
app_id="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query id -o tsv)"
if ! [[ "$target_port" =~ ^[0-9]+$ ]] || [ -z "$app_id" ]; then
  echo "::error::The target port or Container App resource ID could not be resolved."
  exit 6
fi

environment_json="$(jq -c '.properties.template.containers[0].env // []' <<<"$active_revision_json" \
  | python3 -B scripts/forwarded_headers.py --merge "$FORWARDED_HEADERS_ENV")"
containers="$(jq \
  --arg image "$API_IMAGE" \
  --arg commit "$GITHUB_SHA" \
  --arg new_relic_enabled "$new_relic_enabled" \
  --argjson environment "$environment_json" \
  --argjson port "$target_port" '
    .properties.template.containers
    | .[0].image = $image
    | .[0].env = (
        $environment
        | map(select(
            .name != "Deployment__CommitSha"
            and .name != "Database__RunMigrationsOnStartup"
            and .name != "Database__RunMaintenanceOnStartup"
            and .name != "Database__SchemaManagedByDeployment"
            and .name != "EventReminders__RunHostedProcessor"
            and .name != "CpfProtection__CertificateBase64"
            and .name != "CpfProtection__CertificatePassword"
            and .name != "CpfProtection__LookupHashKey"
            and ($new_relic_enabled != "true" or (
              .name != "CORECLR_ENABLE_PROFILING"
              and .name != "NEW_RELIC_LICENSE_KEY"
              and .name != "NEW_RELIC_APP_NAME"
              and .name != "NEW_RELIC_DISTRIBUTED_TRACING_ENABLED"
            ))
          ))
        + [
            {name: "Deployment__CommitSha", value: $commit},
            {name: "Database__RunMigrationsOnStartup", value: "false"},
            {name: "Database__RunMaintenanceOnStartup", value: "false"},
            {name: "Database__SchemaManagedByDeployment", value: "true"},
            {name: "EventReminders__RunHostedProcessor", value: "false"},
            {name: "CpfProtection__CertificateBase64", secretRef: "cpf-protection-certificate"},
            {name: "CpfProtection__CertificatePassword", secretRef: "cpf-protection-certificate-password"},
            {name: "CpfProtection__LookupHashKey", secretRef: "cpf-protection-lookup-hash-key"}
          ]
        + (if $new_relic_enabled == "true" then [
            {name: "CORECLR_ENABLE_PROFILING", value: "1"},
            {name: "NEW_RELIC_LICENSE_KEY", secretRef: "new-relic-license-key"},
            {name: "NEW_RELIC_APP_NAME", value: "Hemodinks API"},
            {name: "NEW_RELIC_DISTRIBUTED_TRACING_ENABLED", value: "true"}
          ] else [] end)
      )
    | .[0].probes = [
        {
          type: "Startup",
          httpGet: {path: "/livez", port: $port, scheme: "HTTP"},
          initialDelaySeconds: 1,
          periodSeconds: 3,
          timeoutSeconds: 2,
          failureThreshold: 30,
          successThreshold: 1
        },
        {
          type: "Liveness",
          httpGet: {path: "/livez", port: $port, scheme: "HTTP"},
          initialDelaySeconds: 10,
          periodSeconds: 10,
          timeoutSeconds: 2,
          failureThreshold: 3,
          successThreshold: 1
        },
        {
          type: "Readiness",
          # Database-free: a 5 s probe on /healthz kept serverless SQL awake (#209).
          # The candidate smoke step still requires /healthz (database) before promotion.
          httpGet: {path: "/livez", port: $port, scheme: "HTTP"},
          initialDelaySeconds: 3,
          periodSeconds: 5,
          timeoutSeconds: 3,
          failureThreshold: 6,
          successThreshold: 1
        }
      ]
  ' <<<"$active_revision_json")"
patch_body="$(jq -n \
  --arg suffix "$suffix" \
  --argjson containers "$containers" '
    {
      properties: {
        template: {
          revisionSuffix: $suffix,
          containers: $containers,
          scale: {minReplicas: 0, maxReplicas: 1}
        }
      }
    }
  ')"
if existing_candidate_json="$(az containerapp revision show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --revision "$candidate_revision" \
  --output json 2>/dev/null)"; then
  if ! jq -c '.properties.template.containers[0].env // []' <<<"$existing_candidate_json" \
    | python3 -B scripts/forwarded_headers.py --merge "$FORWARDED_HEADERS_ENV" --verify-existing; then
    echo "::error::Existing candidate proxy policy differs from the approved preflight. Reuse and promotion are blocked."
    exit 8
  fi
  if ! jq -e '
    [.properties.template.containers[0].env[]?
      | select(.name == "CpfProtection__CertificateBase64" or .name == "CpfProtection__CertificatePassword"
          or .name == "CpfProtection__LookupHashKey")
      | {name, secretRef}]
    | sort_by(.name) == [
        {name: "CpfProtection__CertificateBase64", secretRef: "cpf-protection-certificate"},
        {name: "CpfProtection__CertificatePassword", secretRef: "cpf-protection-certificate-password"},
        {name: "CpfProtection__LookupHashKey", secretRef: "cpf-protection-lookup-hash-key"}
      ]
  ' <<<"$existing_candidate_json" >/dev/null; then
    echo "::error::Existing candidate does not reference the CPF protection secrets. Delete or deactivate it and rerun the deploy."
    exit 8
  fi
  echo "::notice::Reusing existing candidate revision '$candidate_revision'."
else
  az rest \
    --method patch \
    --uri "https://management.azure.com${app_id}?api-version=2025-07-01" \
    --body "$patch_body" \
    --output none
fi

candidate_provisioned=false
for attempt in $(seq 1 30); do
  actual_revision="$(az containerapp show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --query properties.latestRevisionName -o tsv)"
  provisioning_state="$(az containerapp show \
    --name "$API_NAME" \
    --resource-group "$API_RESOURCE_GROUP" \
    --query properties.provisioningState -o tsv)"
  if [ "$actual_revision" = "$candidate_revision" ] && [ "$provisioning_state" = "Succeeded" ]; then
    candidate_provisioned=true
    break
  fi
  echo "Candidate provisioning attempt ${attempt}/30 (latest=${actual_revision}, state=${provisioning_state})."
  sleep $(( attempt < 6 ? attempt * 2 : 10 ))
done
if [ "$candidate_provisioned" != "true" ]; then
  echo "::error::Expected provisioned revision '$candidate_revision', got '$actual_revision' with state '$provisioning_state'."
  exit 7
fi

az containerapp revision label add \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --revision "$candidate_revision" \
  --label "$candidate_label" \
  --yes \
  --output none
az containerapp ingress traffic set \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --label-weight "$active_label=100" "$candidate_label=0" \
  --output none

app_fqdn="$(az containerapp show \
  --name "$API_NAME" \
  --resource-group "$API_RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv)"
domain="${app_fqdn#*.}"
candidate_fqdn="${API_NAME}---${candidate_label}.${domain}"

{
  echo "already_current=false"
  echo "active_label=$active_label"
  echo "candidate_label=$candidate_label"
  echo "previous_revision=$previous_revision"
  echo "candidate_revision=$candidate_revision"
  echo "candidate_fqdn=$candidate_fqdn"
} >> "$GITHUB_OUTPUT"
