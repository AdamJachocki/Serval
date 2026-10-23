#!/usr/bin/env pwsh
param([switch] $InjectFailureAfterStart)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$container = "serval-f5-smoke-$suffix"
$image = "serval-f5-smoke-$suffix"
$client = Join-Path $root "artifacts/f5-client-$suffix"
$created = $false
$imageBuilt = $false

function Run([string] $Executable, [string[]] $Arguments, [int] $Seconds = 120,
    [string] $PrivateInput = $null) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $root
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void] $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void] $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not [string]::IsNullOrEmpty($PrivateInput)) { $process.StandardInput.Write($PrivateInput + "`n") }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($Seconds * 1000)) {
            $process.Kill($true)
            throw "$Executable timed out at $($Arguments[0])."
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            # Private-input operations must never echo their payload or response.
            if ([string]::IsNullOrEmpty($PrivateInput)) { Write-Warning $output.Trim() }
            elseif ($output -match 'Development authorization probe failed \(assertion line [0-9]+, phase [A-Za-z]+, type [A-Za-z]+\)\.') {
                Write-Warning $Matches[0]
            }
            throw "$Executable failed at $($Arguments[0]) (exit $($process.ExitCode))."
        }
        return $output.Trim()
    }
    finally { $process.Dispose() }
}
function Exec([string[]] $Arguments) { Run docker (@('exec', $container) + $Arguments) }

try {
    $architecture = Run docker @('info', '--format', '{{.Architecture}}')
    $runtime = switch ($architecture) {
        { $_ -in 'x86_64', 'amd64' } { 'linux-x64'; break }
        { $_ -in 'aarch64', 'arm64' } { 'linux-arm64'; break }
        default { throw 'Unsupported Docker architecture.' }
    }
    Run pwsh @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'prepare-context.ps1'), '-Runtime', $runtime) 600 | Out-Null
    Run dotnet @('restore', 'tests/Serval.Agent.FixtureClient', '--locked-mode') 180 | Out-Null
    Run dotnet @('publish', 'tests/Serval.Agent.FixtureClient', '-c', 'Release', '-r', $runtime,
        '--self-contained', 'true', '--no-restore', '-o', $client) 180 | Out-Null
    Run docker @('build', '-t', $image, 'artifacts/docker-visual-studio/context') 600 | Out-Null
    $imageBuilt = $true
    Run docker @('run', '-d', '-t', '--name', $container, '--privileged', '--cgroupns=private',
        '--ipc=private', '--tmpfs', '/run:rw,exec,mode=755', '--tmpfs', '/run/lock:rw,mode=755',
        '--env', 'ASPNETCORE_ENVIRONMENT=Development', '--env', 'ASPNETCORE_URLS=http://0.0.0.0:8080', $image) | Out-Null
    $created = $true
    if ($InjectFailureAfterStart) { throw 'Injected development fixture failure.' }
    $isolation = (Run docker @('inspect', $container, '--format',
        '{{json .HostConfig}}')) | ConvertFrom-Json
    if (-not $isolation.Privileged -or $isolation.CgroupnsMode -ne 'private' -or
        $isolation.IpcMode -ne 'private' -or $isolation.PidMode -eq 'host' -or
        $isolation.Binds.Count -ne 0 -or $isolation.PortBindings.PSObject.Properties.Count -gt 0) {
        throw 'Unexpected development smoke isolation.'
    }
    Write-Host 'Runtime: privileged disposable container, private PID/IPC/cgroup, tmpfs /run, no binds or published ports.'
    Exec @('timeout', '90s', '/opt/serval/development/start-agent.sh') | Out-Null
    Run docker @('exec', '-d', $container, '/opt/serval/development/start-web.sh') | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Exec @('/opt/serval/development/verify-development-host.sh') | Out-Null; $ready = $true; break }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'Development Web readiness failed.' }
    Run docker @('cp', "$client/.", "${container}:/opt/serval/probe") | Out-Null
    Exec @('chmod', '0755', '/opt/serval/probe/Serval.Agent.FixtureClient') | Out-Null
    Exec @('useradd', '--create-home', '--shell', '/bin/sh', 'serval-denied') | Out-Null
    $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    Run docker @('exec', '-i', $container, 'chpasswd') 20 "serval-denied:$password" | Out-Null
    Exec @('ln', '-s', '/etc/systemd/system/serval-agent.service', '/etc/systemd/system/serval-development-protected-alias.service') | Out-Null
    Exec @('timeout', '5s', 'systemctl', 'daemon-reload') | Out-Null
    Run docker @('exec', '-i', '--user', 'serval-web', $container,
        '/opt/serval/probe/Serval.Agent.FixtureClient', '--development-services') 75 "serval-dev`nserval`nserval-denied`n$password" | Out-Null
    $password = $null
    Write-Host 'Agent and rendered HTTP pages: allowed, denied, anonymous, malformed and protected alias checks passed.'
    Run docker @('cp', (Join-Path $PSScriptRoot 'test-development-host.sh'), "${container}:/tmp/test-development-host.sh") | Out-Null
    Exec @('timeout', '180s', 'sh', '/tmp/test-development-host.sh') | Write-Host
    Run docker @('stop', '--time', '30', $container) 40 | Out-Null
    $state = (Run docker @('inspect', $container, '--format', '{{json .State}}')) | ConvertFrom-Json
    if ($state.Running -or $state.OOMKilled -or $state.ExitCode -notin 0, 130) { throw 'Ungraceful systemd shutdown.' }
    Write-Host 'Development smoke passed.'
}
finally {
    if ($created) { Run docker @('rm', '--force', $container) 40 | Out-Null }
    if ($imageBuilt) { Run docker @('image', 'rm', '--force', $image) 40 | Out-Null }
    $expected = [IO.Path]::GetFullPath((Join-Path $root "artifacts/f5-client-$suffix"))
    if ([IO.Path]::GetFullPath($client) -ne $expected) { throw 'Unsafe client cleanup path.' }
    if (Test-Path -LiteralPath $client) { Remove-Item -LiteralPath $client -Recurse -Force }
}

