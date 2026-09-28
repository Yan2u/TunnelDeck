[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$reviewRoot = Join-Path $repoRoot ('.test-output/m11-review/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $reviewRoot 'data'
$samplePath = Join-Path $reviewRoot 'samples'
$statePath = Join-Path $dataPath 'state.json'
$outputPath = Join-Path $repoRoot 'artifacts/m11-review/gui-review'
$null = New-Item -ItemType Directory -Path $dataPath, $samplePath, $outputPath -Force
$first = @{ id = [guid]::NewGuid().ToString(); displayName = 'M11 review · Build server'; host = 'first.example'; port = 22; username = 'developer'; authMethod = 0 }
$second = @{ id = [guid]::NewGuid().ToString(); displayName = 'M11 review · Build server'; host = 'second.example'; port = 2222; username = 'developer'; authMethod = 0 }
$sampleProfile = @{
    id = [guid]::NewGuid().ToString(); name = 'M11 review · Portable workspace'; rules = @(
        @{ id = [guid]::NewGuid().ToString(); serverId = $first.id; mode = 0; localHost = '127.0.0.1'; localPort = 3010; remoteHost = '127.0.0.1'; remotePort = 5432; autoFindAvailablePort = $true },
        @{ id = [guid]::NewGuid().ToString(); serverId = $second.id; mode = 1; localHost = '127.0.0.1'; localPort = 9000; remoteHost = '127.0.0.1'; remotePort = 9010; autoFindAvailablePort = $false }
    )
}
$order = @{ pinnedServerIds = @($first.id); otherServerIds = @($second.id); pinnedProfileIds = @($sampleProfile.id); otherProfileIds = @() }
@{
    schemaVersion = 1; revision = 0; servers = @($first, $second); profiles = @($sampleProfile); order = $order
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $first.id; pinnedQuickServerId = $first.id; autoFindAvailablePortDefault = $true }
    recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$jsonPath = Join-Path $samplePath 'tunneldeck-sample.json'
@{ type = 'tunneldeck-export'; schemaVersion = 1; exportedAt = [DateTimeOffset]::UtcNow.ToString('O'); servers = @($first, $second); profiles = @($sampleProfile); order = $order } |
    ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding utf8NoBOM
$sshPath = Join-Path $samplePath 'ssh-sample.config'
@'
# Isolated M11 GUI review. These reserved example hosts are not real servers.
Host m11-review-ready
    HostName staging.example
    User developer
    Port 2222
Host m11-review-needs-user
    HostName private.example
    ProxyJump bastion.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
Transfer-Wait DashboardPage
Transfer-Preview $sshPath -Ssh
$null = Transfer-Ui scroll ImportPreview --to bottom
Transfer-Capture 'import-preview'
[ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    processId = $appProcess.Id; windowHandle = $appProcess.MainWindowHandle.ToInt64()
    dataPath = $dataPath; jsonSample = $jsonPath; sshConfigSample = $sshPath
    isolation = 'Dedicated data and credential namespace; reserved .example hosts; zero connections started'
    guiReview = 'M11 GUI G pending user evaluation'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding utf8NoBOM
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'review.json')
