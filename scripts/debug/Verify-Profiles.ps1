[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $NoFixtureBuild,
    [string] $Distribution = 'Ubuntu',
    [string] $OutputDirectory,
    [string] $DebuggerPath
)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Native-DebuggerSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output\m8-profiles\' + [Guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts\m8-review' }
$statePath = Join-Path $dataPath 'state.json'
New-Item -ItemType Directory -Path $dataPath, $outputPath -Force | Out-Null
$appProcess = $null
$fixturePath = $null
$ports = $null
$echo = $null
$checks = [ordered]@{}
$credentialPrompts = 0
$hostPrompts = 0
$debuggers = [Collections.Generic.List[Diagnostics.Process]]::new()

function Ui {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "Profile UI command failed [$($args -join ' ')]: $raw" }
    return $result
}
function Wait-Ui([string] $selector) { $null = Ui wait-for $selector --timeout 15000 }
function Set-Input([string] $selector, [string] $value) { $null = Ui set-value $selector $value }
function Id([string] $value) { ([Guid]$value).ToString('N') }
function Read-State {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        try {
            $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        }
        catch [IO.IOException] {
            $code = $_.Exception.GetBaseException().HResult -band 0xffff
            if ($code -notin @(2, 32, 33)) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
    throw 'The state file remained unavailable after the concurrent save.'
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(12)
    do { $state = Read-State; if (& $condition $state) { return $state }; Start-Sleep -Milliseconds 40 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected profile change was not persisted.'
}
function Capture([string] $name) { Start-Sleep -Milliseconds 400; $null = Ui screenshot --output (Join-Path $outputPath ($name + '.png')) }
function Open-App {
    $script:appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Wait-Ui DashboardPage
    if ($DebuggerPath) { $debuggers.Add((Start-NativeCrashCapture $appProcess $DebuggerPath (Join-Path $outputPath ('crash-' + $appProcess.Id)))) }
}
function Close-App {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The profile test app did not close normally.' }
    }
}
function Select-RuleServer([int] $index) {
    $null = Ui focus RuleServer
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x24)
    for ($i = 0; $i -lt $index; $i++) { [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x28) }
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
}
function Complete-Dialogs([string] $summary) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        if ((Ui search ForwardCredentialDialog).matchCount -gt 0) {
            & winapp ui set-value ForwardCredentialPassword $manifest.password -a $appProcess.Id --json *> $null
            if ($LASTEXITCODE -ne 0) { throw 'Could not enter the disposable profile credential.' }
            $script:credentialPrompts++
            $null = Ui invoke PrimaryButton
        }
        elseif ((Ui search HostKeyApproval).matchCount -gt 0) {
            $null = Ui wait-for HostKeyFingerprint --value $manifest.hostKeyFingerprint --contains --timeout 5000
            $script:hostPrompts++
            $null = Ui invoke PrimaryButton
        }
        else {
            $match = (Ui search ProfileBatchSummary).matches | Where-Object automationId -eq 'ProfileBatchSummary' | Select-Object -First 1
            if ($match -and $match.name -eq $summary) { return }
        }
        Start-Sleep -Milliseconds 80
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "The profile did not reach the expected summary: $summary"
}
function Post-Fixture([string] $route, [hashtable] $body) {
    Invoke-RestMethod -Uri "http://$($manifest.host):$($manifest.controlPort)/$route" -NoProxy -Method Post -TimeoutSec 10 -Headers @{ Authorization = 'Bearer ' + $manifest.controlToken } -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)
}
function Assert-Local([int] $port) { $null = [TunnelDeck.Verification.ForwardNative]::AssertPayloadAsync($port, $payload).GetAwaiter().GetResult() }
function Assert-Remote([int] $port) {
    $result = Post-Fixture probe @{ port = $port; payload = [Convert]::ToBase64String($payload) }
    if ($result.payload -ne [Convert]::ToBase64String($payload)) { throw 'Profile remote forwarding changed the payload.' }
}
function Open-Profile {
    $null = Ui invoke AllProfiles
    Wait-Ui ProfilesPage
    $null = Ui invoke ('ProfileRow_' + (Id $profileId))
    Wait-Ui ProfileDetailPage
}
try {
    $fixturePath = & (Join-Path $repoRoot 'scripts\fixtures\Start-SshFixture.ps1') -Distribution $Distribution -NoBuild:$NoFixtureBuild
    $manifest = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $ports = [TunnelDeck.Verification.ForwardPortBlock]::new(3)
    for ($i = 0; $i -lt 3; $i++) { $ports.Release($i) }
    $echo = [TunnelDeck.Verification.ForwardEchoServer]::new()
    $payload = [byte[]]::new(65536)
    [Security.Cryptography.RandomNumberGenerator]::Fill($payload)
    $remotePort = [int](Post-Fixture reserve @{ port = 0 }).port
    $null = Post-Fixture release @{ port = $remotePort }
    $firstServer = [Guid]::NewGuid().ToString()
    $secondServer = [Guid]::NewGuid().ToString()
    $servers = @(
        @{ id = $firstServer; displayName = 'Fixture alpha'; host = $manifest.host; port = $manifest.allowPort; username = $manifest.username; authMethod = 0 },
        @{ id = $secondServer; displayName = 'Fixture beta'; host = $manifest.host; port = $manifest.denyPort; username = $manifest.username; authMethod = 0 }
    )
    [ordered]@{
        schemaVersion = 1; revision = 0; servers = $servers; profiles = @()
        order = @{ pinnedProfileIds = @(); otherProfileIds = @(); pinnedServerIds = @(); otherServerIds = @($firstServer, $secondServer) }
        settings = @{ sidebarMaximumItems = 3; defaultServerId = $firstServer; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true }
        recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8

    Open-App
    $null = Ui invoke AllProfiles
    Wait-Ui EmptyAddResource
    $null = Ui wait-for PinnedHeading --gone --timeout 5000
    $null = Ui invoke AddProfile
    Wait-Ui ProfileDetailPage
    if ((Ui get-property StartProfile --property IsEnabled).properties.IsEnabled -ne 'False') { throw 'An empty unsaved profile enabled Start all.' }
    Set-Input ProfileName 'M8 cross-server'
    $null = Ui invoke AddRule
    Wait-Ui RuleServer
    $null = Ui wait-for RuleServer --value 'Fixture alpha' --contains --timeout 5000
    Set-Input RuleLocalPortText '3000.5'
    Set-Input RuleRemotePortText ([string]$manifest.echoPort)
    $null = Ui invoke ApplyRule
    $null = Ui wait-for RuleLocalPortError --value '65535' --contains --timeout 5000
    Set-Input RuleLocalPortText ([string]$ports.First)
    $null = Ui invoke ApplyRule
    $null = Ui wait-for RuleServer --gone --timeout 5000
    if ((Read-State).profiles.Count -ne 0) { throw 'Applying a draft rule wrote configuration.' }
    $null = Ui invoke AddRule
    $null = Ui wait-for RuleServer --value 'Fixture alpha' --contains --timeout 5000
    Set-Input RuleLocalPortText ([string]($ports.First + 1))
    Set-Input RuleRemotePortText ([string]$manifest.echoPort)
    $null = Ui invoke ApplyRule
    $null = Ui invoke AddRule
    Select-RuleServer 1
    $null = Ui wait-for RuleServer --value 'Fixture beta' --contains --timeout 5000
    $null = Ui invoke RuleRemoteMode
    $null = Ui wait-for RuleAutoFindLocalPort --gone --timeout 5000
    Set-Input RuleLocalPortText ([string]$echo.Port)
    Set-Input RuleRemotePortText ([string]$remotePort)
    Capture 'rule-editor-remote-light'
    $null = Ui invoke ApplyRule
    if ((Ui get-property StartProfile --property IsEnabled).properties.IsEnabled -ne 'False') { throw 'Unsaved profile changes enabled Start all.' }
    $null = Ui invoke SaveProfile
    $state = Wait-State { param($s) $s.profiles.Count -eq 1 -and $s.profiles[0].rules.Count -eq 3 }
    $profileId = $state.profiles[0].id
    $ruleIds = @($state.profiles[0].rules | ForEach-Object { $_.id })
    if ($state.profiles[0].rules[2].autoFindAvailablePort) { throw 'A saved Remote rule retained automatic port search.' }
    $checks.editorDefaultsValidationAndAtomicSave = 'Passed'

    $null = Ui invoke ('MoreRule_' + (Id $ruleIds[0]))
    $null = Ui invoke ('MoveDownRule_' + (Id $ruleIds[0]))
    $null = Ui invoke ('MoreRule_' + (Id $ruleIds[1]))
    $null = Ui invoke ('RemoveRule_' + (Id $ruleIds[1]))
    if ((Read-State).profiles[0].rules.Count -ne 3) { throw 'A rule draft removal was saved without Save.' }
    $null = Ui invoke CancelProfile
    Open-Profile
    if ((Read-State).profiles[0].rules[0].id -ne $ruleIds[0]) { throw 'Cancel did not preserve saved rule order.' }
    $checks.ruleMovesAndRemovalCanBeDiscarded = 'Passed'

    $null = Ui invoke StartProfile
    Complete-Dialogs '2 started · 1 failed'
    $state = Wait-State { param($s) $s.statistics.totalForwards -eq 2 -and @($s.recentActivity | Where-Object outcome -eq 2).Count -eq 1 -and $s.statistics.profileUses.PSObject.Properties[$profileId].Value -eq 1 }
    if ($credentialPrompts -ne 2 -or $hostPrompts -ne 2) { throw 'The batch did not share credential input by server or verify both host endpoints.' }
    Assert-Local $ports.First
    Assert-Local ($ports.First + 1)
    Capture 'profile-partial-success'
    $checks.crossServerPartialSuccessAndScopedCredentials = 'Passed'

    $null = Ui invoke AllServers
    $null = Ui invoke ('ServerRow_' + (Id $secondServer))
    Wait-Ui ServerDetailPage
    Set-Input ServerPort ([string]$manifest.allowPort)
    $null = Ui invoke SaveServer
    $null = Wait-State { param($s) ($s.servers | Where-Object id -eq $secondServer).port -eq $manifest.allowPort }
    Open-Profile
    $null = Ui invoke RetryProfileFailed
    Complete-Dialogs '3 started'
    Assert-Local $ports.First
    Assert-Local ($ports.First + 1)
    Assert-Remote $remotePort
    $null = Wait-State { param($s) $s.statistics.totalForwards -eq 3 -and $s.statistics.profileUses.PSObject.Properties[$profileId].Value -eq 2 }
    $null = Ui invoke StartProfile
    Complete-Dialogs '3 already running'
    if ((Read-State).statistics.totalForwards -ne 3 -or $credentialPrompts -ne 3) { throw 'An already-running batch started or prompted again.' }
    Capture 'profile-running-light'
    $checks.retryFailedAndRepeatedStart = 'Passed'

    Set-Input ProfileName 'M8 saved snapshot'
    $null = Ui invoke ('EditRule_' + (Id $ruleIds[0]))
    Wait-Ui RuleServer
    Set-Input RuleLocalPortText ([string]($ports.First + 2))
    $null = Ui invoke ApplyRule
    $null = Ui invoke SaveProfile
    $null = Wait-State { param($s) $s.profiles[0].name -eq 'M8 saved snapshot' -and $s.profiles[0].rules[0].localPort -eq ($ports.First + 2) }
    Assert-Local $ports.First
    if (@((Read-State).recentActivity | Where-Object { $_.outcome -eq 0 -and $_.source.profileName -ne 'M8 cross-server' }).Count -ne 0) { throw 'Editing a profile changed historical source snapshots.' }
    $null = Ui invoke ('StopRule_' + (Id $ruleIds[1]))
    $null = Ui wait-for ('StartRule_' + (Id $ruleIds[1])) --property IsEnabled --value True --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First + 1)
    Assert-Local $ports.First
    Assert-Remote $remotePort
    $checks.savedEditsDoNotChangeRunningSnapshotsAndIndependentStop = 'Passed'

    $null = Ui invoke Settings
    $null = Ui invoke ThemeSelector
    $null = Ui invoke ThemeDark
    Open-Profile
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 900, 760)
    Capture 'profile-900-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 650)
    Capture 'profile-500-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    $null = Ui invoke ('EditRule_' + (Id $ruleIds[2]))
    Wait-Ui RuleServer
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 640, 760)
    Capture 'rule-editor-640-dark'
    $null = Ui invoke CancelRule
    $checks.lightDarkAndAdaptiveProfileUi = 'Passed'

    Close-App
    [TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First)
    if ((Post-Fixture listening @{ port = $remotePort }).listening) { throw 'Closing the profile page retained a remote listener.' }
    $state = Read-State
    if (@($state.recentActivity | Where-Object outcome -eq 1).Count -ne 3) { throw 'Profile shutdown did not save three normal stops.' }
    if ([IO.File]::ReadAllText($statePath).Contains($manifest.password)) { throw 'Profile credentials leaked into configuration.' }
    $beforeRestart = $state.revision
    Open-App
    Open-Profile
    $null = Ui wait-for ProfileName --value 'M8 saved snapshot' --timeout 5000
    if ((Read-State).revision -ne $beforeRestart) { throw 'Reopening a saved profile changed its persisted state.' }
    Close-App
    $checks.shutdownPersistenceAndNoAutomaticResume = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; dataDirectory = $dataPath; humanGuiReview = 'Tracked separately in docs/ui-refresh-validation.md' } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $outputPath 'profiles-verification.json') -Encoding utf8
    Write-Host 'M8 native profile verification passed.'
}
catch {
    Write-Warning $_.ScriptStackTrace
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'profiles-partial.json') -Encoding utf8
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture 'profiles-failure' } } catch { }
    throw
}
finally {
    try { Close-App }
    finally {
        if ($echo) { $echo.Dispose() }
        if ($ports) { $ports.Dispose() }
        if ($fixturePath) { & (Join-Path $repoRoot 'scripts\fixtures\Stop-SshFixture.ps1') -ManifestPath $fixturePath }
        foreach ($debugger in $debuggers) { Stop-NativeCrashCapture $debugger }
    }
}
