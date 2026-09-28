[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $OutputDirectory)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/m12-ui/' + [guid]::NewGuid().ToString('N'))
$sample = New-ActivityReviewData $runPath
$dataPath = $sample.dataPath
$statePath = $sample.statePath
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m12-review/native-activity' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$checks = [ordered]@{}
$appProcess = $null
$lockedFile = $null
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for ActiveCount --value '0 active' --timeout 10000
    $null = Transfer-Ui scroll DashboardScroll --to bottom
    if ((Transfer-Value TotalForwards) -ne '9,000') { throw 'Lifetime totals were recomputed from the recent list.' }
    if ((Transfer-Value MostUsedServer) -notlike 'Build server*') { throw 'A deleted resource displaced the most-used current server.' }
    if ((Transfer-Value RecentSummary) -ne 'Showing 5 of 200 recent events') { throw 'Recent activity did not start with five events.' }
    Transfer-Capture 'dashboard-1200-light'
    $revision = (Transfer-ReadState).revision
    Transfer-Invoke ToggleRecent
    if ((Transfer-Value RecentSummary) -ne 'Showing 200 of 200 recent events') { throw 'The retained history could not be expanded.' }
    $headingBefore = (Transfer-Ui get-property RecentHeading --property BoundingRectangle).properties.BoundingRectangle
    $null = Transfer-Ui scroll RecentActivityList --to bottom
    $headingAfter = (Transfer-Ui get-property RecentHeading --property BoundingRectangle).properties.BoundingRectangle
    if ($headingBefore -ne $headingAfter) { throw 'The bounded recent list scrolled the page.' }
    Transfer-Capture 'recent-expanded'
    Transfer-Invoke ToggleRecent
    if ((Transfer-ReadState).revision -ne $revision) { throw 'Reading or expanding activity wrote configuration.' }
    $checks.historyAndCurrentResourceRankings = 'Passed (200 events; five initially visible; deleted and renamed sources; no writes)'

    $null = Transfer-Ui scroll DashboardScroll --to top
    $null = Transfer-Ui focus QuickPortText
    $null = Transfer-Ui set-value QuickPortText '3019'
    $null = Transfer-Ui wait-for ForwardButton --property IsEnabled --value True --timeout 5000
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    Activity-SetMaximum '3'
    $null = Activity-WaitState { param($state) $state.settings.sidebarMaximumItems -eq 3 }
    Activity-SetMaximum '10'
    $null = Activity-WaitState { param($state) $state.settings.sidebarMaximumItems -eq 10 }
    Activity-SetMaximum '3.5'
    Transfer-Wait SettingsSaveError
    if ((Transfer-ReadState).settings.sidebarMaximumItems -ne 10) { throw 'An invalid sidebar maximum changed saved data.' }
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    Transfer-Invoke AutoFindDefault
    $null = Activity-WaitState { param($state) -not $state.settings.autoFindAvailablePortDefault }
    $null = Transfer-Ui invoke DefaultServer
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x24)
    1..3 | ForEach-Object { [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x28) }
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Activity-WaitState { param($state) $state.settings.defaultServerId -eq $sample.servers[2].id }
    Transfer-Invoke DashboardButton
    Transfer-Wait DashboardPage
    if ((Transfer-Value QuickPortText) -ne '3019') { throw 'Settings replaced the edited Quick Forward draft.' }
    if ((Transfer-Ui get-property AutoFindLocalPort --property ToggleState).properties.ToggleState -ne 'On') { throw 'A new default overwrote the existing Quick Forward option.' }
    if (-not (Transfer-ReadState).profiles[0].rules[0].autoFindAvailablePort) { throw 'Settings rewrote a saved forwarding rule.' }
    $checks.settingsBoundariesAndDraftIsolation = 'Passed (3/10, invalid fraction, default server, existing Quick/rule preservation)'

    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    $lockedFile = [IO.File]::Open($statePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
    Transfer-Invoke AutoFindDefault
    Transfer-Wait SettingsSaveError
    if ((Transfer-ReadState).settings.autoFindAvailablePortDefault) { throw 'A failed setting was published.' }
    Transfer-Capture 'settings-save-failure'
    $lockedFile.Dispose(); $lockedFile = $null
    Transfer-Invoke RetrySettingsSave
    $null = Activity-WaitState { param($state) $state.settings.autoFindAvailablePortDefault }
    Transfer-Invoke AutoFindDefault
    $null = Activity-WaitState { param($state) -not $state.settings.autoFindAvailablePortDefault }
    $checks.failedSettingsAndRetry = 'Passed (locked state file; original value retained; retry)'

    $null = Transfer-Ui scroll SettingsScroll --to top
    Transfer-Invoke ThemeSelector
    Transfer-Invoke ThemeDark
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 680)
    Transfer-Capture 'settings-500-dark'
    if ((Transfer-Ui search DashboardButton).matchCount -eq 0) { Transfer-Invoke PART_PaneToggleButton }
    Transfer-Invoke DashboardButton
    Transfer-Wait DashboardPage
    $null = Transfer-Ui scroll DashboardScroll --to bottom
    Transfer-Capture 'dashboard-500-dark'
    $recentHeading = (Transfer-Ui search RecentHeading).matches | Where-Object automationId -eq RecentHeading | Select-Object -First 1
    $statsHeading = (Transfer-Ui search StatisticsHeading).matches | Where-Object automationId -eq StatisticsHeading | Select-Object -First 1
    if (-not $recentHeading -or -not $statsHeading) { throw 'The narrow auxiliary section headings were not found.' }
    if ($statsHeading.y -le $recentHeading.y) { throw 'Narrow dashboard auxiliary sections did not stack.' }
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 820)
    $null = Transfer-Ui scroll DashboardScroll --to bottom
    Transfer-Capture 'dashboard-1200-dark'
    $checks.responsiveAuxiliarySections = 'Passed (1200 light/dark, 500 dark; native controls and bounded lists)'

    $sshPath = Join-Path $runPath 'optional-options.config'
    $sshText = (1..9 | ForEach-Object { "Host migrated-$_`n HostName migrated-$_.example`n User developer`n Port 2222`n Compression yes`n ForwardX11 yes`n ForwardX11Trusted yes`n RemoteForward 9000 localhost:9000`n" }) -join "`n"
    Set-Content -LiteralPath $sshPath -Value $sshText -Encoding utf8NoBOM
    $sourceHash = (Get-FileHash -LiteralPath $sshPath).Hash
    Transfer-Preview $sshPath -Ssh
    if ((Transfer-Value ImportSummary) -ne '9 new servers · 0 new profiles · 0 skipped') { throw 'Optional SSH options still blocked valid servers.' }
    if (-not (Transfer-Enabled CommitImport)) { throw 'Valid SSH servers could not be imported.' }
    Transfer-Capture 'ssh-options-ignored-preview'
    Transfer-Invoke CommitImport
    Transfer-Wait ServersPage
    if ((Transfer-ReadState).servers.Count -ne 21) { throw 'The nine valid SSH entries were not imported.' }
    if ((Get-FileHash -LiteralPath $sshPath).Hash -ne $sourceHash) { throw 'Import modified the source SSH config.' }
    $invalidPath = Join-Path $runPath 'invalid-fields.config'
    Set-Content -LiteralPath $invalidPath -Value "Host missing-user`n HostName example.org`n Compression yes" -Encoding utf8NoBOM
    Transfer-Preview $invalidPath -Ssh
    Transfer-Invoke SkipUnavailableImports
    if (Transfer-Enabled CommitImport) { throw 'Skipping every unavailable entry enabled an empty import.' }
    if ((Transfer-Value ImportActionHint) -notlike 'No entries can be imported.*') { throw 'An empty import had no visible explanation.' }
    Transfer-Capture 'import-required-fields-missing'
    $checks.partialSshImportAndEmptyPreviewReason = 'Passed (nine valid entries with ignored options; source unchanged; missing User remains unavailable)'

    Transfer-Close
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for ActiveCount --value '0 active' --timeout 10000
    if ((Transfer-Ui get-property AutoFindLocalPort --property ToggleState).properties.ToggleState -ne 'Off') { throw 'A new Quick draft did not use the saved default.' }
    if ((Transfer-Value TotalForwards) -ne '9,000') { throw 'Counts changed on restart or import.' }
    $restored = Transfer-ReadState
    if ($restored.settings.defaultServerId -ne $sample.servers[2].id -or $restored.recentActivity.Count -ne 200) { throw 'Settings or history did not survive restart.' }
    $checks.restartAndNoAutomaticConnections = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; dataPath = $dataPath; highContrast = 'Not switched globally'; humanGuiReview = 'Pending GUI H' } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'activity-verification.json') -Encoding utf8NoBOM
    Write-Host 'M12 activity, statistics, settings and SSH import verification passed.'
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'activity-verification-failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose() }
    Transfer-Close
}
