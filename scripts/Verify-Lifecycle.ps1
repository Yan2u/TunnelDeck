[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $InstanceOnly,
    [switch] $NoFixtureBuild,
    [string] $Distribution = 'Ubuntu'
)
. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Lifecycle-TestSupport.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $repoRoot 'artifacts/m10-review/native-lifecycle'
$executable = Join-Path $repoRoot "TunnelDeck/bin/x64/$Configuration/net10.0-windows10.0.19041.0/TunnelDeck.exe"
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$checks = [ordered]@{}
$appProcess = $null
$fixturePath = $null
$lockedFile = $null
$ports = $null
$echo = $null
$passed = $false
try {
    . (Join-Path $PSScriptRoot 'Lifecycle-InstanceChecks.ps1')
    if (-not $InstanceOnly) {
        $fixturePath = & (Join-Path $repoRoot 'test-infra/Start-SshFixture.ps1') -Distribution $Distribution -NoBuild:$NoFixtureBuild
        $manifest = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
        . (Join-Path $PSScriptRoot 'Lifecycle-ForwardingChecks.ps1')
    }
    $passed = $true
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Life-Capture 'failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose(); $lockedFile = $null }
    try {
        if ($fixturePath) { $null = Life-Post resume @{ server = 'mutable' } }
        if ($appProcess -and -not $appProcess.HasExited) {
            $dialog = Life-Ui search ShutdownConfirmation
            if ($dialog.matchCount -gt 0) { Life-Invoke PrimaryButton }
            Life-Close
        }
    }
    finally {
        if ($echo) { $echo.Dispose() }
        if ($ports) { $ports.Dispose() }
        if ($fixturePath) { & (Join-Path $repoRoot 'test-infra/Stop-SshFixture.ps1') -ManifestPath $fixturePath }
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; instanceOnly = [bool]$InstanceOnly; checks = $checks; humanReview = 'Pending M10 closing and reconnect presentation' } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8
    }
}
Get-Content -LiteralPath (Join-Path $outputPath 'result.json') -Raw
