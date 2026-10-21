#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$fixture = $PSScriptRoot
$composeFile = Join-Path $fixture 'compose.systemd.yaml'
$runner = Join-Path $fixture 'test-compose-systemd.ps1'
$dockerExecutable = (Get-Command docker -CommandType Application | Select-Object -First 1).Source
$global:servalMockConfigJson = & $dockerExecutable compose --file $composeFile config --format json
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the Compose fixture.' }
$badConfig = $global:servalMockConfigJson | ConvertFrom-Json
$badConfig.services.systemd | Add-Member -NotePropertyName volumes -NotePropertyValue @('/tmp:/host-data:ro')
$global:servalMockBadConfigJson = $badConfig | ConvertTo-Json -Depth 20

function docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    $global:servalMockCalls.Add(($Arguments -join ' ')) | Out-Null
    $global:LASTEXITCODE = 0
    if ($Arguments -contains 'config') {
        if ($global:servalMockScenario -eq 'mount-config') { return $global:servalMockBadConfigJson }
        return $global:servalMockConfigJson
    }
    if ($Arguments -contains 'up') {
        if ($global:servalMockScenario -eq 'startup') { $global:LASTEXITCODE = 1 }
        return
    }
    if ($Arguments -contains 'ps') { return 'mock-container' }
    if ($Arguments -contains 'inspect') {
        if ($Arguments -contains '{{json .Mounts}}') {
            if ($global:servalMockScenario -eq 'mount-runtime') {
                return '[{"Type":"bind","Destination":"/host-data"}]'
            }
            return '[{"Type":"tmpfs","Destination":"/run"},{"Type":"tmpfs","Destination":"/run/lock"}]'
        }
        if ($Arguments -contains '{{json .HostConfig}}') { return '{"Binds":null}' }
        if ($Arguments -contains '{{.State.Running}} {{.State.Health.Status}}') { return 'true healthy' }
        if ($Arguments -contains '{{json .State}}') { return '{"Running":false,"OOMKilled":false,"ExitCode":137,"Error":""}' }
    }
    if ($Arguments -contains 'exec') {
        if ($Arguments -contains 'cat') { return 'systemd' }
        if ($global:servalMockScenario -eq 'query') { $global:LASTEXITCODE = 1; return }
        return 'running'
    }
    if ($Arguments -contains 'stop' -or $Arguments -contains 'logs' -or $Arguments -contains 'down') { return }
    throw "Unexpected mocked Docker call: $($Arguments -join ' ')"
}

foreach ($case in @(
    @{ Name = 'startup'; Expected = 'Docker command failed' },
    @{ Name = 'query'; Expected = 'Docker command failed' },
    @{ Name = 'forced-stop'; Expected = 'systemd did not shut down cleanly' },
    @{ Name = 'mount-config'; Expected = 'Forbidden Compose setting: volumes' },
    @{ Name = 'mount-runtime'; Expected = 'Forbidden container mount' }
)) {
    $global:servalMockScenario = $case.Name
    $global:servalMockCalls = [System.Collections.ArrayList]::new()
    $failed = $false
    try {
        & $runner
    }
    catch {
        $failed = $_.Exception.Message.Contains($case.Expected)
    }
    if (-not $failed) { throw "Expected $($case.Name) failure was not reported." }
    if (-not ($global:servalMockCalls | Where-Object { $_ -match ' down --remove-orphans ' })) {
        throw "Compose cleanup was skipped after $($case.Name) failure."
    }
    if ($case.Name -eq 'mount-config' -and ($global:servalMockCalls | Where-Object { $_ -match ' up --build ' })) {
        throw 'A forbidden Compose mount reached container startup.'
    }
}

Write-Host 'Compose startup, manager-query, forced-stop and forbidden-mount failures were rejected and cleaned up.'
