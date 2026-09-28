function New-ScaleTestData([string] $root, [int] $count = 1000) {
    $dataPath = Join-Path $root 'data'
    $null = New-Item -ItemType Directory -Path $dataPath -Force
    $servers = @(0..($count - 1) | ForEach-Object {
        @{ id = [guid]::NewGuid().ToString(); displayName = ('Scale server {0:D4}' -f $_); host = "scale-$_.example"; port = 22; username = 'developer'; authMethod = 0 }
    })
    $profiles = @(0..($count - 1) | ForEach-Object {
        @{ id = [guid]::NewGuid().ToString(); name = ('Scale profile {0:D4}' -f $_); rules = @(
            @{ id = [guid]::NewGuid().ToString(); serverId = $servers[$_].id; mode = 0; localHost = '127.0.0.1'; localPort = 3000; remoteHost = '127.0.0.1'; remotePort = 3000; autoFindAvailablePort = $true }
        ) }
    })
    $pinned = [Math]::Min(20, $count - 1)
    $state = @{
        schemaVersion = 2; revision = 0; servers = $servers; profiles = $profiles
        order = @{ pinnedServerIds = @($servers[0..($pinned - 1)].id); otherServerIds = @($servers[$pinned..($count - 1)].id); pinnedProfileIds = @($profiles[0..($pinned - 1)].id); otherProfileIds = @($profiles[$pinned..($count - 1)].id) }
        settings = @{ sidebarMaximumItems = 5; defaultServerId = $servers[0].id; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true; defaultAuthenticationMethod = 0; defaultIdentityFilePath = $null }
        recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
    }
    $statePath = Join-Path $dataPath 'state.json'
    $state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
    return @{ dataPath = $dataPath; statePath = $statePath; servers = $servers; profiles = $profiles; pinned = $pinned }
}

function Get-ScaleProcessSample([Diagnostics.Process] $process) {
    $process.Refresh()
    return [ordered]@{ privateBytes = $process.PrivateMemorySize64; workingSetBytes = $process.WorkingSet64; handles = $process.HandleCount; threads = $process.Threads.Count }
}
