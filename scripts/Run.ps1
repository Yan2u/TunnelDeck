[CmdletBinding()]
param(
    [switch] $WindowProbe,
    [string] $DataDirectory,
    [string] $ExecutablePath,
    [string] $PerformancePath,
    [ValidateSet('Sample', 'Stress', 'Empty')][string] $Preview,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$executable = if ($ExecutablePath) { [IO.Path]::GetFullPath($ExecutablePath) } else { Join-Path $repoRoot "src\TunnelDeck\bin\x64\$Configuration\net10.0-windows10.0.19041.0\TunnelDeck.exe" }
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build first with scripts/Build.ps1.' }
$startArguments = @{ FilePath = $executable; PassThru = $true }
if ($PerformancePath) {
    if (-not $DataDirectory -or -not [IO.Path]::IsPathFullyQualified($PerformancePath)) { throw 'UI timings require an explicit data directory and an absolute report path.' }
    $startArguments.Environment = @{ TUNNELDECK_UI_TIMINGS = $PerformancePath }
}
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
