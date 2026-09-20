# Dot-sourced by Verify-Deletions.ps1 after its layout-only scenarios have closed.
$fixturePath = $null
$ports = $null
$echo = $null
$lockedFile = $null
function Post-Fixture([string] $route, [hashtable] $body) {
    Invoke-RestMethod -Uri "http://$($manifest.host):$($manifest.controlPort)/$route" -NoProxy -Method Post -TimeoutSec 10 -Headers @{ Authorization = 'Bearer ' + $manifest.controlToken } -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)
}
function Assert-Local([int] $port) { $null = [TunnelDeck.Verification.ForwardNative]::AssertPayloadAsync($port, $payload).GetAwaiter().GetResult() }
function Assert-Remote([int] $port) {
    $result = Post-Fixture probe @{ port = $port; payload = [Convert]::ToBase64String($payload) }
    if ($result.payload -ne [Convert]::ToBase64String($payload)) { throw 'Deletion changed an unaffected remote payload.' }
}
function Assert-RemoteStopped([int] $port) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while ((Post-Fixture listening @{ port = $port }).listening) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'The deleted server still has a remote listener.' }
        Start-Sleep -Milliseconds 50
    }
}
function Complete-Dialogs([string] $summary) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        if (Find-Ui ForwardCredentialDialog) {
            # Never echo the disposable PasswordBox input or its setter response.
            & winapp ui set-value ForwardCredentialPassword $manifest.password -a $appProcess.Id --json *> $null
            if ($LASTEXITCODE -ne 0) { throw 'Could not enter the isolated deletion-test credential.' }
            $null = Ui invoke PrimaryButton
        }
        elseif (Find-Ui HostKeyApproval) {
            $null = Ui wait-for HostKeyFingerprint --value $manifest.hostKeyFingerprint --contains --timeout 5000
            $null = Ui invoke PrimaryButton
        }
        else {
            $result = Find-Ui ProfileBatchSummary
            if ($result -and $result.name -eq $summary) { return }
        }
        Start-Sleep -Milliseconds 80
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "The deletion fixture did not reach the expected profile summary: $summary"
}
try {
    $fixturePath = & (Join-Path $PSScriptRoot '../test-infra/Start-SshFixture.ps1') -Distribution $Distribution -NoBuild:$NoFixtureBuild
    $manifest = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $ports = [TunnelDeck.Verification.ForwardPortBlock]::new(3)
    for ($index = 0; $index -lt 3; $index++) { $ports.Release($index) }
    $echo = [TunnelDeck.Verification.ForwardEchoServer]::new()
    $payload = [byte[]]::new(65536)
    [Security.Cryptography.RandomNumberGenerator]::Fill($payload)
    $remotePort = [int](Post-Fixture reserve @{ port = 0 }).port
    $null = Post-Fixture release @{ port = $remotePort }
    $firstServer = [Guid]::NewGuid().ToString()
    $secondServer = [Guid]::NewGuid().ToString()
    $firstProfile = [Guid]::NewGuid().ToString()
    $secondProfile = [Guid]::NewGuid().ToString()
    $servers = @(
        @{ id = $firstServer; displayName = 'Deletion fixture alpha'; host = $manifest.host; port = $manifest.allowPort; username = $manifest.username; authMethod = 0 }
        @{ id = $secondServer; displayName = 'Deletion fixture beta'; host = $manifest.host; port = $manifest.allowPort; username = $manifest.username; authMethod = 0 }
    )
    $profiles = @(
        @{ id = $firstProfile; name = 'M9 live source snapshot'; rules = @((New-Rule $firstServer $ports.First $manifest.echoPort), (New-Rule $secondServer $echo.Port $remotePort 1)) }
        @{ id = $secondProfile; name = 'M9 retained empty profile'; rules = @((New-Rule $firstServer ($ports.First + 1) $manifest.echoPort)) }
    )
    New-TestState $servers $profiles 'forwarding'
    $forwardingDataPath = $dataPath
    Open-App
    Open-Resource Profile $firstProfile
    Invoke-Ready StartProfile
    Complete-Dialogs '2 started'
    Open-Resource Profile $secondProfile
    Invoke-Ready StartProfile
    Complete-Dialogs '1 started'
    $null = Ui invoke DashboardButton
    Wait-Ui DashboardPage
    $null = Ui invoke QuickAdvanced
    $null = Ui set-value QuickLocalPortText ([string]($ports.First + 2))
    $null = Ui set-value QuickRemotePortText ([string]$manifest.echoPort)
    Invoke-Ready ForwardButton
    $null = Ui wait-for ActiveCount --value '4 active' --timeout 15000
    $state = Wait-State { param($s) $s.statistics.totalForwards -eq 4 -and @($s.statistics.profileUses.PSObject.Properties).Count -eq 2 }
    for ($index = 0; $index -lt 3; $index++) { Assert-Local ($ports.First + $index) }
    Assert-Remote $remotePort
    $checks.realCrossServerAndQuickPayload = 'Passed'

    Open-Resource Profile $firstProfile
    $null = Ui invoke MoreButton
    Invoke-Ready DeleteProfile
    Wait-Ui DeletionConfirmation
    $null = Ui wait-for DeletionRuntimeSummary --value '2 existing forwards will keep running.' --timeout 5000
    Capture 'delete-live-profile-light'
    Confirm-Deletion
    $state = Wait-State { param($s) $s.profiles.Count -eq 1 }
    if ($state.servers.Count -ne 2) { throw 'Deleting the live profile also deleted servers.' }
    Wait-Ui ProfilesPage
    $null = Ui invoke DashboardButton
    $null = Ui wait-for ActiveCount --value '4 active' --timeout 5000
    $sourceDetails = (Ui search TunnelDetails_).matches | Where-Object { $_.name -match ('(?<!\d)' + $ports.First + '(?!\d)') } | Select-Object -First 1
    if (-not $sourceDetails) { throw 'The first profile tunnel has no reachable details button.' }
    $null = Ui invoke $sourceDetails.automationId
    $null = Ui wait-for TunnelSource --value 'M9 live source snapshot' --timeout 5000
    Assert-Absent OpenTunnelProfile
    Capture 'deleted-profile-source-details'
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    for ($index = 0; $index -lt 3; $index++) { Assert-Local ($ports.First + $index) }
    Assert-Remote $remotePort
    $checks.profileDeletionPreservesPayloadAndSource = 'Passed'

    Open-SingleDeletion Server $firstServer
    $null = Ui wait-for DeletionRuntimeSummary --value '3 forwards will be stopped.' --timeout 5000
    $null = Ui wait-for DeletionRulesSummary --value '1 forwarding rule in 1 profile will be removed.' --timeout 5000
    Capture 'delete-live-server-impact-light'
    $savedBeforeFailure = (Get-FileHash -LiteralPath $statePath).Hash
    $lockedFile = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    Confirm-Deletion
    Wait-Ui RetryDeletion
    $errorTree = Ui inspect EditError --depth 3
    $errorMessage = $errorTree.windows[0].elements[0].children | Where-Object automationId -eq 'Message' | Select-Object -First 1
    if (-not $errorMessage.name.StartsWith('Nothing was deleted. 3 forwards stopped')) { throw 'The deletion failure did not explain which forwards had stopped.' }
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $savedBeforeFailure) { throw 'A failed deletion replaced the original JSON.' }
    $state = Read-State
    if ($state.servers.Count -ne 2 -or $state.profiles[0].rules.Count -ne 1 -or $state.settings.defaultServerId -ne $firstServer -or $state.settings.pinnedQuickServerId -ne $firstServer) {
        throw 'A failed deletion damaged the saved resource references.'
    }
    for ($index = 0; $index -lt 3; $index++) { [TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First + $index) }
    Assert-Remote $remotePort
    Capture 'deletion-save-failed-light'
    $lockedFile.Dispose(); $lockedFile = $null
    $checks.failedSaveKeepsConfigurationAndReportsStoppedForwards = 'Passed'

    Invoke-Ready RetryDeletion
    Wait-Ui DeletionConfirmation
    Assert-Absent DeletionRuntimeSummary
    Confirm-Deletion
    $state = Wait-State { param($s) $s.servers.Count -eq 1 -and $s.profiles[0].rules.Count -eq 0 }
    if ($state.profiles[0].id -ne $secondProfile -or $state.settings.defaultServerId -or $state.settings.pinnedQuickServerId) { throw 'Retry lost the empty profile or retained deleted selections.' }
    if (@($state.order.pinnedServerIds + $state.order.otherServerIds | Where-Object { $_ -eq $firstServer }).Count -ne 0) { throw 'Deleted server remained in resource ordering.' }
    Assert-Absent ('SidebarServer_' + (Id $firstServer))
    Search-For 'Deletion fixture alpha'
    Wait-Ui SearchNoResults
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $null = Ui invoke DashboardButton
    $null = Ui wait-for ForwardButton --property IsEnabled --value False --timeout 5000
    $null = Ui wait-for ActiveCount --value '1 active' --timeout 5000
    Assert-Remote $remotePort
    if ((Read-State).statistics.totalForwards -ne 4) { throw 'Retry restarted a stopped tunnel.' }
    $checks.retryClearsSelectionsKeepsEmptyProfilesAndOtherTraffic = 'Passed'

    Open-BulkDeletion Profile
    Confirm-Deletion
    $state = Wait-State { param($s) $s.profiles.Count -eq 0 }
    if ($state.servers.Count -ne 1) { throw 'Delete all profiles removed a server.' }
    Assert-Remote $remotePort
    Open-BulkDeletion Server
    $null = Ui wait-for DeletionRuntimeSummary --value '1 forward will be stopped.' --timeout 5000
    Assert-Absent DeletionRulesSummary
    Confirm-Deletion
    $state = Wait-State { param($s) $s.servers.Count -eq 0 -and @($s.recentActivity | Where-Object outcome -eq 1).Count -eq 4 }
    Assert-RemoteStopped $remotePort
    if (@($state.recentActivity | Where-Object outcome -eq 0).Count -ne 4 -or $state.statistics.totalForwards -ne 4) { throw 'Deletion duplicated or lost successful start history.' }
    if ([IO.File]::ReadAllText($statePath).Contains($manifest.password)) { throw 'Disposable credentials leaked into persisted state.' }
    $null = Ui invoke DashboardButton
    $null = Ui wait-for ActiveCount --value '0 active' --timeout 5000
    Assert-Absent TunnelDetails_
    Capture 'delete-all-complete-light'
    $checks.bulkDeleteReleasesListenersAndPreservesHistory = 'Passed'
    $revision = $state.revision
    Close-App
    Open-App
    $state = Read-State
    if ($state.revision -ne $revision -or $state.servers.Count -ne 0 -or $state.profiles.Count -ne 0) { throw 'Deleted resources returned after restarting.' }
    Close-App
    $checks.restartPreservesDeletion = 'Passed'
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture 'deletion-forwarding-failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose() }
    try { Close-App }
    finally {
        if ($echo) { $echo.Dispose() }
        if ($ports) { $ports.Dispose() }
        if ($fixturePath) { & (Join-Path $PSScriptRoot '../test-infra/Stop-SshFixture.ps1') -ManifestPath $fixturePath }
    }
}
