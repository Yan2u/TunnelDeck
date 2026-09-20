[CmdletBinding()]
param(
    [switch] $Ssh,
    [string] $Distribution = 'Ubuntu',
    [switch] $NoFixtureBuild,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$resultsPath = Join-Path $repoRoot 'artifacts\test-results'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
foreach ($project in @('TunnelDeck.Core.Tests', 'TunnelDeck.Infrastructure.Tests')) {
    & dotnet test (Join-Path $repoRoot "tests\$project\$project.csproj") -c $Configuration -p:Platform=x64 `
        --logger "trx;LogFileName=$project.trx" --results-directory $resultsPath -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "$project failed ($LASTEXITCODE)." }
}

if ($Ssh) {
    $previousFixture = $env:TUNNELDECK_SSH_FIXTURE
    $fixturePath = $null
    try {
        $fixturePath = & (Join-Path $repoRoot 'test-infra\Start-SshFixture.ps1') `
            -Distribution $Distribution -NoBuild:$NoFixtureBuild
        $env:TUNNELDECK_SSH_FIXTURE = $fixturePath
        & dotnet test (Join-Path $repoRoot 'tests\TunnelDeck.Ssh.Tests\TunnelDeck.Ssh.Tests.csproj') `
            -c $Configuration -p:Platform=x64 --logger 'trx;LogFileName=TunnelDeck.Ssh.Tests.trx' `
            --logger 'console;verbosity=normal' --results-directory $resultsPath
        if ($LASTEXITCODE -ne 0) { throw "Real SSH tests failed ($LASTEXITCODE)." }
    }
    finally {
        $env:TUNNELDECK_SSH_FIXTURE = $previousFixture
        if ($fixturePath) { & (Join-Path $repoRoot 'test-infra\Stop-SshFixture.ps1') -ManifestPath $fixturePath }
    }
}
