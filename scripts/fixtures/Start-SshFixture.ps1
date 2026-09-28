[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string] $Distribution = 'Ubuntu',
    [string] $OutputDirectory,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runId = [Guid]::NewGuid().ToString('N')
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ".test-output\ssh-$runId"
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.test-output')) + [IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Fixture output must be a child of the repository .test-output directory.'
}
if ((Test-Path -LiteralPath $outputPath) -and (Get-ChildItem -LiteralPath $outputPath -Force | Select-Object -First 1)) {
    throw 'Fixture output must be empty. Use a new directory so an old manifest cannot be mistaken for readiness.'
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

function Invoke-FixtureDocker {
    & wsl.exe -d $Distribution -u root --exec docker @args
    if ($LASTEXITCODE -ne 0) { throw "Docker fixture command failed ($LASTEXITCODE)." }
}

$linuxContext = & wsl.exe -d $Distribution -u root --exec wslpath -a ((Join-Path $PSScriptRoot 'sshd').Replace('\', '/'))
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the fixture directory in WSL.' }
$linuxContext = $linuxContext.Trim()
$linuxOutput = & wsl.exe -d $Distribution -u root --exec wslpath -a ($outputPath.Replace('\', '/'))
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the output directory in WSL.' }
$linuxOutput = $linuxOutput.Trim()
$imageName = 'tunneldeck-sshd:openssh-8.9p1-3ubuntu0.17'
if (-not $NoBuild) {
    Invoke-FixtureDocker build --tag $imageName $linuxContext | Out-Host
}

$containerId = $null
try {
    $containerId = (Invoke-FixtureDocker run --detach --name "tunneldeck-test-$runId" `
        --label 'com.tunneldeck.fixture=sshd' --label "com.tunneldeck.run=$runId" `
        --mount "type=bind,source=$linuxOutput,target=/fixture-output" `
        --publish '127.0.0.1::2222' --publish '127.0.0.1::2223' `
        --publish '127.0.0.1::2224' --publish '127.0.0.1::2225' `
        --publish '127.0.0.1::8080' $imageName).Trim()
    $containerId | Set-Content -LiteralPath (Join-Path $outputPath 'container.id')
    # A systemd-owned Docker daemon does not keep a WSL distribution alive by itself.
    # Keep a scoped docker-wait process until this exact container exits.
    $keeper = Start-Process -FilePath 'wsl.exe' -WindowStyle Hidden -PassThru `
        -ArgumentList @('-d', $Distribution, '-u', 'root', '--exec', 'docker', 'wait', $containerId)
    if ($keeper.WaitForExit(500)) {
        throw "WSL fixture lifetime process exited early ($($keeper.ExitCode))."
    }
    $container = (Invoke-FixtureDocker inspect $containerId | ConvertFrom-Json)[0]
    $ports = $container.NetworkSettings.Ports
    $containerManifest = Join-Path $outputPath 'container-manifest.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath $containerManifest)) {
        if ([DateTime]::UtcNow -ge $deadline) {
            Invoke-FixtureDocker logs $containerId | Out-Host
            throw 'SSH fixture did not become ready in 30 seconds.'
        }
        Start-Sleep -Milliseconds 100
    }
    $manifest = Get-Content -LiteralPath $containerManifest -Raw | ConvertFrom-Json -AsHashtable
    $manifest.host = '127.0.0.1'
    $manifest.allowPort = [int]$ports.'2222/tcp'[0].HostPort
    $manifest.denyPort = [int]$ports.'2223/tcp'[0].HostPort
    $manifest.restrictedPort = [int]$ports.'2224/tcp'[0].HostPort
    $manifest.mutablePort = [int]$ports.'2225/tcp'[0].HostPort
    $manifest.controlPort = [int]$ports.'8080/tcp'[0].HostPort
    $manifest.privateKeyPath = Join-Path $outputPath 'identity'
    $manifest.plainPrivateKeyPath = Join-Path $outputPath 'identity_plain'
    $manifest.containerId = $containerId
    $manifest.runId = $runId
    $manifest.distribution = $Distribution
    $manifest.imageId = $container.Image
    $manifest.keeperProcessId = $keeper.Id
    $healthUri = "http://127.0.0.1:$($manifest.controlPort)/health"
    $health = Invoke-RestMethod -Uri $healthUri -NoProxy -TimeoutSec 10
    if (-not $health.ready) { throw 'At least one isolated sshd failed to start.' }
    $manifestPath = Join-Path $outputPath 'fixture.json'
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "Isolated SSH fixture ready. OpenSSH $($manifest.sshdVersion)."
    Write-Output $manifestPath
}
catch {
    if ($containerId) {
        Invoke-FixtureDocker rm --force $containerId | Out-Host
    }
    throw
}
