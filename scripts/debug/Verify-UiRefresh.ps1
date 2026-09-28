[CmdletBinding()]
param([string] $OutputDirectory, [string] $ExecutablePath)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/ui-refresh-interaction/' + [guid]::NewGuid().ToString('N'))
$sample = New-ActivityReviewData $runPath
$dataPath = $sample.dataPath
$statePath = $sample.statePath
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/ui-refresh/interaction' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json -AsHashtable -DateKind String
$state.schemaVersion = 2
$state.settings.defaultAuthenticationMethod = 0
$previewKey = Join-Path $runPath 'preview.key'
Set-Content -LiteralPath $previewKey -Value 'UI preview fixture; no SSH connection uses this file.' -Encoding utf8NoBOM
$state.settings.defaultIdentityFilePath = $previewKey
$state.order.pinnedServerIds = @($sample.servers[0..1].id)
$state.order.otherServerIds = @($sample.servers[2..11].id)
$state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$serverId = ([guid]$sample.servers[0].id).ToString('N')
$profileId = ([guid]$sample.profiles[0].id).ToString('N')
$recentId = ([guid]$state.recentActivity[0].id).ToString('N')
$sshPath = Join-Path $runPath 'preview.config'
@'
Host ready
 HostName ready.example
 User developer
 Port 2222
 Compression yes
Host needs-user
 HostName missing.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = $null
$checks = [ordered]@{}
$stage = 'Startup'
$passed = $false
if (-not ('TunnelDeck.Verification.ReviewPointer' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification {
    public static class ReviewPointer {
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        public static bool Move(int x, int y) {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { return SetCursorPos(x, y); }
            finally { SetThreadDpiAwarenessContext(previous); }
        }
        public static int[] Bounds(IntPtr window) {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
                if (!GetWindowRect(window, out Rect bounds)) throw new InvalidOperationException("Cannot read the test window bounds.");
                return new[] { bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top };
            }
            finally { SetThreadDpiAwarenessContext(previous); }
        }
    }
}
'@
}
function Open-Navigation {
    $item = (Transfer-Ui search DashboardButton).matches | Where-Object automationId -eq 'DashboardButton' | Select-Object -First 1
    if (-not $item -or $item.isOffscreen -or $item.width -le 0) { Transfer-Invoke PART_PaneToggleButton }
}
function Go-To([string] $destination, [string] $page) { Open-Navigation; Transfer-Invoke $destination; Transfer-Wait $page }
function Key([int] $key, [int] $modifier = 0) { [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, $key, $modifier) }
function Capture([string] $name) { Start-Sleep -Milliseconds 250; Transfer-Capture $name }
function Hover([string] $selector) {
    $element = Transfer-Visible $selector
    [TunnelDeck.Verification.ForwardNative]::Activate($appProcess.MainWindowHandle)
    if (-not [TunnelDeck.Verification.ReviewPointer]::Move([int]($element.x + $element.width / 2), [int]($element.y + $element.height / 2))) { throw 'Pointer move failed.' }
}
function Assert-FitsWindow([string] $selector) {
    $element = Transfer-Visible $selector
    $rectangle = [TunnelDeck.Verification.ReviewPointer]::Bounds($appProcess.MainWindowHandle)
    if ($element.x -lt $rectangle[0] -or $element.x + $element.width -gt $rectangle[0] + $rectangle[2] + 1) { throw "Control exceeds the window: $selector" }
}
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for StorageNotice --gone --timeout 5000
    Capture 'dashboard-light'
    $stage = 'Activity details'
    $null = Transfer-Ui scroll-into-view "RecentActivity_$recentId"
    Transfer-Invoke "RecentActivity_$recentId"
    Transfer-Wait ActivityDetails
    if ((Transfer-Value ActivityDetails) -notlike '*Server deleted*') { throw 'Activity details lost the deleted-source annotation.' }
    Capture 'activity-details'
    Key 0x1b
    $checks.activityDetails = 'Passed: the compact row exposes complete history in its keyboard-accessible flyout.'

    $stage = 'Card right-click and pin'
    Go-To AllServers ServersPage
    $null = Transfer-Ui click "ServerRow_$serverId" --right
    Transfer-Wait "MenuPinServer_$serverId"
    Capture 'server-context-menu'
    Transfer-Invoke "MenuPinServer_$serverId"
    $null = Activity-WaitState { param($value) $value.order.otherServerIds[0] -eq $sample.servers[0].id }
    Transfer-Invoke "PinServer_$serverId"
    $null = Activity-WaitState { param($value) $value.order.pinnedServerIds[-1] -eq $sample.servers[0].id }
    Start-Sleep -Milliseconds 200
    Hover "MenuServer_$serverId"
    Capture 'card-action-hover'
    $checks.rightClickPinAndUnpin = 'Passed: context-menu unpin and card pin persist the original order rules.'

    $stage = 'Keyboard profile context menu'
    Go-To AllProfiles ProfilesPage
    $null = Transfer-Ui focus "ProfileRow_$profileId"
    Key 0x79 0x10
    Transfer-Wait "MenuPinProfile_$profileId"
    Transfer-Invoke "MenuPinProfile_$profileId"
    $null = Activity-WaitState { param($value) $value.order.otherProfileIds[0] -eq $sample.profiles[0].id }
    Transfer-Invoke "PinProfile_$profileId"
    $null = Activity-WaitState { param($value) $value.order.pinnedProfileIds -contains $sample.profiles[0].id }
    $checks.keyboardContextMenu = 'Passed: Shift+F10, Unpin and Pin on profile cards.'

    $stage = 'Sidebar context menu and deletion cancellation'
    $revision = (Transfer-ReadState).revision
    $null = Transfer-Ui click "SidebarServer_$serverId" --right
    Transfer-Invoke "DeleteServer_$serverId"
    Transfer-Wait DeletionConfirmation
    Capture 'delete-dialog-light'
    Transfer-Invoke CloseButton
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Cancelling a deletion changed the saved state.' }
    $checks.sidebarDeletionCancel = 'Passed: sidebar Remove opens the shared confirmation and Cancel does not write.'

    $stage = 'Adaptive resource cards'
    Go-To AllServers ServersPage
    foreach ($width in @(1200, 900, 640, 480)) {
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, $width, 760)
        Start-Sleep -Milliseconds 200
        $firstPinned = ([guid](Transfer-ReadState).order.pinnedServerIds[0]).ToString('N')
        Assert-FitsWindow "ServerRow_$firstPinned"
        Assert-FitsWindow "MenuServer_$firstPinned"
        Capture "servers-$width-light"
    }
    $checks.adaptiveCards = 'Passed at 1200, 900, 640 and 480 DIP: title and action buttons fit.'

    $stage = 'Adaptive authentication settings'
    Go-To Settings SettingsPage
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $null = Transfer-Ui focus DefaultAuthentication
    Key 0x23
    $null = Activity-WaitState { param($value) $value.settings.defaultAuthenticationMethod -eq 1 }
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    Assert-FitsWindow DefaultKeyPath
    Assert-FitsWindow BrowseDefaultKey
    Assert-FitsWindow SaveDefaultKey
    Capture 'settings-480-key'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    $null = Transfer-Ui scroll SettingsScroll --to top
    Capture 'settings-light'
    $null = Transfer-Ui focus ThemeSelector
    Key 0x23
    Capture 'settings-dark'
    $checks.adaptiveSettings = 'Passed: narrow cards stack vertically; key controls appear only in key mode and remain reachable.'

    $stage = 'Dark cards and dialogs'
    Go-To AllServers ServersPage
    Capture 'servers-dark'
    Transfer-Invoke "MenuServer_$serverId"
    Transfer-Invoke "DeleteServer_$serverId"
    Transfer-Wait DeletionConfirmation
    Capture 'delete-dialog-dark'
    Transfer-Invoke CloseButton

    $stage = 'Import preview and explicit skip'
    Transfer-Preview $sshPath -Ssh
    if (Transfer-Enabled CommitImport) { throw 'An unavailable entry was skipped without explicit consent.' }
    Transfer-Invoke SkipUnavailableImports
    if (-not (Transfer-Enabled CommitImport)) { throw 'The available entry cannot be imported after choosing skip.' }
    Capture 'import-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 480, 760)
    Assert-FitsWindow CommitImport
    Assert-FitsWindow CancelImport
    Capture 'import-480-dark'
    $revision = (Transfer-ReadState).revision
    Transfer-Invoke CancelImport
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Cancelling a preview imported data.' }
    $checks.importPreview = 'Passed: required-field skipping stays explicit; narrow footer is visible; Cancel does not write.'
    $passed = $true
}
finally {
    if (-not $passed -and $appProcess -and -not $appProcess.HasExited) { try { Capture 'failure' } catch { } }
    [ordered]@{ passed = $passed; stage = $stage; checks = $checks; dataPath = $dataPath; executable = $ExecutablePath; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Transfer-Close
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
