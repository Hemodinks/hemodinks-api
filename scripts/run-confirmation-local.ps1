param(
    [int]$Port = 5000,
    [string]$LaunchProfile = "confirmation-local"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "HemodinksAPI.Api"

function Get-ListeningProcesses([int]$TargetPort) {
    $connections = Get-NetTCPConnection -LocalPort $TargetPort -State Listen -ErrorAction SilentlyContinue

    if (-not $connections) {
        return @()
    }

    $processIds = $connections | Select-Object -ExpandProperty OwningProcess -Unique
    $processes = foreach ($processId in $processIds) {
        Get-CimInstance Win32_Process -Filter "ProcessId = $processId"
    }

    return $processes | Where-Object { $_ -ne $null }
}

function Stop-ExistingApiOnPort([int]$TargetPort) {
    $processes = Get-ListeningProcesses -TargetPort $TargetPort

    foreach ($process in $processes) {
        $isThisApi =
            $process.Name -like "HemodinksAPI.Api*" -or
            ($process.CommandLine -and $process.CommandLine -like "*HemodinksAPI.Api*")

        if (-not $isThisApi) {
            throw "A porta $TargetPort esta em uso por outro processo ($($process.Name), PID $($process.ProcessId)). Libere a porta manualmente antes de subir a API."
        }

        Write-Host "Encerrando instancia anterior da API na porta $TargetPort (PID $($process.ProcessId))..." -ForegroundColor Yellow
        Stop-Process -Id $process.ProcessId -Force
    }
}

Push-Location $repoRoot

try {
    Stop-ExistingApiOnPort -TargetPort $Port

    Write-Host "Subindo HemodinksAPI.Api com o profile '$LaunchProfile'..." -ForegroundColor Cyan
    dotnet run --project .\HemodinksAPI.Api --launch-profile $LaunchProfile
}
finally {
    Pop-Location
}
