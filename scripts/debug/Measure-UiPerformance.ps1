[CmdletBinding()]
param([string] $OutputDirectory, [string] $ExecutablePath)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Scale-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sample = New-ScaleTestData (Join-Path $repoRoot ('.test-output/ui-performance/' + [guid]::NewGuid().ToString('N')))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/ui-performance' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$reportPath = Join-Path $outputPath 'handlers.json'
$appProcess = $null
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $sample.dataPath -ExecutablePath $ExecutablePath -PerformancePath $reportPath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for QuickServer --property IsEnabled --value True --timeout 15000
    for ($iteration = 0; $iteration -lt 24; $iteration++) {
        $kind = if ($iteration % 2 -eq 0) { 'Server' } else { 'Profile' }
        Transfer-Invoke "All${kind}s"
        Transfer-Wait "${kind}sPage"
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x4b, 0x11)
        $null = Transfer-Ui focus TextBox
        $null = Transfer-Ui set-value TextBox 'scale'
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x23)
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x08)
        Transfer-Wait ('SearchProfile_' + ([guid]$sample.profiles[0].id).ToString('N'))
        $null = Transfer-Ui set-value TextBox ''
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    }
    Transfer-Close
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if ($report.operations.Navigation.count -lt 20 -or $report.operations.Search.count -lt 20) { throw 'Not enough UI handler samples were captured.' }
    if ($report.operations.Navigation.p95Ms -ge 100 -or $report.operations.Search.p95Ms -ge 100) { throw 'UI handlers exceeded the 100 ms P95 target.' }
    Get-Content -Raw -LiteralPath $reportPath
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) {
        Transfer-Capture 'ui-performance-failure'
        Transfer-Ui search Search --max 30 | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'search-failure.json') -Encoding utf8NoBOM
    } } catch { }
    throw
}
finally { Transfer-Close }
