#!/usr/bin/env pwsh

param([string] $ComposeFile)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$composeFile = if ($ComposeFile) { $ComposeFile } else { Join-Path $PSScriptRoot 'compose.systemd.yaml' }
$projectName = "serval-systemd-smoke-$PID"
$composeArgs = @('compose', '--project-name', $projectName, '--file', $composeFile)
$logDirectory = Join-Path $PSScriptRoot 'artifacts'
$logFile = Join-Path $logDirectory "$projectName.log"
$failure = $null

function Invoke-Docker {
    param([string[]] $Arguments)

    $output = & docker @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed (exit $LASTEXITCODE): $($Arguments -join ' ')"
    }

    return $output.Trim()
}

function Assert-True {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) { throw $Message }
}

try {
    $config = Invoke-Docker -Arguments ($composeArgs + @('config', '--format', 'json')) | ConvertFrom-Json
    $service = $config.services.systemd
    Assert-True ($null -ne $service) 'Expected exactly one systemd service.'
    Assert-True ($config.services.PSObject.Properties.Count -eq 1) 'Unexpected Compose service.'
    Assert-True ($service.build.context -eq $PSScriptRoot) 'Build context must be the fixture directory.'
    Assert-True ($service.build.dockerfile -eq 'DebianSystemd.Dockerfile') 'Unexpected Dockerfile.'
    Assert-True ($service.image -eq 'serval-debian-systemd-smoke') 'Unexpected image.'
    Assert-True ($service.command.Count -eq 1 -and $service.command[0] -eq '/sbin/init') 'systemd must be PID 1.'
    Assert-True ($service.privileged -eq $true -and $service.cgroup -eq 'private') 'Unexpected systemd container privileges.'
    Assert-True ($service.stop_signal -eq 'SIGRTMIN+3' -and $service.stop_grace_period -eq '30s') 'Unexpected shutdown settings.'
    Assert-True ($service.tmpfs.Count -eq 2 -and
        ($service.tmpfs | Where-Object { $_ -like '/run:*' }).Count -eq 1 -and
        ($service.tmpfs | Where-Object { $_ -like '/run/lock:*' }).Count -eq 1) 'Only /run and /run/lock may be tmpfs mounts.'
    foreach ($forbidden in @('volumes', 'ports', 'secrets', 'configs', 'devices', 'network_mode', 'pid', 'ipc')) {
        Assert-True (-not ($service.PSObject.Properties.Name -contains $forbidden)) "Forbidden Compose setting: $forbidden"
    }

    Invoke-Docker -Arguments ($composeArgs + @('up', '--build', '--detach', '--wait', '--wait-timeout', '90')) | Out-Null
    $containerId = Invoke-Docker -Arguments ($composeArgs + @('ps', '--all', '--quiet', 'systemd'))
    Assert-True (-not [string]::IsNullOrWhiteSpace($containerId)) 'systemd container was not created.'

    $mounts = Invoke-Docker -Arguments @('inspect', '--format', '{{json .Mounts}}', $containerId) | ConvertFrom-Json
    foreach ($mount in @($mounts)) {
        Assert-True ($mount.Type -eq 'tmpfs' -and $mount.Destination -in @('/run', '/run/lock')) "Forbidden container mount: $($mount.Type) at $($mount.Destination)"
    }
    $hostConfig = Invoke-Docker -Arguments @('inspect', '--format', '{{json .HostConfig}}', $containerId) | ConvertFrom-Json
    Assert-True ($null -eq $hostConfig.Binds -or $hostConfig.Binds.Count -eq 0) 'Container has host bind mounts.'

    $state = Invoke-Docker -Arguments @('inspect', '--format', '{{.State.Running}} {{.State.Health.Status}}', $containerId)
    Assert-True ($state -eq 'true healthy') "Container is not running and healthy: $state"
    $managerState = Invoke-Docker -Arguments ($composeArgs + @('exec', '--no-TTY', 'systemd', 'systemctl', 'show', '--property=SystemState', '--value'))
    Assert-True (-not [string]::IsNullOrWhiteSpace($managerState)) 'systemd manager returned an empty state.'
    $pidOne = Invoke-Docker -Arguments ($composeArgs + @('exec', '--no-TTY', 'systemd', 'cat', '/proc/1/comm'))
    Assert-True ($pidOne -eq 'systemd') "Unexpected PID 1: $pidOne"

    Invoke-Docker -Arguments ($composeArgs + @('stop', '--timeout', '30', 'systemd')) | Out-Null
    $exitState = Invoke-Docker -Arguments @('inspect', '--format', '{{json .State}}', $containerId) | ConvertFrom-Json
    Assert-True (-not $exitState.Running -and -not $exitState.OOMKilled -and
        $exitState.ExitCode -in @(0, 130) -and [string]::IsNullOrEmpty($exitState.Error)) "systemd did not shut down cleanly: exit $($exitState.ExitCode)"
}
catch {
    $failure = $_.Exception.Message
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    & docker @composeArgs logs --no-color --tail 100 2>&1 | Set-Content -LiteralPath $logFile
    Write-Warning "Compose smoke failed. Container logs saved to $logFile"
}
finally {
    & docker @composeArgs down --remove-orphans --timeout 30 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $failure = @($failure, 'Compose cleanup failed.').Where({ $_ }) -join ' '
    }
}

if ($failure) { throw $failure }
Write-Host 'Debian 13 systemd Compose startup and graceful shutdown passed.'
