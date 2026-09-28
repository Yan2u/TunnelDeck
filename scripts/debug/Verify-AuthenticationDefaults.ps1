[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $OutputDirectory)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/auth-defaults/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$filesPath = Join-Path $runPath 'files'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m12-auth-defaults/native' }
$null = New-Item -ItemType Directory -Path $dataPath, $filesPath, $outputPath -Force
$statePath = Join-Path $dataPath 'state.json'
$legacyServer = @{ id = [guid]::NewGuid().ToString(); displayName = 'Existing password server'; host = 'existing.example'; port = 22; username = 'developer'; authMethod = 0 }
@{
    schemaVersion = 1; revision = 0; servers = @($legacyServer); profiles = @()
    order = @{ pinnedServerIds = @(); otherServerIds = @($legacyServer.id); pinnedProfileIds = @(); otherProfileIds = @() }
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $null; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true }
    recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$keyPath = Join-Path $filesPath 'selected 私钥.key'
$explicitPath = Join-Path $filesPath 'explicit identity.key'
$alternatePath = Join-Path $filesPath 'alternate.key'
foreach ($path in @($keyPath, $explicitPath, $alternatePath)) {
    Set-Content -LiteralPath $path -Value 'UI fixture only. Not a private key; this test never connects.' -Encoding utf8NoBOM
}
$sshPath = Join-Path $filesPath 'defaults.config'
@"
Host uses-default
 HostName default.example
 User developer
 Compression yes
Host explicit-identity
 HostName explicit.example
 User developer
 IdentityFile "$explicitPath"
Host also-default
 HostName other.example
 User developer
"@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$sourceHash = (Get-FileHash -LiteralPath $sshPath).Hash
$appProcess = $null
$lockedFile = $null
$checks = [ordered]@{}

function Open-AuthenticationSettings {
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    $null = Transfer-Ui scroll SettingsScroll --to bottom
}
function Select-DefaultAuthentication([string] $selector) {
    $null = Transfer-Ui focus DefaultAuthentication
    $key = if ($selector -eq 'DefaultAuthPrivateKey') { 0x23 } else { 0x24 }
    # Saving can replace the popup peer before UIA's SelectionItem call returns. Use the native keyboard contract.
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, $key)
    $null = Transfer-Ui wait-for DefaultAuthentication --property IsEnabled --value True --timeout 10000
    $null = Transfer-Ui scroll SettingsScroll --to bottom
}
function Add-DefaultServer([string] $name, [string] $hostName) {
    Transfer-Invoke AllServers
    Transfer-Wait ServersPage
    Transfer-Invoke AddServer
    Transfer-Wait ServerDetailPage
    $null = Transfer-Ui set-value ServerDisplayName $name
    $null = Transfer-Ui set-value ServerHost $hostName
    $null = Transfer-Ui set-value ServerUsername 'developer'
}

try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    Open-AuthenticationSettings
    if ((Transfer-Ui search DefaultKeyPath).matchCount -ne 0) { throw 'Password defaults displayed a private-key field.' }
    if ((Transfer-ReadState).schemaVersion -ne 1) { throw 'Loading settings wrote the migrated file before an edit.' }
    Select-DefaultAuthentication DefaultAuthPrivateKey
    $null = Activity-WaitState { param($state) $state.schemaVersion -eq 2 -and $state.settings.defaultAuthenticationMethod -eq 1 }
    Transfer-Wait DefaultKeyPath
    $detected = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.ssh/id_rsa'
    if ([IO.File]::Exists($detected)) {
        if ((Transfer-Value DefaultKeyPath) -ne $detected) { throw 'The default id_rsa path was not detected.' }
        if ((Transfer-ReadState).settings.defaultIdentityFilePath -ne $detected) { throw 'The detected key was not saved with the selected authentication method.' }
    }
    Transfer-Capture 'detected-key-light'
    $checks.defaultPasswordMigrationAndIdRsaDetection = 'Passed'

    $beforePicker = (Transfer-ReadState).revision
    Transfer-Invoke BrowseDefaultKey
    Transfer-CancelPicker
    if ((Transfer-ReadState).revision -ne $beforePicker) { throw 'Cancelling the key picker saved a change.' }
    Transfer-Invoke BrowseDefaultKey
    Transfer-PickPath $keyPath
    $null = Activity-WaitState { param($state) $state.settings.defaultIdentityFilePath -eq $keyPath }
    Transfer-Capture 'selected-key-light'
    $checks.nativeKeyPickerAndCancel = 'Passed'

    Add-DefaultServer 'Created with key defaults' 'created.example'
    if ((Transfer-Value IdentityFilePath) -ne $keyPath) { throw 'A new server did not receive the selected default key.' }
    Transfer-Invoke SaveServer
    $createdState = Activity-WaitState { param($state) @($state.servers | Where-Object displayName -eq 'Created with key defaults').Count -eq 1 }
    $created = $createdState.servers | Where-Object displayName -eq 'Created with key defaults'
    if ($created.authMethod -ne 1 -or $created.identityFilePath -ne $keyPath) { throw 'The new server did not persist key authentication.' }
    $existing = $createdState.servers | Where-Object id -eq $legacyServer.id
    if ($existing.authMethod -ne 0 -or $existing.identityFilePath) { throw 'Changing defaults rewrote an existing server.' }
    $checks.newServerPrefillAndExistingServerIsolation = 'Passed'

    Transfer-Preview $sshPath -Ssh
    if ((Transfer-Value ImportSummary) -ne '3 new servers · 0 new profiles · 0 skipped') { throw 'The default-key SSH import preview was incomplete.' }
    Transfer-Capture 'ssh-default-and-explicit-preview'
    Transfer-Invoke CommitImport
    Transfer-Wait ServersPage
    $imported = (Transfer-ReadState).servers | Where-Object id -notin $createdState.servers.id
    if ($imported.Count -ne 3 -or @($imported | Where-Object authMethod -ne 1).Count -gt 0) { throw 'SSH import did not apply key authentication.' }
    if (($imported | Where-Object displayName -eq 'explicit-identity').identityFilePath -ne $explicitPath) { throw 'Defaults replaced an explicit IdentityFile.' }
    if (@($imported | Where-Object { $_.displayName -ne 'explicit-identity' -and $_.identityFilePath -ne $keyPath }).Count -gt 0) { throw 'SSH entries without IdentityFile did not use the configured default key.' }
    $checks.sshDefaultAndExplicitIdentityPrecedence = 'Passed'

    Open-AuthenticationSettings
    Select-DefaultAuthentication DefaultAuthPassword
    $null = Activity-WaitState { param($state) $state.settings.defaultAuthenticationMethod -eq 0 }
    if ((Transfer-Ui search DefaultKeyPath).matchCount -ne 0) { throw 'Password defaults did not hide the key controls.' }
    Add-DefaultServer 'Created with password defaults' 'password.example'
    if ((Transfer-Ui search IdentityFilePath).matchCount -ne 0) { throw 'A new password server displayed key authentication.' }
    Transfer-Invoke SaveServer
    $beforePasswordImport = Activity-WaitState { param($state) @($state.servers | Where-Object displayName -eq 'Created with password defaults').Count -eq 1 }
    Transfer-Preview $sshPath -Ssh
    Transfer-Invoke CommitImport
    Transfer-Wait ServersPage
    $passwordImported = (Transfer-ReadState).servers | Where-Object id -notin $beforePasswordImport.servers.id
    if (($passwordImported | Where-Object displayName -eq 'explicit-identity').authMethod -ne 1) { throw 'An explicit IdentityFile did not override password defaults.' }
    if (@($passwordImported | Where-Object { $_.displayName -ne 'explicit-identity' -and ($_.authMethod -ne 0 -or $_.identityFilePath) }).Count -gt 0) { throw 'Password defaults copied a private key into an unspecified entry.' }
    $checks.passwordDefaultAndConditionalKeyFields = 'Passed'

    Open-AuthenticationSettings
    Select-DefaultAuthentication DefaultAuthPrivateKey
    Transfer-Wait DefaultKeyPath
    $null = Transfer-Ui set-value DefaultKeyPath (Join-Path $filesPath 'missing.key')
    Transfer-Invoke SaveDefaultKey
    Transfer-Wait SettingsSaveError
    if ((Transfer-ReadState).settings.defaultIdentityFilePath -ne $keyPath) { throw 'An invalid key path replaced the saved default.' }
    $null = Transfer-Ui set-value DefaultKeyPath $alternatePath
    $lockedFile = [IO.File]::Open($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    Transfer-Invoke SaveDefaultKey
    Transfer-Wait RetrySettingsSave
    if ((Transfer-ReadState).settings.defaultIdentityFilePath -ne $keyPath) { throw 'A failed default-key save changed persistent settings.' }
    Transfer-Capture 'key-save-failure'
    $lockedFile.Dispose(); $lockedFile = $null
    Transfer-Invoke RetrySettingsSave
    $null = Activity-WaitState { param($state) $state.settings.defaultIdentityFilePath -eq $alternatePath }
    $checks.invalidKeyAndFailedSaveRetry = 'Passed'

    $null = Transfer-Ui scroll SettingsScroll --to top
    Transfer-Invoke ThemeSelector
    Transfer-Invoke ThemeDark
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 720)
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $null = Transfer-Visible DefaultKeyPath
    $null = Transfer-Visible BrowseDefaultKey
    Transfer-Capture 'authentication-500-dark'
    $checks.narrowAuthenticationSettings = 'Passed'
    Transfer-Close

    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    Open-AuthenticationSettings
    if ((Transfer-Value DefaultKeyPath) -ne $alternatePath) { throw 'The default key was not restored after restart.' }
    Add-DefaultServer 'Restart draft' 'restart.example'
    if ((Transfer-Value IdentityFilePath) -ne $alternatePath) { throw 'New server prefill did not use the restored default.' }
    if ((Get-FileHash -LiteralPath $sshPath).Hash -ne $sourceHash) { throw 'Import modified the source SSH config.' }
    $finalState = Transfer-ReadState
    if ($finalState.statistics.totalForwards -ne 0 -or $finalState.recentActivity.Count -ne 0) { throw 'Authentication configuration created runtime activity.' }
    $checks.restartAndSourcePreservation = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; dataPath = $dataPath; schemaVersion = $finalState.schemaVersion; userDataModified = $false } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Write-Host 'Default authentication, native key selection, SSH import precedence and persistence verification passed.'
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'authentication-defaults-failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose() }
    Transfer-Close
}
