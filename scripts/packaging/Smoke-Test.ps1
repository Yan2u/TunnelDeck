[CmdletBinding()]
param(
    [string] $InstallDirectory = $PSScriptRoot,
    [string] $PreviousExecutable,
    [string] $OutputDirectory = (Join-Path $env:TEMP ('TunnelDeck-smoke-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$installPath = [IO.Path]::GetFullPath($InstallDirectory)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$dataPath = Join-Path $outputPath 'isolated-data'
if (Test-Path -LiteralPath $dataPath) { throw 'Use a new output directory; existing smoke data is not overwritten.' }
$null = New-Item -ItemType Directory -Path $dataPath -Force
$statePath = Join-Path $dataPath 'state.json'
$serverId = [guid]::NewGuid()
$legacyState = @{
    schemaVersion = 1; revision = 0
    servers = @(@{ id = $serverId.ToString(); displayName = 'Legacy smoke server'; host = 'release.example'; port = 22; username = 'smoke'; authMethod = 0 })
    profiles = @(); order = @{ pinnedServerIds = @(); otherServerIds = @($serverId.ToString()); pinnedProfileIds = @(); otherProfileIds = @() }
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $serverId.ToString(); pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true }
    recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($statePath, $legacyState, [Text.UTF8Encoding]::new($false))
$originalHash = (Get-FileHash -LiteralPath $statePath).Hash
$process = $null
$checks = [ordered]@{}
$passed = $false

function Wait-Element([string] $value, [switch] $ByName, [switch] $Enabled) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'The smoke application exited before the expected UI appeared.' }
        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $property = if ($ByName) { [System.Windows.Automation.AutomationElement]::NameProperty } else { [System.Windows.Automation.AutomationElement]::AutomationIdProperty }
            $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
            $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($element -and (-not $Enabled -or $element.Current.IsEnabled)) { return $element }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "The expected smoke UI was not available: $value"
}
function Invoke-Element([string] $id) {
    $element = Wait-Element $id -Enabled
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return }
    if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select(); return }
    throw "The element has no native activation pattern: $id"
}
function Start-SmokeApplication([string] $executable, [string] $directory) {
    if ($directory.Contains('"')) { throw 'The data path contains unsupported quotation marks.' }
    $script:process = Start-Process -FilePath $executable -ArgumentList ('"--data-directory=' + $directory + '"') -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do { $process.Refresh(); if ($process.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 50 } while (-not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline)
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'The application did not expose a window.' }
    $null = Wait-Element DashboardPage
}
function Close-SmokeApplication {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'The smoke application did not exit normally.' }
    }
}
function Read-SmokeState {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { return Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json }
        catch [IO.IOException] { Start-Sleep -Milliseconds 30 }
    }
    throw 'The smoke state file is not readable.'
}

try {
    & (Join-Path $installPath 'Check-Prerequisites.ps1') -Json | Set-Content -LiteralPath (Join-Path $outputPath 'prerequisites.json') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw 'Install the prerequisites listed in README.md before running the smoke test.' }
    $executable = Join-Path $installPath 'TunnelDeck.exe'
    Start-SmokeApplication $executable $dataPath
    $null = Wait-Element QuickServer -Enabled
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $originalHash) { throw 'Loading the old data modified it before an edit.' }
    Invoke-Element AllServers
    Invoke-Element ('ServerRow_' + $serverId.ToString('N'))
    $name = Wait-Element ServerDisplayName -Enabled
    $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Upgraded smoke server')
    Invoke-Element SaveServer
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do { $saved = Read-SmokeState; if ($saved.schemaVersion -eq 2 -and $saved.servers[0].displayName -eq 'Upgraded smoke server') { break }; Start-Sleep -Milliseconds 50 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($saved.schemaVersion -ne 2 -or $saved.servers[0].displayName -ne 'Upgraded smoke server') { throw 'The packaged application did not save the upgrade.' }
    if ((Get-FileHash -LiteralPath ($statePath + '.bak')).Hash -ne $originalHash) { throw 'The upgrade did not preserve the previous data.' }
    Close-SmokeApplication
    $checks.upgradeThroughRealUiAndOriginalBackup = $true
    $upgradedHash = (Get-FileHash -LiteralPath $statePath).Hash
    Start-SmokeApplication $executable $dataPath
    $null = Wait-Element QuickServer -Enabled
    Close-SmokeApplication
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $upgradedHash) { throw 'Restarting the package rewrote saved data.' }
    $checks.packagedRestartAndNormalExit = $true
    if ($PreviousExecutable) {
        Start-SmokeApplication ([IO.Path]::GetFullPath($PreviousExecutable)) $dataPath
        $null = Wait-Element 'Saved data is read-only' -ByName
        Close-SmokeApplication
        if ((Get-FileHash -LiteralPath $statePath).Hash -ne $upgradedHash) { throw 'The older application overwrote newer data.' }
        $checks.previousApplicationProtectsNewSchema = $true
        $rollbackData = Join-Path $outputPath 'restored-old-backup'
        $null = New-Item -ItemType Directory -Path $rollbackData
        Copy-Item -LiteralPath ($statePath + '.bak') -Destination (Join-Path $rollbackData 'state.json')
        Start-SmokeApplication ([IO.Path]::GetFullPath($PreviousExecutable)) $rollbackData
        $null = Wait-Element QuickServer -Enabled
        Close-SmokeApplication
        if ((Get-FileHash -LiteralPath (Join-Path $rollbackData 'state.json')).Hash -ne $originalHash) { throw 'The old application rewrote its restored backup.' }
        $checks.explicitRollbackToSeparateOldBackup = $true
    }
    $saved = Read-SmokeState
    if ($saved.statistics.totalForwards -ne 0 -or $saved.recentActivity.Count -ne 0) { throw 'The package connected automatically.' }
    $checks.noAutomaticConnections = $true
    $passed = $true
}
finally {
    try { Close-SmokeApplication }
    finally {
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; checks = $checks; installDirectory = $installPath; dataDirectory = $dataPath; previousExecutable = $PreviousExecutable; normalUserDataModified = $false } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'smoke.json') -Encoding UTF8
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'smoke.json')
