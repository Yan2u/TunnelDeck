[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$fixturePath = Join-Path $repoRoot ('.test-output\storage-window\' + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $repoRoot 'artifacts\storage-window-verification'
New-Item -ItemType Directory -Path $fixturePath, $outputPath -Force | Out-Null
$statePath = Join-Path $fixturePath 'state.json'
$backupPath = $statePath + '.bak'
$valid = [ordered]@{
    schemaVersion = 1
    revision = 4
    servers = @()
    profiles = @()
    order = @{ pinnedServerIds = @(); otherServerIds = @(); pinnedProfileIds = @(); otherProfileIds = @() }
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $null; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true }
    recentActivity = @()
    statistics = @{ totalForwards = 42; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 8
$future = '{"schemaVersion":2,"futureData":{"preserve":true}}'
Set-Content -LiteralPath $statePath -Value $future -Encoding utf8NoBOM
Set-Content -LiteralPath $backupPath -Value $valid -Encoding utf8NoBOM
$futureHash = (Get-FileHash -LiteralPath $statePath).Hash
$appProcess = $null

function Invoke-StorageUi {
    $result = & winapp ui @args -w $appProcess.MainWindowHandle --json
    $commandExit = $LASTEXITCODE
    $parsed = $result | ConvertFrom-Json
    $emptySearch = $args[0] -eq 'search' -and $parsed.PSObject.Properties['matchCount'] -and $parsed.matchCount -eq 0
    if ($commandExit -ne 0 -and -not $emptySearch) { throw "Storage UI verification failed: $result" }
    return $parsed
}

function Wait-StorageNotice([string] $text, [bool] $visible = $true) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $matches = Invoke-StorageUi search $text
        if (($matches.matchCount -gt 0) -eq $visible) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Storage notice did not reach its expected state: $text / visible=$visible"
}

function Close-StorageWindow {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(5000)) { throw 'Storage test window did not finish shutdown.' }
        if ($appProcess.ExitCode -ne 0) { throw 'Storage test window exited with an error.' }
    }
}

try {
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $fixturePath -Configuration $Configuration
    Wait-StorageNotice 'Saved data is read-only'
    $null = Invoke-StorageUi screenshot --output (Join-Path $outputPath 'future-schema.png')
    $null = Invoke-StorageUi invoke StorageRetry
    Wait-StorageNotice 'Saved data is read-only'
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $futureHash) { throw 'The future schema was overwritten.' }

    Set-Content -LiteralPath $statePath -Value $valid -Encoding utf8NoBOM
    $validHash = (Get-FileHash -LiteralPath $statePath).Hash
    $null = Invoke-StorageUi invoke StorageRetry
    Wait-StorageNotice 'StorageNotice' $false
    $appProcess.Refresh()
    if (-not $appProcess.Responding) { throw 'Storage retry blocked the window.' }
    Close-StorageWindow

    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $fixturePath -Configuration $Configuration
    Wait-StorageNotice 'StorageNotice' $false
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $validHash) { throw 'Loading or closing rewrote a clean configuration.' }
    Close-StorageWindow

    Set-Content -LiteralPath $statePath -Value '{broken' -Encoding utf8NoBOM
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $fixturePath -Configuration $Configuration
    Wait-StorageNotice 'Loaded a saved backup'
    $null = Invoke-StorageUi screenshot --output (Join-Path $outputPath 'recovered-backup.png')
    Set-Content -LiteralPath $backupPath -Value '{also broken' -Encoding utf8NoBOM
    $null = Invoke-StorageUi invoke StorageRetry
    Wait-StorageNotice 'Saved data could not be loaded'
    $null = Invoke-StorageUi screenshot --output (Join-Path $outputPath 'corrupt-data.png')
    Set-Content -LiteralPath $statePath -Value $valid -Encoding utf8NoBOM
    $null = Invoke-StorageUi invoke StorageRetry
    Wait-StorageNotice 'StorageNotice' $false
    Close-StorageWindow
    [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow
        futureSchemaProtected = $true
        validBackupWasNotUsedToDowngrade = $true
        recoveredBackupNotice = $true
        bothFilesCorruptNotice = $true
        retryAfterExternalRepair = $true
        cleanLoadAndShutdownPreservedFile = $true
        isolatedDataDirectory = $fixturePath
        credentialNamespace = 'TunnelDeck/Isolated/<directory hash>'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'result.json')
    Get-Content -LiteralPath (Join-Path $outputPath 'result.json') -Raw
}
finally { Close-StorageWindow }
