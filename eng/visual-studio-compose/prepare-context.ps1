#!/usr/bin/env pwsh

param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string] $Runtime
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$fixture = $PSScriptRoot
$root = (Resolve-Path (Join-Path $fixture '../..')).Path
$artifactRoot = Join-Path $root 'artifacts/docker-visual-studio'
$context = Join-Path $artifactRoot 'context'
$stage = Join-Path $artifactRoot ("context-stage-{0}" -f [Guid]::NewGuid().ToString('N'))

function Invoke-Native([string] $Executable, [string[]] $Arguments, [int] $TimeoutSeconds = 300) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $root
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void] $start.ArgumentList.Add($argument) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void] $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "$Executable timed out while preparing the Visual Studio Docker context: $($Arguments -join ' ')"
        }

        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "$Executable failed while preparing the Visual Studio Docker context (exit $($process.ExitCode)): $($Arguments -join ' ')`n$output"
        }

        return $output.Trim()
    }
    finally { $process.Dispose() }
}

function Assert-SafeArtifactPath([string] $Path) {
    $resolvedRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to manage a path outside the Visual Studio Docker artifact root: $resolvedPath"
    }
}

try {
    if (-not $Runtime) {
        $architecture = Invoke-Native docker @('info', '--format', '{{.Architecture}}') 30
        $Runtime = switch ($architecture) {
            { $_ -in @('x86_64', 'amd64') } { 'linux-x64'; break }
            { $_ -in @('aarch64', 'arm64') } { 'linux-arm64'; break }
            default { throw "Unsupported Linux Docker architecture: $architecture" }
        }
    }

    Assert-SafeArtifactPath $stage
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'agent'),
        (Join-Path $stage 'web') | Out-Null

    foreach ($project in @('src/Serval.Agent/Serval.Agent.csproj', 'src/Serval.Web/Serval.Web.csproj')) {
        Invoke-Native dotnet @('restore', (Join-Path $root $project), '--locked-mode') | Out-Null
    }

    Invoke-Native dotnet @('publish', (Join-Path $root 'src/Serval.Agent/Serval.Agent.csproj'),
        '--configuration', 'Debug', '--runtime', $Runtime, '--self-contained', 'true', '--no-restore',
        '--output', (Join-Path $stage 'agent')) | Out-Null
    Invoke-Native dotnet @('publish', (Join-Path $root 'src/Serval.Web/Serval.Web.csproj'),
        '--configuration', 'Debug', '--no-restore',
        '--output', (Join-Path $stage 'web')) | Out-Null

    $copies = @{
        'Dockerfile' = 'eng/visual-studio-compose/Dockerfile'
        '.dockerignore' = 'eng/visual-studio-compose/context.dockerignore'
        'serval.pam' = 'deploy/pam.d/serval'
        'start-agent.sh' = 'eng/visual-studio-compose/start-agent.sh'
        'verify-development-host.sh' = 'eng/visual-studio-compose/verify-development-host.sh'
    }
    foreach ($copy in $copies.GetEnumerator()) {
        Copy-Item -LiteralPath (Join-Path $root $copy.Value) -Destination (Join-Path $stage $copy.Key)
    }

    $expected = @('agent', 'web') + @($copies.Keys)
    $actual = @(Get-ChildItem -Force -LiteralPath $stage | ForEach-Object Name)
    if ($actual.Count -ne $expected.Count -or @($actual | Where-Object { $_ -notin $expected }).Count -ne 0) {
        throw 'The staged Docker context does not match the deployment allowlist.'
    }
    foreach ($executable in @('agent/Serval.Agent', 'web/Serval.Web.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $executable) -PathType Leaf)) {
            throw "Missing staged executable: $executable"
        }
    }
    $forbiddenNames = @(Get-ChildItem -Recurse -Force -File -LiteralPath $stage | Where-Object {
        $_.Name -match '(?i)(^|[._-])(credential|password|secret)([._-]|$)' -or
        $_.Extension -in @('.pfx', '.p12', '.pem', '.key')
    })
    if ($forbiddenNames.Count -ne 0) {
        throw 'A credential-like file name was found in the staged Docker context.'
    }

    Assert-SafeArtifactPath $context
    if (Test-Path -LiteralPath $context) { Remove-Item -LiteralPath $context -Recurse -Force }
    Move-Item -LiteralPath $stage -Destination $context
    Write-Host "Prepared allowlisted Docker context for $Runtime at $context"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Assert-SafeArtifactPath $stage
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
