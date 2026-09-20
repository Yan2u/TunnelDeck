[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$fixturePath = Join-Path $repoRoot ('.test-output\m4-shell\' + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $repoRoot 'artifacts\m4-review'
New-Item -ItemType Directory -Path $fixturePath, $outputPath -Force | Out-Null
$statePath = Join-Path $fixturePath 'state.json'
if (-not ('TunnelDeck.Verification.ShellNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification
{
    public static class ShellNative
    {
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public ushort Key;
            [FieldOffset(12)] public uint Flags;
        }
        public static void Resize(IntPtr window, int widthEpx, int heightEpx)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                double scale = GetDpiForWindow(window) / 96.0;
                if (!SetWindowPos(window, IntPtr.Zero, 0, 0, (int)(widthEpx * scale), (int)(heightEpx * scale), 6)) { throw new InvalidOperationException("The test window could not be resized."); }
            }
            finally { SetThreadDpiAwarenessContext(previous); }
        }
        public static void Press(IntPtr window, ushort key) { Press(window, key, 0); }
        public static void Press(IntPtr window, ushort key, ushort modifier)
        {
            GetWindowThreadProcessId(window, out uint targetProcess);
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
            if (foregroundProcess != targetProcess)
            {
                SetForegroundWindow(window);
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
                    if (foregroundProcess == targetProcess) { break; }
                    System.Threading.Thread.Sleep(10);
                }
            }
            // Owned WinUI flyouts have a different HWND, but must belong to this same app process.
            if (targetProcess == 0 || foregroundProcess != targetProcess) { throw new InvalidOperationException("Keyboard verification requires the target app in the foreground."); }
            Input Down(ushort value) => new Input { Type = 1, Key = value };
            Input Up(ushort value) => new Input { Type = 1, Key = value, Flags = 2 };
            Input[] inputs = modifier == 0 ? new[] { Down(key), Up(key) } : new[] { Down(modifier), Down(key), Up(key), Up(modifier) };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) { throw new InvalidOperationException("Keyboard input could not be delivered."); }
        }
    }
}
'@
}

$appProcess = $null
$checks = [ordered]@{}
function Invoke-ShellUi {
    $result = & winapp ui @args -w $appProcess.MainWindowHandle --json
    $commandExit = $LASTEXITCODE
    $parsed = $result | ConvertFrom-Json
    $emptySearch = $args[0] -eq 'search' -and $parsed.PSObject.Properties['matchCount'] -and $parsed.matchCount -eq 0
    if ($commandExit -ne 0 -and -not $emptySearch) { throw "Shell verification failed [$($args -join ' ')]: $result" }
    return $parsed
}
function Wait-Page([string] $selector) {
    $result = Invoke-ShellUi wait-for $selector --timeout 5000
    if (-not $result.found) { throw "The expected page did not appear: $selector" }
}
function Read-State {
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }
    $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream)
    try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $state = Read-State
        if ($state -and (& $condition $state)) { return $state }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected configuration change was not persisted.'
}
function Capture-Shell([string] $name) {
    Start-Sleep -Milliseconds 250
    $null = Invoke-ShellUi screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function Resize-Shell([int] $width, [int] $height) {
    [TunnelDeck.Verification.ShellNative]::Resize($appProcess.MainWindowHandle, $width, $height)
    Start-Sleep -Milliseconds 400
}
function Select-Preview([string] $name) {
    $null = Invoke-ShellUi invoke PreviewSelector
    $selector = switch ($name) { 'Long lists' { 'PreviewStress' }; 'Empty' { 'PreviewEmpty' }; default { 'PreviewSample' } }
    $null = Invoke-ShellUi invoke $selector
}
function Search-For([string] $text) {
    [TunnelDeck.Verification.ShellNative]::Press($appProcess.MainWindowHandle, 0x4b, 0x11)
    $null = Invoke-ShellUi focus TextBox
    $null = Invoke-ShellUi set-value TextBox ''
    $null = Invoke-ShellUi set-value TextBox $text
}
function Close-Shell {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(5000)) { throw 'The shell did not finish shutdown.' }
        if ($appProcess.ExitCode -ne 0) { throw 'The shell exited with an error.' }
    }
}

try {
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -Preview Sample -DataDirectory $fixturePath -Configuration $Configuration
    Wait-Page DashboardPage
    $null = Wait-State { param($state) $state.servers.Count -eq 6 }
    Capture-Shell 'dashboard-light'
    $checks.dashboard = 'Passed'

    $null = Invoke-ShellUi invoke AllServers
    Wait-Page ServersPage
    Capture-Shell 'servers-light'
    $null = Invoke-ShellUi invoke ServerRow_00000001000000000000000000000000
    Wait-Page ServerDetailPage
    $null = Invoke-ShellUi set-value ServerDisplayName 'Shell verification server'
    $null = Invoke-ShellUi invoke SaveServer
    $null = Invoke-ShellUi invoke Settings
    Wait-Page SettingsPage
    $null = Wait-State { param($state) $state.servers[0].displayName -eq 'Shell verification server' }
    Wait-Page SettingsPage
    $checks.saveDoesNotStealNavigation = 'Passed'

    $null = Invoke-ShellUi invoke ThemeSelector
    $null = Invoke-ShellUi invoke ThemeDark
    Capture-Shell 'settings-dark'
    $null = Invoke-ShellUi invoke AutoFindDefault
    $null = Wait-State { param($state) -not $state.settings.autoFindAvailablePortDefault }
    $null = Invoke-ShellUi invoke DashboardButton
    Wait-Page DashboardPage
    $auto = Invoke-ShellUi get-property AutoFindLocalPort --property ToggleState
    if ($auto.properties.ToggleState -ne 'On') { throw 'Settings changed an existing Quick Forward draft.' }
    $null = Invoke-ShellUi invoke QuickRemoteMode
    if ((Invoke-ShellUi search AutoFindLocalPort).matchCount -ne 0) { throw 'Remote mode exposed automatic port search.' }
    $null = Invoke-ShellUi invoke QuickLocalMode
    Capture-Shell 'dashboard-dark'
    $checks.localPortOption = 'Passed'

    Search-For 'staging-eu'
    [TunnelDeck.Verification.ShellNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Wait-Page ServerDetailPage
    Search-For 'staging-eu'
    [TunnelDeck.Verification.ShellNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Invoke-ShellUi invoke PART_BackButton
    Wait-Page DashboardPage
    Search-For 'no-such-resource'
    $null = Invoke-ShellUi wait-for SearchNoResults --timeout 3000
    [TunnelDeck.Verification.ShellNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $null = Invoke-ShellUi set-value TextBox ''
    $checks.searchAndBack = 'Passed'

    Select-Preview 'Long lists'
    $null = Wait-State { param($state) $state.profiles.Count -eq 28 }
    $fixedDashboard = (Invoke-ShellUi get-property DashboardButton --property BoundingRectangle).properties.BoundingRectangle
    $fixedSettings = (Invoke-ShellUi get-property Settings --property BoundingRectangle).properties.BoundingRectangle
    $fixedRuntime = (Invoke-ShellUi get-property RuntimeStatus --property BoundingRectangle).properties.BoundingRectangle
    $null = Invoke-ShellUi scroll MenuItemsScrollViewer --to bottom
    if ($fixedDashboard -ne (Invoke-ShellUi get-property DashboardButton --property BoundingRectangle).properties.BoundingRectangle -or
        $fixedSettings -ne (Invoke-ShellUi get-property Settings --property BoundingRectangle).properties.BoundingRectangle -or
        $fixedRuntime -ne (Invoke-ShellUi get-property RuntimeStatus --property BoundingRectangle).properties.BoundingRectangle) { throw 'A fixed sidebar region moved with the resource list.' }
    $checks.fixedSidebar = 'Passed'

    $null = Invoke-ShellUi invoke AllProfiles
    Wait-Page ProfilesPage
    $null = Invoke-ShellUi invoke PinProfile_000003e9000000000000000000000000
    $null = Wait-State { param($state) $state.order.otherProfileIds[0] -eq '000003e9-0000-0000-0000-000000000000' }
    $null = Invoke-ShellUi wait-for MenuProfile_000003ea000000000000000000000000 --property IsEnabled --value True --timeout 3000
    $null = Invoke-ShellUi invoke MenuProfile_000003ea000000000000000000000000
    $null = Invoke-ShellUi wait-for MoveDownProfile_000003ea000000000000000000000000 --timeout 3000
    $null = Invoke-ShellUi invoke MoveDownProfile_000003ea000000000000000000000000
    $null = Wait-State { param($state) $state.order.pinnedProfileIds[0] -eq '000003eb-0000-0000-0000-000000000000' }
    Capture-Shell 'profiles-stress'
    $null = Invoke-ShellUi invoke ProfileRow_000003eb000000000000000000000000
    Wait-Page ProfileDetailPage
    Capture-Shell 'profile-detail'
    $null = Invoke-ShellUi set-value ProfileName 'Shell verification profile'
    $null = Invoke-ShellUi invoke SaveProfile
    $null = Wait-State { param($state) ($state.profiles | Where-Object id -eq '000003eb-0000-0000-0000-000000000000').name -eq 'Shell verification profile' }
    $checks.resourceEditsAndPinOrder = 'Passed'

    $null = Invoke-ShellUi invoke Settings
    Wait-Page SettingsPage
    $null = Invoke-ShellUi invoke ThemeSelector
    $null = Invoke-ShellUi invoke ThemeLight
    $null = Invoke-ShellUi invoke DashboardButton
    Wait-Page DashboardPage
    Resize-Shell 900 600
    Capture-Shell 'dashboard-900'
    Resize-Shell 640 600
    Capture-Shell 'dashboard-640'
    $null = Invoke-ShellUi scroll DashboardScroll --to bottom
    if ((Invoke-ShellUi get-property StatisticsHeading --property IsOffscreen).properties.IsOffscreen -ne 'False') { throw 'Statistics were not reachable in the narrow layout.' }
    Resize-Shell 500 600
    $null = Invoke-ShellUi invoke CompactSearchButton
    $null = Invoke-ShellUi wait-for CompactSearch --timeout 3000
    [TunnelDeck.Verification.ShellNative]::Press($appProcess.MainWindowHandle, 0x1b)
    Capture-Shell 'dashboard-500'
    $checks.adaptiveLayouts = 'Passed (900, 640 and 500 epx)'

    Resize-Shell 1200 760
    Select-Preview Empty
    $null = Wait-State { param($state) $state.servers.Count -eq 0 -and $state.profiles.Count -eq 0 }
    $null = Invoke-ShellUi invoke PART_PaneToggleButton
    if ((Invoke-ShellUi search DashboardButton).matchCount -eq 0) { $null = Invoke-ShellUi invoke PART_PaneToggleButton }
    $null = Invoke-ShellUi invoke AllServers
    Wait-Page ServersPage
    $null = Invoke-ShellUi invoke EmptyAddResource
    Wait-Page ServerDetailPage
    $null = Invoke-ShellUi set-value ServerDisplayName 'Saved test server'
    $null = Invoke-ShellUi set-value ServerHost 'test.example.com'
    $null = Invoke-ShellUi set-value ServerUsername 'tester'
    $null = Invoke-ShellUi set-value ServerPort '22'
    $null = Invoke-ShellUi invoke SaveServer
    $null = Wait-State { param($state) $state.servers.Count -eq 1 -and $state.servers[0].displayName -eq 'Saved test server' }
    Capture-Shell 'server-detail'
    $null = Invoke-ShellUi invoke AllProfiles
    Wait-Page ProfilesPage
    Capture-Shell 'profiles-empty'
    $null = Invoke-ShellUi invoke EmptyAddResource
    Wait-Page ProfileDetailPage
    $null = Invoke-ShellUi set-value ProfileName 'Saved test profile'
    $null = Invoke-ShellUi invoke SaveProfile
    $null = Wait-State { param($state) $state.profiles.Count -eq 1 -and $state.profiles[0].name -eq 'Saved test profile' }
    $checks.emptyAndAddFlows = 'Passed'
    Close-Shell

    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $fixturePath -Configuration $Configuration
    Wait-Page DashboardPage
    $null = Invoke-ShellUi invoke AllServers
    Wait-Page ServersPage
    $savedServerId = [Guid](Read-State).servers[0].id
    $null = Invoke-ShellUi wait-for ('ServerRow_' + $savedServerId.ToString('N')) --timeout 3000
    $checks.restartPersistence = 'Passed'
    Close-Shell
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow; checks = $checks; dataDirectory = $fixturePath; highContrast = 'Not switched globally; ThemeResource audit and manual review pending'; humanGuiReview = 'Pending' } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Get-Content -LiteralPath (Join-Path $outputPath 'result.json') -Raw
}
finally { Close-Shell }
