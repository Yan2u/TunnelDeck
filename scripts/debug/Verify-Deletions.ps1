[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $LayoutOnly,
    [switch] $NoFixtureBuild,
    [string] $Distribution = 'Ubuntu',
    [string] $OutputDirectory
)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Deletion-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m9-review' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$appProcess = $null
$checks = [ordered]@{}
$layoutDataPath = $null
$forwardingDataPath = $null
$verificationError = $null

try {
    $firstServer = [Guid]::NewGuid().ToString()
    $secondServer = [Guid]::NewGuid().ToString()
    $unusedServer = [Guid]::NewGuid().ToString()
    $servers = @(
        @{ id = $firstServer; displayName = 'Shared server name'; host = 'first.example'; port = 22; username = 'dev'; authMethod = 0 }
        @{ id = $secondServer; displayName = 'Shared server name'; host = 'second.example'; port = 22; username = 'dev'; authMethod = 0 }
        @{ id = $unusedServer; displayName = 'Unused deletion example'; host = 'unused.example'; port = 22; username = 'dev'; authMethod = 0 }
    )
    $firstProfile = [Guid]::NewGuid().ToString()
    $secondProfile = [Guid]::NewGuid().ToString()
    $emptyProfile = [Guid]::NewGuid().ToString()
    $profiles = @(
        @{ id = $firstProfile; name = 'Shared profile name'; rules = @((New-Rule $firstServer 3100 3100), (New-Rule $secondServer 3200 3200)) }
        @{ id = $secondProfile; name = 'Shared profile name'; rules = @(1..24 | ForEach-Object { New-Rule $firstServer (4100 + $_) (5100 + $_) }) }
        @{ id = $emptyProfile; name = 'Empty deletion example'; rules = @() }
    )
    New-TestState $servers $profiles 'layout'
    $layoutDataPath = $dataPath
    Open-App
    Set-Theme Light
    $null = Ui invoke AllServers
    Wait-Ui ServersPage
    Assert-Absent DeleteAllServers
    $before = (Get-FileHash -LiteralPath $statePath).Hash
    Open-SingleDeletion Server $unusedServer
    foreach ($section in 'DeletionRuntimeSummary', 'DeletionPendingSummary', 'DeletionTestsSummary', 'DeletionRulesSummary', 'DeletionRuleDetails') { Assert-Absent $section }
    $null = Ui wait-for CloseButton --property HasKeyboardFocus --value True --timeout 5000
    Capture 'delete-zero-light'
    Cancel-Deletion 0x0d
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $before) { throw 'Enter on the default Cancel changed configuration.' }
    $checks.zeroSectionsAndDefaultCancel = 'Passed'

    Open-SingleDeletion Server $firstServer
    $null = Ui wait-for DeletionResourceSummary --value 'dev@first.example:22' --contains --timeout 5000
    $null = Ui wait-for DeletionRulesSummary --value '25 forwarding rules in 2 profiles will be removed.' --timeout 5000
    Assert-Absent ('Shared profile name · ' + (Id $firstProfile).Substring(0, 8))
    $null = Ui invoke DeletionRuleDetails
    Wait-Ui ('Shared profile name · ' + (Id $firstProfile).Substring(0, 8))
    Wait-Ui ('Shared profile name · ' + (Id $secondProfile).Substring(0, 8))
    Capture 'delete-grouped-rules-light'
    $buttonBeforeScroll = Assert-Visible CloseButton
    $null = Ui scroll DeletionBody --to bottom
    $buttonAfterScroll = Assert-Visible CloseButton
    if ($buttonBeforeScroll.y -ne $buttonAfterScroll.y) { throw 'Scrolling the deletion details moved its action buttons.' }
    Capture 'delete-grouped-rules-bottom-light'
    Cancel-Deletion
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $before) { throw 'Inspecting and cancelling the long impact changed configuration.' }
    $checks.groupedLongRulesAndStationaryActions = 'Passed'

    Set-Theme Dark
    Open-SingleDeletion Server $firstServer
    foreach ($size in @(@(900, 760), @(640, 650), @(500, 500))) {
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, $size[0], $size[1])
        Assert-DialogButtons
        Capture ('delete-' + $size[0] + '-dark')
    }
    $null = Ui invoke DeletionRuleDetails
    $null = Ui scroll DeletionBody --to bottom
    Assert-DialogButtons
    Capture 'delete-500-expanded-dark'
    Cancel-Deletion
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    Set-Theme Light
    $checks.darkAndAdaptiveDialog = 'Passed'

    Open-BulkDeletion Server
    $null = Ui wait-for DeletionResourceSummary --value '3 servers will be deleted.' --timeout 5000
    $null = Ui wait-for DeletionRulesSummary --value '26 forwarding rules in 2 profiles will be removed.' --timeout 5000
    Assert-Absent DeletionRuleDetails
    Capture 'delete-all-servers-summary-light'
    Cancel-Deletion
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $before) { throw 'Cancelling bulk deletion changed configuration.' }
    $checks.bulkDeduplicatedSummaryAndCancel = 'Passed'

    Open-Resource Server $unusedServer
    $null = Ui invoke MoreButton
    Invoke-Ready DeleteServer
    Wait-Ui DeletionConfirmation
    Confirm-Deletion
    $null = Wait-State { param($s) $s.servers.Count -eq 2 }
    Wait-Ui ServersPage
    Search-For 'Unused deletion example'
    Wait-Ui SearchNoResults
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $checks.singleServerDetailDeletionAndSearchCleanup = 'Passed'

    Open-Resource Profile $emptyProfile
    $null = Ui set-value ProfileName 'Unsaved deletion example'
    $null = Ui invoke MoreButton
    Invoke-Ready DeleteProfile
    Wait-Ui DeletionConfirmation
    $null = Ui wait-for DeletionResourceSummary --value 'unsaved edits will be discarded' --contains --timeout 5000
    Cancel-Deletion
    $null = Ui wait-for ProfileName --value 'Unsaved deletion example' --timeout 5000
    $null = Ui invoke MoreButton
    Invoke-Ready DeleteProfile
    Wait-Ui DeletionConfirmation
    Confirm-Deletion
    $null = Wait-State { param($s) $s.profiles.Count -eq 2 }
    Wait-Ui ProfilesPage
    $checks.profileDetailCancelKeepsDraftAndDeleteNavigates = 'Passed'

    Open-BulkDeletion Profile
    $null = Ui wait-for DeletionResourceSummary --value '2 profiles will be deleted.' --timeout 5000
    $null = Ui wait-for DeletionKeptServers --value 'Servers are kept.' --timeout 5000
    Assert-Absent DeletionRuleDetails
    Capture 'delete-all-profiles-light'
    Confirm-Deletion
    $state = Wait-State { param($s) $s.profiles.Count -eq 0 }
    if ($state.servers.Count -ne 2 -or $state.order.pinnedProfileIds.Count + $state.order.otherProfileIds.Count -ne 0) { throw 'Bulk profile deletion damaged servers or left order references.' }
    Wait-Ui EmptyAddResource
    $null = Ui invoke MoreButton
    $null = Ui wait-for DeleteAllProfiles --property IsEnabled --value False --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $checks.bulkProfileDeletionAndEmptyCommands = 'Passed'

    $beforeClose = (Get-FileHash -LiteralPath $statePath).Hash
    Open-BulkDeletion Server
    Close-App
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $beforeClose) { throw 'Closing during confirmation performed a deletion.' }
    Open-App
    if ((Read-State).servers.Count -ne 2) { throw 'Closing confirmation changed the saved resource count.' }
    Close-App
    $checks.closeDuringConfirmationHasNoSideEffects = 'Passed'

    if (-not $LayoutOnly) { . (Join-Path $PSScriptRoot 'Deletion-ForwardingChecks.ps1') }
    [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks
        layoutDataDirectory = $layoutDataPath; forwardingDataDirectory = $forwardingDataPath
        highContrast = 'Not switched globally'; humanGuiReview = 'Pending GUI E'
    } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $outputPath 'deletions-verification.json') -Encoding utf8
    Write-Host 'M9 native deletion verification passed.'
}
catch {
    $verificationError = $_
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $outputPath 'deletions-error.txt') -Encoding utf8
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'deletions-partial.json') -Encoding utf8
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture 'deletions-failure' } } catch { }
    throw
}
finally {
    try { Close-App }
    catch { if ($null -eq $verificationError) { throw }; Write-Warning $_.Exception.Message }
}
