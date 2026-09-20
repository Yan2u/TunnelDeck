[CmdletBinding()]
param(
    [switch] $WindowProbe,
    [string] $DataDirectory,
    [ValidateSet('Sample', 'Stress', 'Empty')][string] $Preview,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $repoRoot "TunnelDeck\bin\x64\$Configuration\net10.0-windows10.0.19041.0\TunnelDeck.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build first with scripts/Build.ps1.' }
$startArguments = @{ FilePath = $executable; PassThru = $true }
$appArguments = @()
if ($WindowProbe) { $appArguments += '--window-probe' }
if ($Preview) { $appArguments += '--preview=' + $Preview.ToLowerInvariant() }
if ($DataDirectory) {
    if (-not [IO.Path]::IsPathFullyQualified($DataDirectory) -or $DataDirectory.Contains('"')) { throw 'Use an absolute data directory without quotation marks.' }
    $appArguments += '"--data-directory=' + $DataDirectory + '"'
}
if ($appArguments.Count -gt 0) { $startArguments.ArgumentList = $appArguments }
$appProcess = Start-Process @startArguments
$deadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    Start-Sleep -Milliseconds 100
    $appProcess.Refresh()
} while (-not $appProcess.HasExited -and $appProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
if ($appProcess.HasExited -or $appProcess.MainWindowHandle -eq 0 -or -not $appProcess.Responding) {
    throw 'TunnelDeck did not expose a responsive top-level window within 10 seconds.'
}
Write-Output $appProcess
