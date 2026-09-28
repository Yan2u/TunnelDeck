[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $OutputDirectory, [string] $ExecutablePath, [string] $DebuggerPath,
      [ValidateRange(1, 100)][int] $ResizeCycles = 1)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/m11-ui/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$filesPath = Join-Path $runPath 'files'
$statePath = Join-Path $dataPath 'state.json'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m11-review/native-transfer' }
$null = New-Item -ItemType Directory -Path $dataPath, $filesPath, $outputPath -Force
$appProcess = $null
$debuggerProcess = $null
$lockedFile = $null
$passed = $false
$checks = [ordered]@{}
$rendererModules = @()
$completedResizeCycles = 0
$servers = @(
    @{ id = [guid]::NewGuid().ToString(); displayName = 'Build server'; host = 'first.example'; port = 22; username = 'developer'; authMethod = 0 },
    @{ id = [guid]::NewGuid().ToString(); displayName = 'Build server'; host = 'second.example'; port = 2222; username = 'developer'; authMethod = 0 },
    @{ id = [guid]::NewGuid().ToString(); displayName = 'Missing key demo'; host = 'missing-key.example'; port = 22; username = 'developer'; authMethod = 1; identityFilePath = (Join-Path $filesPath 'missing.key') }
)
$sampleProfile = @{
    id = [guid]::NewGuid().ToString(); name = 'Portable workspace'; rules = @(
        @{ id = [guid]::NewGuid().ToString(); serverId = $servers[0].id; mode = 0; localHost = '127.0.0.1'; localPort = 3010; remoteHost = '127.0.0.1'; remotePort = 5432; autoFindAvailablePort = $true },
        @{ id = [guid]::NewGuid().ToString(); serverId = $servers[1].id; mode = 1; localHost = '127.0.0.1'; localPort = 9000; remoteHost = '127.0.0.1'; remotePort = 9010; autoFindAvailablePort = $false }
    )
}
[ordered]@{
    schemaVersion = 1; revision = 0; servers = $servers; profiles = @($sampleProfile)
    order = @{ pinnedProfileIds = @($sampleProfile.id); otherProfileIds = @(); pinnedServerIds = @($servers[0].id); otherServerIds = @($servers[1].id, $servers[2].id) }
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $servers[0].id; pinnedQuickServerId = $servers[0].id; autoFindAvailablePortDefault = $true }
    recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$sshPath = Join-Path $filesPath 'static.config'
@'
Host imported-ready
    HostName migrated.example
    User developer
    Port 2222
    Compression yes
    ForwardX11 yes
    ProxyJump bastion.example
Host invalid-port
    HostName blocked.example
    User developer
    Port 70000
    ProxyJump bastion.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$exportPath = Join-Path $filesPath 'portable 配置.json'

try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    if ($DebuggerPath) {
        . (Join-Path $PSScriptRoot 'Native-DebuggerSupport.ps1')
        $debuggerProcess = Start-NativeCrashCapture $appProcess $DebuggerPath (Join-Path $outputPath 'debugger')
    }
    Transfer-Wait DashboardPage
    Transfer-Invoke AllProfiles
    Transfer-Invoke MoreButton
    Transfer-Invoke ExportResources
    Transfer-Wait ExportPage
    if ((Transfer-Value ExportSummary) -ne '2 servers · 1 profile') { throw 'Profile export did not include exactly its dependencies.' }
    Transfer-Capture 'export-preview-light'
    $revision = (Transfer-ReadState).revision
    Transfer-Invoke SaveExport
    Write-Host 'Export picker: cancel without saving.'
    Transfer-CancelPicker
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Cancelling the export changed configuration.' }
    Transfer-Invoke SaveExport
    Write-Host 'Export picker: choose the isolated export path.'
    Transfer-PickPath $exportPath
    Transfer-Wait ExportSavedPath
    if ((Transfer-Value ExportSavedPath) -ne "Export saved to $exportPath") { throw 'The selected export path did not match the saved path.' }
    $export = Get-Content -Raw -LiteralPath $exportPath | ConvertFrom-Json
    if ($export.type -ne 'tunneldeck-export' -or $export.servers.Count -ne 2 -or $export.profiles.Count -ne 1) { throw 'The exported document is incomplete.' }
    $json = Get-Content -Raw -LiteralPath $exportPath
    if ($json -match 'credentialReference|password|recentActivity|statistics|trustedHost|activeTunnels') { throw 'The export contains non-portable data.' }
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Export changed saved configuration.' }
    $checks.nativeExportPickerAndCancel = 'Passed'
    $checks.profileDependenciesAndSecretExclusion = 'Passed'

    Transfer-Preview $exportPath
    if ((Transfer-Value ImportSummary) -ne '2 new servers · 1 new profile · 0 skipped') { throw 'The JSON preview is incomplete.' }
    if (-not (Transfer-Enabled CommitImport)) { throw 'The valid preview cannot be imported.' }
    Transfer-Capture 'json-preview-light'
    Transfer-Invoke CancelImport
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Cancelling the preview wrote configuration.' }
    $checks.previewCancelIsReadOnly = 'Passed'

    $invalidPath = Join-Path $filesPath 'invalid.json'
    $invalid = $json | ConvertFrom-Json -AsHashtable
    $invalid.schemaVersion = 99
    $invalid | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPath -Encoding utf8NoBOM
    Transfer-Preview $invalidPath
    Transfer-Wait ImportError
    if (Transfer-Enabled CommitImport) { throw 'An unsupported schema enabled import.' }
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Invalid JSON changed saved configuration.' }
    Transfer-Capture 'unsupported-json'
    $checks.invalidJsonIsNotPartiallyImported = 'Passed'

    Transfer-Preview $exportPath
    $lockedFile = [IO.File]::Open($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    Transfer-Invoke CommitImport
    Transfer-Wait ImportError
    if ((Transfer-ReadState).revision -ne $revision) { throw 'A failed import changed disk state.' }
    Transfer-Capture 'import-save-failure'
    $lockedFile.Dispose(); $lockedFile = $null
    Transfer-Invoke CommitImport
    Transfer-Wait ProfilesPage
    $imported = Transfer-ReadState
    if ($imported.servers.Count -ne 5 -or $imported.profiles.Count -ne 2 -or $imported.revision -ne $revision + 1) { throw 'Retry did not publish one complete import.' }
    $newServers = @($imported.servers | Where-Object { $_.id -notin $servers.id })
    $newProfile = @($imported.profiles | Where-Object id -ne $sampleProfile.id)[0]
    if ($newServers.Count -ne 2 -or @($newProfile.rules | Where-Object { $_.serverId -notin $newServers.id }).Count -ne 0) { throw 'Imported profile references old or missing servers.' }
    foreach ($rule in $newProfile.rules) {
        $actualServer = $newServers | Where-Object id -eq $rule.serverId
        $expectedHost = if ($rule.mode -eq 0) { 'first.example' } else { 'second.example' }
        if ($actualServer.host -ne $expectedHost) { throw 'The imported rule points to the wrong host.' }
    }
    if ($imported.statistics.totalForwards -ne 0 -or $imported.recentActivity.Count -ne 0) { throw 'Import produced runtime activity.' }
    Transfer-Wait ImportResultNotice
    $checks.atomicFailureRetryAndFreshReferences = 'Passed'
    $checks.sameNamesRemainSeparate = 'Passed'

    Transfer-Preview $sshPath -Ssh
    Transfer-Wait ImportWarnings
    if ((Transfer-Value ImportSummary) -ne '1 new server · 0 new profiles · 1 skipped') { throw 'SSH preview did not explain the skipped entry.' }
    if (Transfer-Enabled CommitImport) { throw 'Skipping unavailable SSH entries did not require acknowledgement.' }
    $null = Transfer-Ui scroll ImportPreview --to bottom
    Transfer-Capture 'ssh-warnings-light'
    $appProcess.Refresh()
    $rendererModules = @($appProcess.Modules | Where-Object { $_.ModuleName -like 'RTSS*' -or $_.ModuleName -eq 'Microsoft.UI.Xaml.dll' } | Select-Object ModuleName, FileName)
    for ($resizeCycle = 0; $resizeCycle -lt $ResizeCycles; $resizeCycle++) {
        # Preserve the original single-resize path; optional stress cycles also re-enter the preview.
        if ($ResizeCycles -gt 1) { $null = Transfer-Ui focus ImportFilePath }
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 580)
        $null = Transfer-Ui scroll ImportPreview --to bottom
        $null = Transfer-Visible CommitImport
        $null = Transfer-Visible CancelImport
        $null = Transfer-Visible SkipUnavailableImports
        if ($resizeCycle -eq $ResizeCycles - 1) { Transfer-Capture 'ssh-warnings-narrow' }
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
        $completedResizeCycles++
    }
    Transfer-Invoke SkipUnavailableImports
    Transfer-Invoke CommitImport
    Transfer-Wait ServersPage
    if ((Transfer-ReadState).servers.Count -ne 6) { throw 'SSH import did not create exactly one available server.' }
    Transfer-Wait ImportResultNotice
    $checks.sshWarningsAcknowledgementAndSkip = 'Passed'
    $checks.narrowScrollablePreviewAndFixedActions = 'Passed'

    Transfer-Invoke Settings
    Transfer-Invoke ThemeSelector
    Transfer-Invoke ThemeDark
    Transfer-Preview $sshPath -Ssh
    $null = Transfer-Ui scroll ImportPreview --to bottom
    Transfer-Capture 'ssh-warnings-dark'
    Transfer-Invoke CancelImport
    $checks.darkTheme = 'Passed'

    Transfer-Invoke AllServers
    Transfer-Invoke ('ServerRow_' + ([guid]$servers[2].id).ToString('N'))
    Transfer-Wait ServerDetailPage
    Transfer-Invoke MoreButton
    Transfer-Invoke OpenServerTerminal
    Transfer-Wait 'The private-key file was not found.'
    Transfer-Capture 'terminal-missing-key'
    $null = Transfer-Ui set-value ServerHost 'changed.example'
    Transfer-Invoke MoreButton
    if (Transfer-Enabled OpenServerTerminal) { throw 'A terminal could use unsaved connection edits.' }
    # Close the menu through the page heading before leaving the draft.
    $null = Transfer-Ui click ServerDetailPage
    Transfer-Invoke CancelServer
    Transfer-Invoke DashboardButton
    Transfer-Wait DashboardPage
    # The terminal command is directly available beside the advanced-options switch.
    $null = Transfer-Ui wait-for QuickPortText --property HasKeyboardFocus --value True --timeout 5000
    Transfer-Wait OpenQuickTerminal
    if (-not (Transfer-Enabled OpenQuickTerminal)) { throw 'Quick Forward has no available terminal command.' }
    $checks.terminalEntryMissingKeyAndUnsavedDraft = 'Passed'

    $beforeRestart = Transfer-ReadState
    Transfer-Close
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $afterRestart = Transfer-ReadState
    if ($afterRestart.servers.Count -ne $beforeRestart.servers.Count -or $afterRestart.profiles.Count -ne $beforeRestart.profiles.Count) { throw 'Imported resources did not survive restart.' }
    if ($afterRestart.recentActivity.Count -ne 0 -or $afterRestart.statistics.totalForwards -ne 0) { throw 'Restart connected an imported server automatically.' }
    $checks.restartWithoutAutomaticConnections = 'Passed'
    $passed = $true
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose() }
    try { Transfer-Close }
    finally {
        if ($debuggerProcess) { Stop-NativeCrashCapture $debuggerProcess }
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; checks = $checks; dataPath = $dataPath; exportPath = $exportPath; executableOverride = $ExecutablePath; rendererModules = $rendererModules; requestedResizeCycles = $ResizeCycles; completedResizeCycles = $completedResizeCycles; humanReview = 'M11 GUI G accepted; see milestones for current comprehensive review status' } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
