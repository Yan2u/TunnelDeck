$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')

function New-ActivityReviewData([string] $root) {
    $data = Join-Path $root 'data'
    $null = New-Item -ItemType Directory -Path $data -Force
    $now = [DateTimeOffset]::UtcNow
    $servers = @(0..11 | ForEach-Object {
        $name = if ($_ -lt 2) { 'Build server' } elseif ($_ -eq 2) { 'Renamed server' } else { "Development server $_" }
        @{ id = [guid]::NewGuid().ToString(); displayName = $name; host = "server-$_.example"; port = 22; username = 'developer'; authMethod = 0; lastUsedAt = $now.AddMinutes(-$_).ToString('O') }
    })
    $rule = @{ id = [guid]::NewGuid().ToString(); serverId = $servers[0].id; mode = 0; localHost = '127.0.0.1'; localPort = 3000; remoteHost = '127.0.0.1'; remotePort = 3000; autoFindAvailablePort = $true }
    $profiles = @(
        @{ id = [guid]::NewGuid().ToString(); name = 'Development workspace'; rules = @($rule) },
        @{ id = [guid]::NewGuid().ToString(); name = 'Development workspace'; rules = @() }
    )
    $deletedServer = [guid]::NewGuid().ToString()
    $deletedProfile = [guid]::NewGuid().ToString()
    $recent = @(0..199 | ForEach-Object {
        $entry = @{
            id = [guid]::NewGuid().ToString(); occurredAt = $now.AddMinutes(-5 * $_).ToString('O'); outcome = ($_ % 4)
            requested = @{ id = [guid]::NewGuid().ToString(); serverId = $servers[$_ % 3].id; mode = ($_ % 2); localHost = '127.0.0.1'; localPort = 3000 + $_; remoteHost = '127.0.0.1'; remotePort = 3000; autoFindAvailablePort = $false }
            source = @{ serverName = $servers[$_ % 3].displayName; profileId = $profiles[0].id; profileName = $profiles[0].name }
        }
        if ($_ -eq 0) {
            $entry.requested.serverId = $deletedServer
            $entry.source = @{ serverName = 'Retired server'; profileId = $deletedProfile; profileName = 'Removed workspace' }
        }
        if ($_ % 3 -eq 2) { $entry.source.serverName = 'Original server' }
        if ($_ % 4 -eq 2) { $entry.error = @{ kind = 3; message = 'The remote connection was lost.' } }
        $entry
    })
    $serverCounts = @{}; $serverCounts[$deletedServer] = 8000; $serverCounts[$servers[0].id] = 501; $serverCounts[$servers[1].id] = 499
    $profileCounts = @{}; $profileCounts[$deletedProfile] = 900; $profileCounts[$profiles[0].id] = 30; $profileCounts[$profiles[1].id] = 15
    $state = @{
        schemaVersion = 1; revision = 0; servers = $servers; profiles = $profiles
        order = @{ pinnedServerIds = @($servers[0..10].id); otherServerIds = @($servers[11].id); pinnedProfileIds = @($profiles[0].id); otherProfileIds = @($profiles[1].id) }
        settings = @{ sidebarMaximumItems = 5; defaultServerId = $servers[0].id; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true }
        recentActivity = $recent; statistics = @{ totalForwards = 9000; serverForwards = $serverCounts; profileUses = $profileCounts }
    }
    $state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $data 'state.json') -Encoding utf8NoBOM
    return @{ dataPath = $data; statePath = (Join-Path $data 'state.json'); servers = $servers; profiles = $profiles }
}

function Activity-WaitState([scriptblock] $predicate) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $snapshot = Transfer-ReadState
        if (& $predicate $snapshot) { return $snapshot }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected activity/settings commit did not appear.'
}

function Activity-SetMaximum([string] $value) {
    $null = Transfer-Ui set-value SidebarMaximum $value
    $null = Transfer-Ui focus SidebarMaximum
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09)
}
