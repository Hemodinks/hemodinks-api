# Exercise bootstrap against an in-process Azure CLI stub. No network or Azure writes.
$ErrorActionPreference = 'Stop'
$global:proxyBootstrapTestCalls = 0
$global:proxyBootstrapTestPatch = $null
function az {
    $global:proxyBootstrapTestCalls++
    $global:LASTEXITCODE = 0
    $command = $args -join ' '
    if ($command -like 'account show*') { return 'test-subscription' }
    if ($command -like 'containerapp show*latestRevisionName*') { return 'candidate-test' }
    if ($command -like 'containerapp show*') {
        return '{"name":"hemodinks-api-prod","resourceGroup":"rg-hemodinks-prod","id":"/test/resource","properties":{"configuration":{"ingress":{"targetPort":8080}}}}'
    }
    if ($command -like 'containerapp revision list*') {
        return '[{"name":"current-test","properties":{"active":true,"healthState":"Healthy","runningState":"Running","createdTime":"2026-01-01"}}]'
    }
    if ($command -like 'containerapp revision show*--query*') { return "Healthy`nRunning" }
    if ($command -like 'containerapp revision show*') {
        return '{"properties":{"template":{"containers":[{"name":"api","image":"test-image","env":[{"name":"forwardedheaders:knownnetworks:9","value":"0.0.0.0/0"},{"name":"ForwardedHeaders__TrustAnyImmediateProxy","value":"true"},{"name":"BusinessSecret","secretRef":"retained-secret"}]}]}}}'
    }
    if ($args[0] -eq 'rest') {
        $global:proxyBootstrapTestPatch = $args[[Array]::IndexOf($args, '--body') + 1] | ConvertFrom-Json
    }
}
$bootstrap = Join-Path $PSScriptRoot '../Bootstrap-ProductionBlueGreen.ps1'
try {
    & $bootstrap -SubscriptionId 'test-subscription' -WhatIf 2>$null
    throw 'Missing allowlist was accepted.'
} catch {
    if ($_.Exception.Message -notlike '*KnownProxies/KnownNetworks*') { throw }
}
if ($global:proxyBootstrapTestCalls -ne 0) { throw 'Invalid preflight reached Azure.' }
& $bootstrap -SubscriptionId 'test-subscription' -KnownProxies @('10.20.0.4') -Confirm:$false | Out-Null
$container = $global:proxyBootstrapTestPatch.properties.template.containers[0]
$entries = @($container.env)
if ($container.image -ne 'test-image') { throw 'Image changed.' }
if (($entries | Where-Object name -eq 'ForwardedHeaders__TrustAnyImmediateProxy').value -ne 'false') { throw 'Unsafe trust.' }
if (($entries | Where-Object name -eq 'ForwardedHeaders__KnownProxies__0').value -ne '10.20.0.4') { throw 'Missing approved proxy.' }
if ($entries | Where-Object name -eq 'forwardedheaders:knownnetworks:9') { throw 'Stale network retained.' }
if (($entries | Where-Object name -eq 'BusinessSecret').secretRef -ne 'retained-secret') { throw 'Unrelated secretref changed.' }
'Bootstrap tests passed: invalid policy stops before Azure; valid policy generates safe PATCH with unrelated settings preserved.'
