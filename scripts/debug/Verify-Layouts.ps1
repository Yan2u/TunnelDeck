[CmdletBinding()]
param([string] $ExecutablePath, [string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/layouts/' + [guid]::NewGuid().ToString('N'))
$sample = New-ActivityReviewData $runPath
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/layouts' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$keyPath = Join-Path $runPath 'example private key with a long path'
Set-Content -LiteralPath $keyPath -Value 'Layout fixture; not used to authenticate.' -Encoding utf8NoBOM
$state = Get-Content -LiteralPath $sample.statePath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
$state.schemaVersion = 2
$state.settings.defaultAuthenticationMethod = 1
$state.settings.defaultIdentityFilePath = $keyPath
$state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $sample.statePath -Encoding utf8NoBOM
if (-not ('TunnelDeck.Verification.LayoutNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification {
    public static class LayoutNative {
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    }
}
'@
}
$appProcess = $null
$passed = $false
$checks = [ordered]@{}
$settingsCases = [Collections.Generic.List[object]]::new()

function Close-NavigationPane {
    $badge = (Transfer-Ui search CompactRuntimeStatus).matches | Where-Object { $_.automationId -eq 'CompactRuntimeStatus' -and -not $_.isOffscreen -and $_.width -gt 0 }
    if (-not $badge) { Transfer-Invoke PART_PaneToggleButton }
}

try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $sample.dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for QuickPortText --property HasKeyboardFocus --value True --timeout 5000
    $null = Transfer-Ui set-value QuickPortText '3019'
    Transfer-Invoke QuickAdvanced
    Transfer-Wait QuickLocalPortText
    $null = Transfer-Ui set-value QuickLocalPortText '3031'
    $null = Transfer-Ui set-value QuickRemotePortText '3032'
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    $scale = [TunnelDeck.Verification.LayoutNative]::GetDpiForWindow($appProcess.MainWindowHandle) / 96.0
    foreach ($width in @(1198,1200,1360,1200)) {
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, $width, 800)
        Close-NavigationPane
        $null = Transfer-Ui scroll SettingsScroll --to top
        Start-Sleep -Milliseconds 500
        $viewport = Transfer-Visible SettingsScroll
        $heading = Transfer-Visible SettingsPage
        $expected = 24 * $scale + [Math]::Max(0, ($viewport.width - 1120 * $scale) / 2)
        $actual = $heading.x - $viewport.x
        # Position detects the old incorrect centering even when UIA clips the right edge.
        if ([Math]::Abs($actual - $expected) -gt 12 * $scale) { throw "Settings shifted sideways at $width DIP." }
        $settingsCases.Add([ordered]@{ windowWidthDip = $width; expectedHeadingInset = $expected; actualHeadingInset = $actual; sidebarCollapsed = $true })
    }
    Transfer-Capture 'settings-collapsed'
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $null = Transfer-Visible DefaultKeyPath
    $null = Transfer-Visible BrowseDefaultKey
    Transfer-Capture 'settings-key'
    $checks.settingsWidthThreshold = $true

    Transfer-Invoke DashboardButton
    Transfer-Wait QuickLocalPortText
    if ((Transfer-Value QuickLocalPortText) -ne '3031' -or (Transfer-Value QuickRemotePortText) -ne '3032') { throw 'Recreating Advanced controls lost the draft during navigation.' }
    Transfer-Invoke QuickAdvanced
    $null = Transfer-Ui wait-for QuickLocalPortText --gone --timeout 5000
    if ((Transfer-Value QuickPortText) -ne '3019') { throw 'Closing Advanced changed the basic port draft.' }
    Transfer-Invoke QuickAdvanced
    if ((Transfer-Value QuickLocalPortText) -ne '3031') { throw 'Loading Advanced a second time lost its draft.' }
    Transfer-Invoke QuickAdvanced
    $checks.lazyAdvancedDraft = $true

    Transfer-Invoke ForwardModeSelector
    Transfer-Invoke QuickRemoteMode
    $null = Transfer-Ui wait-for AutoFindLocalPort --gone --timeout 5000
    Transfer-Invoke ForwardModeSelector
    Transfer-Invoke QuickLocalMode
    Transfer-Wait AutoFindLocalPort
    $checks.forwardingMode = $true
    $previousX = -1
    foreach ($selector in @('QuickServer','QuickPin','ForwardModeSelector','QuickPortText','ForwardButton')) {
        $element = Transfer-Visible $selector
        if ($element.x -le $previousX) { throw 'The wide Quick Forward control order is wrong.' }
        $previousX = $element.x
    }
    $null = Transfer-Visible OpenQuickTerminal
    if (-not (Transfer-Enabled OpenQuickTerminal)) { throw 'The direct terminal command is unavailable.' }
    Transfer-Capture 'dashboard-wide'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 800)
    Start-Sleep -Milliseconds 300
    foreach ($selector in @('QuickServer','QuickPin','ForwardModeSelector','QuickPortText','ForwardButton','QuickAdvanced','OpenQuickTerminal')) { $null = Transfer-Visible $selector }
    Transfer-Capture 'dashboard-narrow'
    $checks.quickForwardLayout = $true
    $passed = $true
}
finally {
    [ordered]@{ passed = $passed; checks = $checks; settingsCases = $settingsCases; dataPath = $sample.dataPath; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Transfer-Close
}
Get-Content -LiteralPath (Join-Path $outputPath 'result.json')
