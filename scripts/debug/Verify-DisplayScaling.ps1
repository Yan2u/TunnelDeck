[CmdletBinding()]
param([long] $SettingsWindow, [string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Display-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/display-scaling/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/display-scaling' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$scaleId = 'SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox'
$appProcess = $null
$checks = [Collections.Generic.List[object]]::new()
$passed = $false
$originalMonitors = @([TunnelDeck.Verification.DisplayNative]::Monitors())
if ($originalMonitors.Count -ne 1) { throw 'This scaling test requires a single display; select and verify individual monitors separately on multi-monitor systems.' }
$originalPercent = [int]($originalMonitors[0].Dpi * 100 / 96)
$restored = $false

function Settings-Ui {
    $raw = & winapp ui @args -w $SettingsWindow --json
    $result = $raw | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "Display Settings UI failed [$($args -join ' ')]: $raw" }
    return $result
}

function Open-DisplaySettings {
    Start-Process 'ms-settings:display'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $frame = Get-Process -Name ApplicationFrameHost, SystemSettings -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -in @('设置', 'Settings') } | Select-Object -First 1
        if ($frame) {
            $script:SettingsWindow = $frame.MainWindowHandle.ToInt64()
            $null = Settings-Ui wait-for $scaleId --timeout 10000
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The Windows display settings window did not open.'
}

function Set-DisplayPercent([int] $percent) {
    $current = @([TunnelDeck.Verification.DisplayNative]::Monitors())[0]
    if ($current.Dpi -eq $percent * 96 / 100) { return }
    Open-DisplaySettings
    $combo = (Settings-Ui search $scaleId).matches | Where-Object automationId -eq $scaleId | Select-Object -First 1
    if (-not $combo) { throw 'Open Windows Settings > System > Display before running this test.' }
    if ($combo.isOffscreen) { $null = Settings-Ui scroll-into-view $scaleId }
    if ($combo.expandState -ne 'expanded') { $null = Settings-Ui invoke $scaleId }
    $items = @( (Settings-Ui search '%').matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -match "^$percent%(?:\s|$)" } )
    if ($items.Count -ne 1) { throw "The $percent% display scaling option is not uniquely available." }
    $null = Settings-Ui invoke $items[0].selector
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        if (@([TunnelDeck.Verification.DisplayNative]::Monitors())[0].Dpi -eq $percent * 96 / 100) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Windows did not apply $percent% display scaling."
}

function Open-TestNavigation {
    $dashboard = (Transfer-Ui search DashboardButton).matches | Where-Object automationId -eq 'DashboardButton' | Select-Object -First 1
    if (-not $dashboard -or $dashboard.isOffscreen) { Transfer-Invoke PART_PaneToggleButton }
    $null = Transfer-Ui wait-for DashboardButton --property IsOffscreen --value False --timeout 5000
}

$sshPath = Join-Path $runPath 'display.config'
@'
Host display-test
 HostName display.example
 User developer
 Compression yes
Host missing-user
 HostName missing.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM

try {
    # The same process crosses each system DPI change, testing WM_DPICHANGED rather than only fresh launches.
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Preview Sample -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    foreach ($percent in @(100, 150, 200)) {
        Write-Host "Checking actual Windows display scaling at $percent%."
        Set-DisplayPercent $percent
        $monitor = @([TunnelDeck.Verification.DisplayNative]::Monitors())[0]
        [TunnelDeck.Verification.DisplayNative]::MoveToMonitor($appProcess.MainWindowHandle, $monitor, 900, 650)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while ([TunnelDeck.Verification.DisplayNative]::GetDpiForWindow($appProcess.MainWindowHandle) -ne $monitor.Dpi) {
            if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The application did not adopt the new DPI.' }
            Start-Sleep -Milliseconds 50
        }
        if (-not [TunnelDeck.Verification.DisplayNative]::IsPerMonitorV2($appProcess.MainWindowHandle)) { throw 'The application window is not PerMonitorV2 aware.' }
        Open-TestNavigation
        Transfer-Invoke DashboardButton
        Transfer-Wait DashboardPage
        $null = Transfer-Visible QuickServer
        $null = Transfer-Visible ForwardButton
        Transfer-Capture "dashboard-$percent"
        Open-TestNavigation
        Transfer-Invoke AllServers
        Transfer-Wait ServersPage
        $null = Transfer-Visible AddServer
        Transfer-Capture "servers-$percent"
        Open-TestNavigation
        Transfer-Invoke AllProfiles
        Transfer-Wait ProfilesPage
        $null = Transfer-Visible AddProfile
        Open-TestNavigation
        Transfer-Invoke Settings
        Transfer-Wait SettingsPage
        $null = Transfer-Ui scroll SettingsScroll --to bottom
        $null = Transfer-Visible DefaultAuthentication
        Open-TestNavigation
        Transfer-Preview $sshPath -Ssh
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 580)
        $null = Transfer-Ui scroll ImportPreview --to bottom
        $null = Transfer-Visible CommitImport
        $null = Transfer-Visible CancelImport
        $null = Transfer-Visible SkipUnavailableImports
        Transfer-Capture "import-500-$percent"
        Transfer-Invoke CancelImport
        $checks.Add([ordered]@{ percent = $percent; dpi = $monitor.Dpi; perMonitorV2 = $true; dashboard = 'Passed'; resources = 'Passed'; settings = 'Passed'; narrowImport = 'Passed' })
    }
    $passed = $true
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'display-scaling-failure' } } catch { }
    throw
}
finally {
    try {
        Set-DisplayPercent $originalPercent
        $restored = @([TunnelDeck.Verification.DisplayNative]::Monitors())[0].Dpi -eq $originalMonitors[0].Dpi
        if (-not $restored) { throw 'The original display scaling was not restored.' }
    }
    finally {
        try { Transfer-Close }
        finally {
            [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; checks = $checks; originalPercent = $originalPercent; originalScaleRestored = $restored; monitorCount = $originalMonitors.Count; dataPath = $dataPath } |
                ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
        }
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
