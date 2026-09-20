[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $repoRoot 'artifacts\window-verification'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) { throw 'winapp ui is required for window verification.' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification
{
    public static class WindowNative
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPosNative(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        public static bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { return SetWindowPosNative(window, after, x, y, width, height, flags); }
            finally { SetThreadDpiAwarenessContext(previous); }
        }

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
'@

$watch = [Diagnostics.Stopwatch]::StartNew()
$appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -WindowProbe -Configuration $Configuration
$watch.Stop()
$windowHandle = $appProcess.MainWindowHandle

function Invoke-WindowUi {
    $result = & winapp ui @args -w $windowHandle --json
    $commandExit = $LASTEXITCODE
    $parsed = $result | ConvertFrom-Json
    $expectedEmptySearch = $args[0] -eq 'search' -and $parsed.PSObject.Properties['matchCount'] -and $parsed.matchCount -eq 0
    if ($commandExit -ne 0 -and -not $expectedEmptySearch) { throw "Window verification failed [$($args -join ' ')]: $result" }
    return $parsed
}

function Get-ElementProperty([string] $selector, [string] $property) {
    $result = Invoke-WindowUi get-property $selector --property $property
    return $result.properties.$property
}

function Ensure-SidebarOpen {
    $dashboard = Invoke-WindowUi search DashboardButton
    if ($dashboard.matchCount -eq 0 -or (Get-ElementProperty 'DashboardButton' 'IsOffscreen') -ne 'False') {
        $null = Invoke-WindowUi invoke PART_PaneToggleButton
    }
    $null = Invoke-WindowUi wait-for DashboardButton --property IsOffscreen --value False --timeout 5000
    Start-Sleep -Milliseconds 400
}

try {
    $null = Invoke-WindowUi wait-for StorageNotice --gone --timeout 5000
    Ensure-SidebarOpen
    $dashboardBefore = Get-ElementProperty 'DashboardButton' 'BoundingRectangle'
    $settingsBefore = Get-ElementProperty 'Settings' 'BoundingRectangle'
    $runtimeBefore = Get-ElementProperty 'RuntimeStatus' 'BoundingRectangle'
    $null = Invoke-WindowUi scroll MenuItemsScrollViewer --to bottom
    if ($dashboardBefore -ne (Get-ElementProperty 'DashboardButton' 'BoundingRectangle') -or
        $settingsBefore -ne (Get-ElementProperty 'Settings' 'BoundingRectangle') -or
        $runtimeBefore -ne (Get-ElementProperty 'RuntimeStatus' 'BoundingRectangle')) {
        throw 'A fixed sidebar region moved when the resource list scrolled.'
    }
    if ((Get-ElementProperty 'AllServers' 'IsOffscreen') -ne 'False') { throw 'All servers is not reachable by scrolling.' }
    $null = Invoke-WindowUi screenshot --output (Join-Path $outputPath 'wide.png')
    $null = Invoke-WindowUi set-value TextBox 'probe'
    $search = & winapp ui get-value TextBox -w $windowHandle
    if ($LASTEXITCODE -ne 0 -or $search.Trim() -ne 'probe') { throw 'Title bar search did not accept text.' }
    $null = Invoke-WindowUi set-value TextBox ''
    $null = Invoke-WindowUi invoke PART_PaneToggleButton
    $null = Invoke-WindowUi wait-for DashboardButton --gone --timeout 5000
    $collapsed = Invoke-WindowUi search DashboardButton
    if ($collapsed.matchCount -ne 0) { throw 'Pane toggle did not collapse the header.' }
    $null = Invoke-WindowUi invoke PART_PaneToggleButton
    $null = Invoke-WindowUi wait-for DashboardButton --property IsOffscreen --value False --timeout 5000

    if (-not [TunnelDeck.Verification.WindowNative]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, 900, 500, 6)) {
        throw 'Could not resize the probe window.'
    }
    Start-Sleep -Milliseconds 400
    Ensure-SidebarOpen
    $shortDashboard = Get-ElementProperty 'DashboardButton' 'BoundingRectangle'
    $shortSettings = Get-ElementProperty 'Settings' 'BoundingRectangle'
    $null = Invoke-WindowUi scroll MenuItemsScrollViewer --to bottom
    if ($shortDashboard -ne (Get-ElementProperty 'DashboardButton' 'BoundingRectangle') -or
        $shortSettings -ne (Get-ElementProperty 'Settings' 'BoundingRectangle')) {
        $null = Invoke-WindowUi screenshot --output (Join-Path $outputPath 'short-failure.png')
        throw "Fixed sidebar regions moved in the short window. Before: $shortDashboard / $shortSettings. After: $(Get-ElementProperty 'DashboardButton' 'BoundingRectangle') / $(Get-ElementProperty 'Settings' 'BoundingRectangle')."
    }
    $null = Invoke-WindowUi screenshot --output (Join-Path $outputPath 'short.png')
    $windowTitle = $appProcess.MainWindowTitle
    $closing = [Diagnostics.Stopwatch]::StartNew()
    $null = [TunnelDeck.Verification.WindowNative]::PostMessage($windowHandle, 16, [IntPtr]::Zero, [IntPtr]::Zero)
    $null = [TunnelDeck.Verification.WindowNative]::PostMessage($windowHandle, 16, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    $appProcess.Refresh()
    $remainedResponsiveDuringCleanup = -not $appProcess.HasExited -and $appProcess.Responding
    if (-not $appProcess.WaitForExit(5000)) { throw 'The probe did not finish asynchronous shutdown.' }
    $closing.Stop()
    if (-not $remainedResponsiveDuringCleanup -or $closing.ElapsedMilliseconds -lt 650 -or $appProcess.ExitCode -ne 0) {
        throw 'The probe closed prematurely, blocked the UI, or crashed during cleanup.'
    }
    [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow
        title = $windowTitle
        startupToWindowMs = $watch.ElapsedMilliseconds
        fixedDashboard = $dashboardBefore
        fixedSettings = $settingsBefore
        fixedRuntime = $runtimeBefore
        resourceScroll = 'Passed'
        titleBarSearch = 'Passed'
        paneToggle = 'Passed'
        shortWindow = 'Passed (900 x 500 physical pixels)'
        repeatedClose = 'Passed'
        responsiveDuringCleanup = $remainedResponsiveDuringCleanup
        shutdownMs = $closing.ElapsedMilliseconds
        exitCode = $appProcess.ExitCode
        humanGuiReview = 'Pending'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'result.json')
    Get-Content -LiteralPath (Join-Path $outputPath 'result.json') -Raw
}
finally {
    $appProcess.Refresh()
    if (-not $appProcess.HasExited) { $null = $appProcess.CloseMainWindow() }
}
