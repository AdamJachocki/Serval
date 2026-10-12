#!/usr/bin/env pwsh

param([switch] $InjectFailureAfterStart)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$fixture = $PSScriptRoot
$root = (Resolve-Path (Join-Path $fixture '../../..')).Path
$systemdFixture = Join-Path $root 'tests/Serval.Systemd.Tests/Fixtures'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$container = "serval-agent-pam-$suffix"
$baseImage = "serval-systemd-base-$suffix"
$agentImage = "serval-agent-pam-$suffix"
$staging = Join-Path ([IO.Path]::GetTempPath()) "serval-agent-pam-$suffix"
$failure = $null
$created = $false
$password = $null
$wrongPassword = $null

function Assert-True([bool] $Condition, [string] $Label) {
    if (-not $Condition) { throw "Agent fixture check failed: $Label" }
}

function Invoke-Process([string] $Executable, [string[]] $Arguments,
    [int] $TimeoutSeconds, [string] $PrivateInput = $null) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = (Get-Location).ProviderPath
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
        if ($null -ne $PrivateInput) { $process.StandardInput.Write($PrivateInput + "`n") }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "$Executable timed out at fixture step $($Arguments[0])."
        }
        return @{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() +
            $stderr.GetAwaiter().GetResult() }
    }
    finally { $process.Dispose() }
}

function Invoke-Native([string] $Executable, [string[]] $Arguments,
    [int] $TimeoutSeconds = 180) {
    $result = Invoke-Process $Executable $Arguments $TimeoutSeconds
    if ($result.ExitCode -ne 0) {
        if ($Executable -eq 'docker' -and $Arguments[0] -eq 'build') {
            Write-Warning (($result.Output -split "`n" | Select-Object -Last 12) -join "`n")
        }
        throw "$Executable failed at fixture step $($Arguments[0]) (exit $($result.ExitCode))."
    }
    return $result.Output.Trim()
}

function Invoke-Container([string[]] $Arguments) {
    return Invoke-Native docker (@('exec', $container) + $Arguments)
}

function Send-PrivateInput([string] $InputText, [string[]] $Arguments) {
    # The credential is only written to Docker's anonymous stdin pipe.
    $result = Invoke-Process docker $Arguments 20 $InputText
    if ($result.ExitCode -ne 0) { throw 'Private-input Docker step failed.' }
    return $result.Output.Trim()
}

function Assert-Login([string] $User, [string] $Secret, [string] $Peer,
    [string] $Expected, [string] $Label) {
    $result = Send-PrivateInput "$User`n$Secret" @('exec', '-i', '--user', $Peer,
        $container, '/opt/serval/fixture/Serval.Agent.FixtureClient')
    Assert-True ($result -eq $Expected) $Label
}

try {
    Assert-True ((Invoke-Native docker @('info', '--format', '{{.OSType}}')) -eq 'linux') 'Linux Docker Engine required'
    $architecture = Invoke-Native docker @('info', '--format', '{{.Architecture}}')
    $runtime = switch ($architecture) {
        { $_ -in @('x86_64', 'amd64') } { 'linux-x64'; break }
        { $_ -in @('aarch64', 'arm64') } { 'linux-arm64'; break }
        default { throw 'Unsupported Docker architecture.' }
    }

    New-Item -ItemType Directory -Path $staging, (Join-Path $staging 'agent'),
        (Join-Path $staging 'client') | Out-Null
    Invoke-Native dotnet @('restore', (Join-Path $root 'src/Serval.Agent/Serval.Agent.csproj'),
        '--locked-mode') | Out-Null
    Invoke-Native dotnet @('restore', (Join-Path $root 'tests/Serval.Agent.FixtureClient/Serval.Agent.FixtureClient.csproj'),
        '--locked-mode') | Out-Null
    Invoke-Native dotnet @('publish', (Join-Path $root 'src/Serval.Agent/Serval.Agent.csproj'),
        '--configuration', 'Release', '--runtime', $runtime, '--self-contained', 'true',
        '--no-restore', '--output', (Join-Path $staging 'agent')) | Out-Null
    Invoke-Native dotnet @('publish', (Join-Path $root 'tests/Serval.Agent.FixtureClient/Serval.Agent.FixtureClient.csproj'),
        '--configuration', 'Release', '--runtime', $runtime, '--self-contained', 'true',
        '--no-restore', '--output', (Join-Path $staging 'client')) | Out-Null

    Copy-Item -LiteralPath (Join-Path $fixture 'DockerAgentPam.Dockerfile') -Destination (Join-Path $staging 'Dockerfile')
    Copy-Item -LiteralPath (Join-Path $fixture 'verify-docker-agent.sh') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $root 'deploy/pam.d/serval') -Destination (Join-Path $staging 'serval.pam')
    Copy-Item -LiteralPath (Join-Path $root 'deploy/systemd/serval-agent.service') -Destination $staging
    $expected = @('agent', 'client', 'Dockerfile', 'verify-docker-agent.sh', 'serval.pam', 'serval-agent.service')
    $actual = @(Get-ChildItem -LiteralPath $staging | ForEach-Object Name)
    Assert-True ($actual.Count -eq $expected.Count -and
        @($actual | Where-Object { $_ -notin $expected }).Count -eq 0) 'staging allowlist'

    Push-Location $systemdFixture
    try {
        Invoke-Native docker @('build', '--tag', $baseImage, '--file',
            'DebianSystemd.Dockerfile', '.') | Out-Null
    }
    finally { Pop-Location }
    Push-Location $staging
    try {
        Invoke-Native docker @('build', '--tag', $agentImage, '--build-arg', "BASE_IMAGE=$baseImage",
            '--file', 'Dockerfile', '.') | Out-Null
    }
    finally { Pop-Location }
    $created = $true
    Invoke-Native docker @('run', '--detach', '--name', $container, '--privileged',
        '--cgroupns', 'private', '--tmpfs', '/run:rw,exec,mode=755',
        '--tmpfs', '/run/lock:rw,mode=755', $agentImage) | Out-Null

    $inspect = Invoke-Native docker @('inspect', $container) | ConvertFrom-Json
    $spec = $inspect[0]
    Assert-True ($spec.Config.Cmd.Count -eq 1 -and $spec.Config.Cmd[0] -eq '/sbin/init') 'systemd command'
    Assert-True ($spec.HostConfig.Privileged -and $spec.HostConfig.CgroupnsMode -eq 'private') 'systemd isolation'
    foreach ($mount in $spec.Mounts) {
        Assert-True ($mount.Type -eq 'tmpfs' -and $mount.Destination -in @('/run', '/run/lock')) 'container-local mounts'
    }
    $tmpfsPaths = @($spec.HostConfig.Tmpfs.PSObject.Properties.Name)
    Assert-True ($tmpfsPaths.Count -eq 2 -and '/run' -in $tmpfsPaths -and
        '/run/lock' -in $tmpfsPaths) 'tmpfs configuration'
    Assert-True (@($spec.HostConfig.Binds | Where-Object { $_ }).Count -eq 0 -and
        @($spec.HostConfig.PortBindings.PSObject.Properties.Name | Where-Object { $_ }).Count -eq 0) 'no host binds or ports'
    Assert-True (@($spec.Config.ExposedPorts.PSObject.Properties.Name | Where-Object { $_ }).Count -eq 0) 'no exposed ports'
    if ($InjectFailureAfterStart) { throw 'Injected fixture failure.' }

    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        $state = Invoke-Process docker @('exec', $container, 'systemctl', 'is-active',
            'serval-agent.service') 10
        $socket = Invoke-Process docker @('exec', $container, 'test', '-S',
            '/run/serval/agent.sock') 10
        if ($state.ExitCode -eq 0 -and $state.Output.Trim() -eq 'active' -and
            $socket.ExitCode -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    Assert-True $ready 'Agent startup deadline'

    Invoke-Container @('useradd', '--user-group', '--no-create-home', '--shell', '/usr/sbin/nologin', 'serval-allowed') | Out-Null
    Invoke-Container @('useradd', '--user-group', '--no-create-home', '--shell', '/usr/sbin/nologin', 'serval-expired') | Out-Null
    Invoke-Container @('useradd', '--user-group', '--no-create-home', '--shell', '/usr/sbin/nologin',
        '--groups', 'serval-web', 'serval-peer') | Out-Null
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $wrongPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    Send-PrivateInput "serval-allowed:${password}`nserval-expired:${password}" @('exec', '-i', $container, 'chpasswd') | Out-Null
    Invoke-Container @('chage', '-E', '0', 'serval-expired') | Out-Null
    Invoke-Container @('/opt/serval/fixture/verify-docker-agent.sh', 'identity') | Out-Null
    Invoke-Container @('/opt/serval/fixture/verify-docker-agent.sh', 'runtime') | Out-Null
    Start-Sleep -Seconds 2
    Invoke-Container @('/opt/serval/fixture/verify-docker-agent.sh', 'runtime') | Out-Null

    Assert-Login 'serval-allowed' $password 'serval-web' 'success' 'valid PAM login'
    Assert-Login 'serval-allowed' $wrongPassword 'serval-web' 'denied' 'wrong password'
    Assert-Login 'serval-expired' $password 'serval-web' 'denied' 'PAM account denial'
    Assert-Login 'serval-allowed' $password 'serval-peer' 'unavailable' 'wrong peer UID'
    Assert-Login 'serval-allowed' $password 'root' 'unavailable' 'root peer UID'

    $allowedHash = ((Invoke-Container @('getent', 'shadow', 'serval-allowed')) -split ':')[1]
    $expiredHash = ((Invoke-Container @('getent', 'shadow', 'serval-expired')) -split ':')[1]
    Assert-True (-not [string]::IsNullOrWhiteSpace($allowedHash) -and
        -not [string]::IsNullOrWhiteSpace($expiredHash)) 'disposable password hashes'
    $history = Invoke-Native docker @('history', '--no-trunc', $agentImage)
    $metadata = (Invoke-Native docker @('image', 'inspect', $agentImage)) + $history +
        (Invoke-Native docker @('inspect', $container)) +
        (Invoke-Native docker @('logs', $container))
    Assert-True (-not $metadata.Contains($password) -and -not $metadata.Contains($wrongPassword) -and
        -not $metadata.Contains($allowedHash) -and -not $metadata.Contains($expiredHash)) 'credential absent from metadata and logs'
    $allowedHash = $null
    $expiredHash = $null
    Invoke-Container @('/opt/serval/fixture/verify-docker-agent.sh', 'startup-failures') | Out-Null
    Write-Host 'Agent Docker PAM fixture passed.'
}
catch {
    $failure = $_.Exception.Message
}
finally {
    $password = $null
    $wrongPassword = $null
    if ($created) {
        try {
            $removed = Invoke-Process docker @('rm', '--force', $container) 15
            if ($removed.ExitCode -ne 0) { $failure = 'Container cleanup failed.' }
        }
        catch { $failure = 'Container cleanup timed out.' }
    }
    try { [void] (Invoke-Process docker @('image', 'rm', '--force', $agentImage, $baseImage) 15) }
    catch { $failure = 'Image cleanup timed out.' }
    if (Test-Path -LiteralPath $staging) {
        $resolved = (Resolve-Path -LiteralPath $staging).Path
        Assert-True ($resolved -eq $staging -and (Split-Path $resolved -Leaf) -eq "serval-agent-pam-$suffix") 'staging cleanup path'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

if ($failure) { throw $failure }
