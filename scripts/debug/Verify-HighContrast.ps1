[CmdletBinding()]
param([string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Accessibility-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output/high-contrast/' + [guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/high-contrast' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$original = [TunnelDeck.Verification.AccessibilityNative]::ReadContrast()
$appProcess = $null
$passed = $false
$restored = $false
$schemeNormalized = $false
$sshPath = Join-Path $dataPath 'contrast.config'
@'
Host contrast-ready
 HostName contrast.example
 User developer
 Compression yes
Host missing-user
 HostName unavailable.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
function Open-TestNavigation {
    $dashboard = (Transfer-Ui search DashboardButton).matches | Where-Object automationId -eq 'DashboardButton' | Select-Object -First 1
    if (-not $dashboard -or $dashboard.isOffscreen) { Transfer-Invoke PART_PaneToggleButton }
    $null = Transfer-Ui wait-for DashboardButton --property IsOffscreen --value False --timeout 5000
}
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Preview Stress -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    [TunnelDeck.Verification.AccessibilityNative]::SetContrast($original, $true)
    if (-not [TunnelDeck.Verification.AccessibilityNative]::ReadContrast().Enabled) { throw 'Windows did not enable high contrast.' }
    $null = Transfer-Visible QuickServer
    $null = Transfer-Visible ForwardButton
    Transfer-Capture 'dashboard-high-contrast'
    Open-TestNavigation
    Transfer-Invoke AllServers
    Transfer-Wait ServersPage
    $null = Transfer-Visible AddServer
    Transfer-Capture 'servers-high-contrast'
    Open-TestNavigation
    Transfer-Invoke AllProfiles
    Transfer-Wait ProfilesPage
    $null = Transfer-Visible AddProfile
    Open-TestNavigation
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    Transfer-Invoke ThemeSelector
    Transfer-Invoke ThemeDark
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 650)
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $null = Transfer-Visible DefaultAuthentication
    Transfer-Capture 'settings-500-high-contrast'
    if (-not [TunnelDeck.Verification.AccessibilityNative]::ReadContrast().Enabled) { throw 'Window theme selection disabled system high contrast.' }
    Open-TestNavigation
    Transfer-Preview $sshPath -Ssh
    $null = Transfer-Ui scroll ImportPreview --to bottom
    $null = Transfer-Visible CommitImport
    $null = Transfer-Visible CancelImport
    $null = Transfer-Visible SkipUnavailableImports
    Transfer-Capture 'import-500-high-contrast'
    $passed = $true
}
finally {
    try {
        [TunnelDeck.Verification.AccessibilityNative]::SetContrast($original, $original.Enabled)
        $current = [TunnelDeck.Verification.AccessibilityNative]::ReadContrast()
        $schemeNormalized = -not $original.Enabled -and [string]::IsNullOrEmpty($original.Scheme) -and -not $current.Enabled -and -not [string]::IsNullOrEmpty($current.Scheme)
        $restored = $current.Flags -eq $original.Flags -and ($current.Scheme -eq $original.Scheme -or $schemeNormalized)
        if (-not $restored) { throw 'The original contrast settings were not restored.' }
    }
    finally {
        try { Transfer-Close }
        finally {
            [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; original = $original; restoredState = $current; originalContrastFlagsRestored = $restored; emptyInactiveSchemeInitializedByWindows = $schemeNormalized; dataPath = $dataPath } |
                ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
        }
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
