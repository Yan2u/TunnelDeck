[CmdletBinding()]
param([string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Accessibility-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output/text-scaling/' + [guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/text-scaling' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$registryPath = 'HKCU:\Software\Microsoft\Accessibility'
$registryBefore = Get-ItemProperty -LiteralPath $registryPath
$hadValue = $null -ne $registryBefore.PSObject.Properties['TextScaleFactor']
$originalPercent = if ($hadValue) { [int]$registryBefore.TextScaleFactor } else { 100 }
$appProcess = $null
$passed = $false
$restored = $false
$restoredHeight = $null
$textSettingsWindow = 0
$textSettingsInputWindow = [IntPtr]::Zero
$sliderId = 'SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_Slider'
$applyId = 'SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_ButtonRemove'
$changes = [Collections.Generic.List[object]]::new()
function TextSettings-Ui {
    $raw = & winapp ui @args -w $textSettingsWindow --json
    $result = $raw | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "Text-size Settings UI failed: $raw" }
    return $result
}
function Activate-TextSettings {
    # UWP Settings can expose its UI through ApplicationFrameHost while its CoreWindow owns
    # foreground input. Accept only the verified frame or the actual Settings process.
    try { [TunnelDeck.Verification.ForwardNative]::Activate([IntPtr]$textSettingsWindow) } catch { }
    $foreground = [TunnelDeck.Verification.AccessibilityNative]::GetForegroundWindow()
    $settingsProcesses = @(Get-Process -Name SystemSettings -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    if ($foreground -ne [IntPtr]$textSettingsWindow -and [TunnelDeck.Verification.AccessibilityNative]::ForegroundProcess() -notin $settingsProcesses) {
        throw 'Keyboard input requires the verified Windows Settings window in the foreground.'
    }
    $script:textSettingsInputWindow = $foreground
}
function Open-TextSettings {
    Start-Process 'ms-settings:easeofaccess-display'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $frame = Get-Process -Name ApplicationFrameHost, SystemSettings -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -in @('设置', 'Settings') } | Select-Object -First 1
        if ($frame) {
            $script:textSettingsWindow = $frame.MainWindowHandle.ToInt64()
            [TunnelDeck.Verification.AccessibilityNative]::RestoreIfMinimized($frame.MainWindowHandle)
            Activate-TextSettings
            $null = TextSettings-Ui wait-for $sliderId --timeout 10000
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The Windows text-size settings window did not open.'
}
function Set-TextPercent([int] $percent) {
    Open-TextSettings
    $before = (TextSettings-Ui get-value $sliderId).text
    if ($percent -lt 100 -or $percent -gt 225) { throw 'This Windows text-size slider supports 100–225 percent.' }
    $null = TextSettings-Ui focus $sliderId
    Activate-TextSettings
    [TunnelDeck.Verification.ForwardNative]::Press($textSettingsInputWindow, 0x24)
    for ($step = 100; $step -lt $percent; $step++) { [TunnelDeck.Verification.ForwardNative]::Press($textSettingsInputWindow, 0x27) }
    $null = TextSettings-Ui wait-for $sliderId --value "$percent%" --timeout 5000
    if ($before -ne "$percent%") {
        $null = TextSettings-Ui wait-for $applyId --property IsEnabled --value True --timeout 5000
        $null = TextSettings-Ui focus $applyId
        Activate-TextSettings
        [TunnelDeck.Verification.ForwardNative]::Press($textSettingsInputWindow, 0x20)
        $null = TextSettings-Ui wait-for $applyId --property IsEnabled --value False --timeout 15000
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $current = Get-ItemProperty -LiteralPath $registryPath
        $value = if ($null -ne $current.PSObject.Properties['TextScaleFactor']) { [int]$current.TextScaleFactor } else { 100 }
        if ($value -eq $percent) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    # The live application measurement below is the acceptance check. Keep the registry
    # observation as evidence instead of treating this implementation detail as the system API.
    $changes.Add([ordered]@{ requestedPercent = $percent; input = 'Native Home / ArrowRight, then Space on Apply'; before = $before; observedRegistryPercent = $value })
}
function Open-TestNavigation {
    $dashboard = (Transfer-Ui search DashboardButton).matches | Where-Object automationId -eq 'DashboardButton' | Select-Object -First 1
    if (-not $dashboard -or $dashboard.isOffscreen) { Transfer-Invoke PART_PaneToggleButton }
    $null = Transfer-Ui wait-for DashboardButton --property IsOffscreen --value False --timeout 5000
}
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Preview Stress -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    $beforeHeight = (Transfer-Visible DashboardPage).height
    Set-TextPercent 200
    [TunnelDeck.Verification.ForwardNative]::Activate($appProcess.MainWindowHandle)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $afterHeight = (Transfer-Visible DashboardPage).height
        # WinUI uses nonlinear text scaling: large headings grow less than body text.
        if ($afterHeight -gt $beforeHeight * 1.1 -or $originalPercent -ge 150) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($originalPercent -lt 150 -and $afterHeight -le $beforeHeight * 1.1) { throw 'The application text did not respond to the system text-size change.' }
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 900, 650)
    $null = Transfer-Visible QuickServer
    $null = Transfer-Visible ForwardButton
    Transfer-Capture 'dashboard-text-200'
    Open-TestNavigation
    Transfer-Invoke AllServers
    Transfer-Wait ServersPage
    $null = Transfer-Visible AddServer
    Transfer-Capture 'servers-text-200'
    Transfer-Invoke AddServer
    Transfer-Wait ServerDetailPage
    $null = Transfer-Visible SaveServer
    Transfer-Capture 'server-detail-text-200'
    Transfer-Invoke CancelServer
    Open-TestNavigation
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 650)
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $null = Transfer-Visible DefaultAuthentication
    Transfer-Capture 'settings-500-text-200'
    $passed = $true
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'text-scaling-failure' } } catch { }
    throw
}
finally {
    try {
        Set-TextPercent $originalPercent
        if (-not $hadValue) {
            $current = Get-ItemProperty -LiteralPath $registryPath
            if ($null -ne $current.PSObject.Properties['TextScaleFactor'] -and $current.TextScaleFactor -eq $originalPercent) {
                Remove-ItemProperty -LiteralPath $registryPath -Name TextScaleFactor
            }
        }
        $current = Get-ItemProperty -LiteralPath $registryPath
        $restored = if ($hadValue) { $current.TextScaleFactor -eq $originalPercent } else { $null -eq $current.PSObject.Properties['TextScaleFactor'] }
        if (-not $restored) { throw 'The original text-size setting was not restored.' }
        if ($appProcess -and -not $appProcess.HasExited) {
            [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
            Open-TestNavigation
            Transfer-Invoke DashboardButton
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
            do {
                $restoredHeight = (Transfer-Visible DashboardPage).height
                if ([Math]::Abs($restoredHeight - $beforeHeight) -le 2) { break }
                Start-Sleep -Milliseconds 100
            } while ([DateTimeOffset]::UtcNow -lt $deadline)
            if ([Math]::Abs($restoredHeight - $beforeHeight) -gt 2) { $restored = $false; throw 'The live application text did not return to its original size.' }
        }
    }
    finally {
        try { Transfer-Close }
        finally {
            [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; originalPercent = $originalPercent; originalValueExisted = $hadValue; originalTextSettingRestored = $restored; beforeHeadingHeight = $beforeHeight; enlargedHeadingHeight = $afterHeight; restoredHeadingHeight = $restoredHeight; nativeChanges = $changes; dataPath = $dataPath } |
                ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
        }
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
