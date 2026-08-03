[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$SkipEfCli,
    [switch]$NoBuild,
    [switch]$FailOnDestructiveChanges,
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DotNetEf {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & dotnet tool run dotnet-ef @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool run dotnet-ef $($Arguments -join ' ') falhou com codigo $LASTEXITCODE."
    }
}

$migrationsDir = Join-Path (Join-Path (Join-Path $ProjectRoot "HemodinksAPI.Infrastructure") "Data") "Migrations"
$infraProject = Join-Path (Join-Path $ProjectRoot "HemodinksAPI.Infrastructure") "HemodinksAPI.Infrastructure.csproj"
$apiProject = Join-Path (Join-Path $ProjectRoot "HemodinksAPI.Api") "HemodinksAPI.Api.csproj"
$snapshotPath = Join-Path $migrationsDir "AppDbContextModelSnapshot.cs"

if (-not (Test-Path $migrationsDir)) {
    throw "Pasta de migrations nao encontrada: $migrationsDir"
}

if (-not (Test-Path $snapshotPath)) {
    throw "Snapshot do EF Core nao encontrado: $snapshotPath"
}

$migrationFiles = Get-ChildItem -Path $migrationsDir -Filter "*.cs" |
    Where-Object { $_.Name -notlike "*.Designer.cs" -and $_.Name -ne "AppDbContextModelSnapshot.cs" } |
    Sort-Object Name

if ($migrationFiles.Count -eq 0) {
    throw "Nenhuma migration encontrada em $migrationsDir"
}

$designerNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -Path $migrationsDir -Filter "*.Designer.cs" | ForEach-Object {
    [void]$designerNames.Add(($_.Name -replace "\.Designer\.cs$", ""))
}

$report = foreach ($file in $migrationFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $migrationName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $hasUp = $content -match "protected\s+override\s+void\s+Up\s*\("
    $hasDown = $content -match "protected\s+override\s+void\s+Down\s*\("
    $upMatch = [System.Text.RegularExpressions.Regex]::Match(
        $content,
        "protected\s+override\s+void\s+Up\s*\(MigrationBuilder\s+\w+\)\s*\{(?<body>[\s\S]*?)^\s*\}\s*(?:protected|$)",
        [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $upBody = if ($upMatch.Success) { $upMatch.Groups["body"].Value } else { $content }
    $hasDestructiveUp = $upBody -match "(?i)\b(DropTable|DropColumn|DeleteData)\s*\(" `
        -or $upBody -match "(?i)\b(DROP\s+(TABLE|COLUMN)|TRUNCATE\s+TABLE|DELETE\s+FROM)\b"

    if (-not $hasUp) {
        throw "Migration sem Up(): $migrationName"
    }

    if (-not $hasDown) {
        throw "Migration sem Down(): $migrationName"
    }

    $downMatch = [System.Text.RegularExpressions.Regex]::Match(
        $content,
        "protected\s+override\s+void\s+Down\s*\(MigrationBuilder\s+\w+\)\s*\{(?<body>[\s\S]*?)^\s*\}",
        [System.Text.RegularExpressions.RegexOptions]::Multiline)

    $downBody = if ($downMatch.Success) { $downMatch.Groups["body"].Value } else { "" }
    $downBody = [System.Text.RegularExpressions.Regex]::Replace($downBody, "(?s)/\*.*?\*/", "")
    $downBody = [System.Text.RegularExpressions.Regex]::Replace($downBody, "(?m)^\s*//.*$", "")
    $downIsEmpty = [string]::IsNullOrWhiteSpace($downBody)
    $rollbackMayDeleteData = $downBody -match "DropTable\(|DropColumn\(|DeleteData\("

    if ($downIsEmpty -and $content -notmatch "Intentionally empty") {
        throw "Migration com Down() vazio sem justificativa explicita: $migrationName"
    }

    [PSCustomObject]@{
        Migration                = $migrationName
        HasDesigner              = $designerNames.Contains($migrationName)
        UsesRawSql               = $content.Contains("migrationBuilder.Sql(")
        RollbackMayDeleteData    = $rollbackMayDeleteData
        HasDestructiveUp         = $hasDestructiveUp
        HasEmptyDown             = $downIsEmpty
    }
}

$manualMigrations = $report | Where-Object { -not $_.HasDesigner }
$reviewMigrations = $report | Where-Object { $_.UsesRawSql -or $_.HasEmptyDown -or -not $_.HasDesigner }
$rollbackSensitiveMigrations = $report | Where-Object { $_.RollbackMayDeleteData }
$destructiveMigrations = $report | Where-Object { $_.HasDestructiveUp }

Write-Output "Auditadas $($report.Count) migrations em $migrationsDir"
Write-Output "Snapshot encontrado: $snapshotPath"

if (@($manualMigrations).Count -gt 0) {
    Write-Warning "Migrations sem arquivo .Designer.cs detectadas. Revise se foram criadas manualmente de forma intencional."
    $manualMigrations |
        Select-Object Migration, HasDesigner |
        Format-Table -AutoSize |
        Out-String |
        Write-Output
}

if (@($reviewMigrations).Count -gt 0) {
    Write-Warning "Migrations que exigem revisao operacional antes de rollout/rollback:"
    $reviewMigrations |
        Select-Object Migration, UsesRawSql, HasEmptyDown, HasDesigner |
        Format-Table -AutoSize |
        Out-String |
        Write-Output
}

if (@($rollbackSensitiveMigrations).Count -gt 0) {
    Write-Warning "Migrations cujo Down() remove schema ou dados e, por isso, pedem backup/PITR antes de rollback:"
    $rollbackSensitiveMigrations |
        Select-Object Migration, RollbackMayDeleteData |
        Format-Table -AutoSize |
        Out-String |
        Write-Output
}

if (@($destructiveMigrations).Count -gt 0) {
    $destructiveMigrations |
        Select-Object Migration, HasDestructiveUp |
        Format-Table -AutoSize |
        Out-String |
        Write-Warning

    if ($FailOnDestructiveChanges) {
        throw "Migration destrutiva detectada no Up(). Use expand/contract e mova a remocao para um deployment posterior."
    }

    Write-Warning "Migrations destrutivas detectadas no Up(); revise a cadeia historica e bloqueie novas ocorrencias antes de Producao."
}

if (-not $SkipEfCli) {
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

    $commonArguments = @(
        "--configuration", $Configuration,
        "--project", $infraProject,
        "--startup-project", $apiProject
    )

    if ($NoBuild) {
        $commonArguments += "--no-build"
    }

    $listArguments = @("migrations", "list", "--no-connect") + $commonArguments
    $pendingArguments = @("migrations", "has-pending-model-changes") + $commonArguments

    Invoke-DotNetEf -Arguments $listArguments
    Invoke-DotNetEf -Arguments $pendingArguments
}

Write-Output "Auditoria de migrations concluida com sucesso."
