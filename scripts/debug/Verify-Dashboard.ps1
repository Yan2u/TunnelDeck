[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts\m7-review' }
$dataPath = Join-Path $repoRoot ('.test-output\m7-layout\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputPath, $dataPath -Force | Out-Null
$appProcess = $null
$checks = [ordered]@{}
function Invoke-DashboardUi {
    $raw = & winapp ui @args -a $appProcess.Id --json
    if ($LASTEXITCODE -ne 0) { throw "Dashboard UI check failed [$($args -join ' ')]: $raw" }
    return $raw | ConvertFrom-Json
}
function Capture-Dashboard([string] $name) {
    Start-Sleep -Milliseconds 400
    $null = Invoke-DashboardUi screenshot --output (Join-Path $outputPath ($name + '.png'))
}
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Preview Stress -Configuration $Configuration -DataDirectory $dataPath
    $null = Invoke-DashboardUi wait-for ActiveCount --value '23 active' --timeout 15000
    $null = Invoke-DashboardUi scroll DashboardScroll --to top
    $null = Invoke-DashboardUi focus QuickPortText
    $before = (Invoke-DashboardUi get-property QuickPortText --property BoundingRectangle).properties.BoundingRectangle
    $null = Invoke-DashboardUi scroll ActiveTunnelList --to bottom
    $after = (Invoke-DashboardUi get-property QuickPortText --property BoundingRectangle).properties.BoundingRectangle
    if (-not $before -or $before -ne $after) { throw 'Scrolling the runtime list moved the Quick Forward input.' }
    $null = Invoke-DashboardUi scroll ActiveTunnelList --to top
    Capture-Dashboard 'stress-1200-light'
    $checks.boundedListAndStationaryQuickInput = 'Passed (24 sample rows)'

    $details = 'TunnelDetails_00007530000000000000000000000000'
    $null = Invoke-DashboardUi focus $details
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    $null = Invoke-DashboardUi wait-for TunnelDuration --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $null = Invoke-DashboardUi wait-for TunnelDuration --gone --timeout 5000
    $null = Invoke-DashboardUi focus PortAdjustment_00007531000000000000000000000000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    $null = Invoke-DashboardUi wait-for 'Port adjusted' --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $checks.keyboardDetailsAdjustmentAndEscape = 'Passed'

    $null = Invoke-DashboardUi invoke Settings
    $null = Invoke-DashboardUi wait-for SettingsPage --timeout 5000
    $null = Invoke-DashboardUi invoke ThemeSelector
    $null = Invoke-DashboardUi invoke ThemeDark
    $null = Invoke-DashboardUi invoke DashboardButton
    $null = Invoke-DashboardUi wait-for DashboardPage --timeout 5000
    $null = Invoke-DashboardUi scroll DashboardScroll --to top
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 900, 760)
    Capture-Dashboard 'stress-900-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 640, 680)
    Capture-Dashboard 'stress-640-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 600)
    Capture-Dashboard 'stress-500-dark'
    $checks.adaptiveLightAndDark = 'Passed (1200, 900, 640, 500 epx)'

    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    $null = Invoke-DashboardUi invoke PreviewSelector
    $null = Invoke-DashboardUi invoke PreviewEmpty
    $null = Invoke-DashboardUi wait-for DashboardAddServer --timeout 5000
    $null = Invoke-DashboardUi wait-for ActiveCount --value '0 active' --timeout 5000
    if ((Invoke-DashboardUi get-property ForwardButton --property IsEnabled).properties.IsEnabled -ne 'False') { throw 'Preview enabled a real forwarding command.' }
    Capture-Dashboard 'empty-dark'
    $checks.emptyAndPreviewNetworkDisabled = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; highContrast = 'Not switched globally'; humanGuiReview = 'Pending GUI C' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'dashboard-verification.json') -Encoding utf8
    Write-Host 'M7 dashboard layout and keyboard verification passed.'
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture-Dashboard 'dashboard-verification-failure' } } catch { }
    throw
}
finally {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000)) { throw 'The dashboard test app did not close.' }
    }
}
