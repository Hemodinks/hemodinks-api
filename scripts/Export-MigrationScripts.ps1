[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutputDir = (Join-Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")).Path "artifacts") "migrations"),
    [string]$FromMigration,
    [string]$ToMigration,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-SafeName {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return ($Value -replace "[^A-Za-z0-9_.-]", "-")
}

$infraProject = Join-Path (Join-Path $ProjectRoot "HemodinksAPI.Infrastructure") "HemodinksAPI.Infrastructure.csproj"
$apiProject = Join-Path (Join-Path $ProjectRoot "HemodinksAPI.Api") "HemodinksAPI.Api.csproj"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Push-Location $ProjectRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore falhou com codigo $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$arguments = @("migrations", "script")

if ($PSBoundParameters.ContainsKey("FromMigration")) {
    $arguments += $FromMigration
}

if ($PSBoundParameters.ContainsKey("ToMigration")) {
    $arguments += $ToMigration
}

if (-not $PSBoundParameters.ContainsKey("FromMigration") -and -not $PSBoundParameters.ContainsKey("ToMigration")) {
    $arguments += "--idempotent"
    $fileName = "$timestamp-migrations-idempotent.sql"
}
else {
    $fromLabel = if ($PSBoundParameters.ContainsKey("FromMigration")) { Get-SafeName -Value $FromMigration } else { "from-start" }
    $toLabel = if ($PSBoundParameters.ContainsKey("ToMigration")) { Get-SafeName -Value $ToMigration } else { "to-latest" }
    $fileName = "$timestamp-migrations-$fromLabel-to-$toLabel.sql"
}

$scriptPath = Join-Path $OutputDir $fileName

$arguments += @(
    "--project", $infraProject,
    "--startup-project", $apiProject,
    "--output", $scriptPath
)

if ($NoBuild) {
    $arguments += "--no-build"
}

$toolArguments = $arguments

& dotnet tool run dotnet-ef @toolArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet tool run dotnet-ef $($toolArguments -join ' ') falhou com codigo $LASTEXITCODE."
}

Write-Output "Script gerado em: $scriptPath"

if (-not $PSBoundParameters.ContainsKey("FromMigration") -and -not $PSBoundParameters.ContainsKey("ToMigration")) {
    Write-Output "Tipo: rollout idempotente para aplicar migrations pendentes."
}
else {
    Write-Output "Tipo: script direcionado de upgrade/rollback entre migrations especificadas."
}
