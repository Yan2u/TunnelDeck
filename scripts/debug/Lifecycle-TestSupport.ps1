$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
if (-not ('TunnelDeck.Verification.LifecycleNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification
{
    public static class LifecycleNative
    {
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
'@
}

function Life-Ui {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) {
        throw "Lifecycle UI command failed [$($args -join ' ')]: $raw"
    }
    return $result
}
function Life-Wait([string] $selector) { $null = Life-Ui wait-for $selector --timeout 15000 }
function Life-Invoke([string] $selector) {
    $null = Life-Ui wait-for $selector --property IsEnabled --value True --timeout 5000
    $null = Life-Ui invoke $selector
}
function Life-Absent([string] $selector) { $null = Life-Ui wait-for $selector --gone --timeout 5000 }
function Life-Visible([string] $selector) {
    $element = (Life-Ui search $selector).matches | Where-Object automationId -eq $selector | Select-Object -First 1
    if (-not $element -or $element.isOffscreen -or $element.width -le 0 -or $element.height -le 0) { throw "Control is not visible: $selector" }
    return $element
}
function Life-Capture([string] $name) {
    Start-Sleep -Milliseconds 250
    $null = Life-Ui screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function New-LifeState([object[]] $servers = @(), [object[]] $rules = @(), [string] $scenario = 'lifecycle') {
    $script:dataPath = Join-Path $repoRoot ('.test-output/m10-ui/' + $scenario + '-' + [guid]::NewGuid().ToString('N'))
    $script:statePath = Join-Path $dataPath 'state.json'
    $script:profileId = [guid]::NewGuid().ToString()
    $profiles = if ($rules.Count -gt 0) { @(@{ id = $profileId; name = 'Lifecycle verification'; rules = $rules }) } else { @() }
    $defaultServer = if ($servers.Count -gt 0) { $servers[0].id } else { $null }
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    [ordered]@{
        schemaVersion = 2; revision = 0; servers = $servers; profiles = @($profiles)
        order = @{ pinnedProfileIds = @($profiles | ForEach-Object { $_.id }); otherProfileIds = @(); pinnedServerIds = @($servers | ForEach-Object { $_.id }); otherServerIds = @() }
        settings = @{ sidebarMaximumItems = 5; defaultServerId = $defaultServer; pinnedQuickServerId = $defaultServer; autoFindAvailablePortDefault = $true; defaultAuthenticationMethod = 0; defaultIdentityFilePath = $null }
        recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
}
function Life-ReadState {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        try {
            $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        }
        catch [IO.IOException] { Start-Sleep -Milliseconds 20 }
    }
    throw 'The test configuration remained unavailable.'
}
function Life-WaitState([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $state = Life-ReadState
        if (& $condition $state) { return $state }
        Start-Sleep -Milliseconds 40
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected lifecycle state was not persisted.'
}
function Life-Open {
    $script:appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    $null = $appProcess.Handle
    Life-Wait DashboardPage
    Life-Absent StorageNotice
}
function Life-Close {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The lifecycle test instance did not close normally.' }
    }
}
function Life-WaitExit {
    if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The selected exit did not complete normally.' }
}
function Life-TunnelIds { @((Life-Ui search 'TunnelDetails_').matches | ForEach-Object { $_.automationId.Substring('TunnelDetails_'.Length) }) }
function Life-StartProfile([switch] $ApproveTrust) {
    Life-Invoke AllProfiles
    Life-Wait ProfilesPage
    Life-Invoke ('ProfileRow_' + ([guid]$profileId).ToString('N'))
    Life-Wait ProfileDetailPage
    Life-Invoke StartProfile
    if ($ApproveTrust) {
        Life-Wait HostKeyApproval
        $null = Life-Ui wait-for HostKeyFingerprint --value $manifest.hostKeyFingerprint --contains --timeout 5000
        Life-Invoke PrimaryButton
    }
}
function Life-Dashboard([int] $active) {
    Life-Invoke DashboardButton
    Life-Wait DashboardPage
    $null = Life-Ui wait-for ActiveCount --value "$active active" --timeout 15000
}
function Life-Theme([string] $theme) {
    Life-Invoke Settings
    Life-Wait SettingsPage
    Life-Invoke ThemeSelector
    Life-Invoke $theme
    Life-Invoke DashboardButton
    Life-Wait DashboardPage
}
function Life-Post([string] $route, [hashtable] $body) {
    Invoke-RestMethod -Uri "http://$($manifest.host):$($manifest.controlPort)/$route" -Method Post -NoProxy -TimeoutSec 10 `
        -Headers @{ Authorization = 'Bearer ' + $manifest.controlToken } -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)
}
function Life-RemotePort {
    $port = (Life-Post reserve @{ port = 0 }).port
    $null = Life-Post release @{ port = $port }
    return [int]$port
}
function Life-RemoteStopped([int] $port) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while ((Life-Post listening @{ port = $port }).listening) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'The remote listener was not released.' }
        Start-Sleep -Milliseconds 50
    }
}
function New-LifeRule([string] $serverId, [int] $localPort, [int] $remotePort, [int] $mode = 0) {
    @{ id = [guid]::NewGuid().ToString(); serverId = $serverId; mode = $mode; localHost = '127.0.0.1'; localPort = $localPort; remoteHost = '127.0.0.1'; remotePort = $remotePort; autoFindAvailablePort = ($mode -eq 0) }
}
function New-LifeServer([switch] $Password) {
    $server = @{ id = [guid]::NewGuid().ToString(); displayName = 'Disposable lifecycle server'; host = $manifest.host; port = $manifest.mutablePort; username = $manifest.username; authMethod = 1; identityFilePath = $manifest.plainPrivateKeyPath }
    if ($Password) { $server.authMethod = 0; $server.Remove('identityFilePath') }
    return $server
}
