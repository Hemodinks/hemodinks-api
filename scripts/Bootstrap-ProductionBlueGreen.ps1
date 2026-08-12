[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory)]
    [string]$SubscriptionId,
    [string]$ResourceGroup = "rg-hemodinks-prod",
    [string]$ContainerAppName = "hemodinks-api-prod"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ResourceGroup -ne "rg-hemodinks-prod" -or $ContainerAppName -ne "hemodinks-api-prod") {
    throw "Este bootstrap e restrito a hemodinks-api-prod no Resource Group rg-hemodinks-prod."
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI (az) nao encontrado."
}

$signedInSubscription = (& az account show --query id -o tsv 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($signedInSubscription)) {
    throw "Faca login na Azure CLI antes de executar o bootstrap."
}

if ($signedInSubscription -ne $SubscriptionId) {
    throw "Subscription ativa '$signedInSubscription' difere da esperada '$SubscriptionId'."
}

& az group show --name $ResourceGroup --subscription $SubscriptionId --output none
if ($LASTEXITCODE -ne 0) {
    throw "Resource Group '$ResourceGroup' nao encontrado na subscription informada."
}

$app = & az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $app) {
    throw "Container App '$ContainerAppName' nao encontrado em '$ResourceGroup'."
}

if ($app.name -ne $ContainerAppName -or $app.resourceGroup -ne $ResourceGroup) {
    throw "O recurso retornado nao corresponde exatamente ao Container App de Producao esperado."
}

$readyRevision = @(
    & az containerapp revision list `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --subscription $SubscriptionId `
        --output json | ConvertFrom-Json |
        Where-Object {
            $_.properties.active -eq $true -and
            $_.properties.healthState -eq "Healthy" -and
            $_.properties.runningState -eq "Running"
        } |
        Sort-Object { $_.properties.createdTime } -Descending
)[0]

if ($null -eq $readyRevision) {
    throw "Nenhuma revisao ativa, Healthy e Running foi encontrada. Nenhuma alteracao foi feita."
}

$readyRevisionDetails = & az containerapp revision show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --revision $readyRevision.name `
    --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $null -eq $readyRevisionDetails) {
    throw "Nao foi possivel ler a configuracao da revisao pronta '$($readyRevision.name)'."
}

if (-not $PSCmdlet.ShouldProcess(
    "$ContainerAppName/$ResourceGroup",
    "habilitar multiple revisions, reforcar migrations=false e inicializar o label blue")) {
    return
}

& az containerapp revision set-mode `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --mode multiple `
    --output none
if ($LASTEXITCODE -ne 0) { throw "Falha ao habilitar multiple revisions." }

# O PATCH reutiliza o container completo retornado pela Azure, preservando imagem,
# recursos e env vars/secretrefs. Escala e limites de replicas nao sao enviados.
$container = $readyRevisionDetails.properties.template.containers[0]
$targetPort = [int]$app.properties.configuration.ingress.targetPort
if ($targetPort -le 0) {
    throw "O Container App nao possui targetPort HTTP valido para configurar probes."
}
$environmentVariables = @($container.env)
foreach ($setting in @(
    @{ name = "Database__RunMigrationsOnStartup"; value = "false" },
    @{ name = "Database__RunMaintenanceOnStartup"; value = "false" }
)) {
    $existing = $environmentVariables | Where-Object { $_.name -eq $setting.name } | Select-Object -First 1
    if ($null -eq $existing) {
        $environmentVariables += [PSCustomObject]$setting
    }
    else {
        $existing.PSObject.Properties.Remove("secretRef")
        $existing.value = $setting.value
    }
}
$container | Add-Member -MemberType NoteProperty -Name env -Value $environmentVariables -Force
$container | Add-Member -MemberType NoteProperty -Name probes -Value @(
    @{
        type = "Startup"
        httpGet = @{ path = "/healthz"; port = $targetPort; scheme = "HTTP" }
        initialDelaySeconds = 1
        periodSeconds = 3
        timeoutSeconds = 2
        failureThreshold = 30
        successThreshold = 1
    },
    @{
        type = "Liveness"
        httpGet = @{ path = "/healthz"; port = $targetPort; scheme = "HTTP" }
        initialDelaySeconds = 10
        periodSeconds = 10
        timeoutSeconds = 2
        failureThreshold = 3
        successThreshold = 1
    },
    @{
        type = "Readiness"
        httpGet = @{ path = "/healthz"; port = $targetPort; scheme = "HTTP" }
        initialDelaySeconds = 3
        periodSeconds = 5
        timeoutSeconds = 3
        failureThreshold = 6
        successThreshold = 1
    }
) -Force

$suffix = "bootstrap-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
$patchBody = @{
    properties = @{
        template = @{
            revisionSuffix = $suffix
            containers = @($container)
        }
    }
} | ConvertTo-Json -Depth 30 -Compress
$resourceUri = "https://management.azure.com$($app.id)?api-version=2025-07-01"
& az rest --method patch --uri $resourceUri --body $patchBody --output none
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao reforcar migrations=false e configurar as probes HTTP."
}

$currentRevision = (& az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --query properties.latestRevisionName `
    --output tsv).Trim()
if ([string]::IsNullOrWhiteSpace($currentRevision)) {
    throw "A revisao atual nao pode ser identificada depois da atualizacao."
}

for ($attempt = 1; $attempt -le 30; $attempt++) {
    $state = & az containerapp revision show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --subscription $SubscriptionId `
        --revision $currentRevision `
        --query "[properties.healthState,properties.runningState]" `
        --output tsv
    if ($state -match "Healthy" -and $state -match "Running") { break }
    if ($attempt -eq 30) { throw "A revisao '$currentRevision' nao ficou pronta." }
    Start-Sleep -Seconds ([Math]::Min(2 * $attempt, 10))
}

& az containerapp revision label add `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --revision $currentRevision `
    --label blue `
    --yes `
    --output none
if ($LASTEXITCODE -ne 0) { throw "Falha ao atribuir o label blue." }

& az containerapp ingress traffic set `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --subscription $SubscriptionId `
    --label-weight blue=100 `
    --output none
if ($LASTEXITCODE -ne 0) { throw "Falha ao direcionar o trafego para blue." }

Write-Output "Bootstrap concluido. Revisao ativa: $currentRevision; label: blue; trafego: 100%."
