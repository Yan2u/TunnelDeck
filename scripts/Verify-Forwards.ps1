[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $NoFixtureBuild,
    [string] $Distribution = 'Ubuntu'
)

. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$dataPath = Join-Path $repoRoot ('.test-output\m7-ui\' + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $repoRoot 'artifacts\m7-review'
$statePath = Join-Path $dataPath 'state.json'
New-Item -ItemType Directory -Path $dataPath, $outputPath -Force | Out-Null
$appProcess = $null
$fixturePath = $null
$portBlock = $null
$localEcho = $null
$checks = [ordered]@{}

function Invoke-ForwardUi {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "UI command failed [$($args -join ' ')]: $raw" }
    return $result
}
function Wait-Ui([string] $selector) { $null = Invoke-ForwardUi wait-for $selector --timeout 15000 }
function Set-Input([string] $selector, [string] $value) { $null = Invoke-ForwardUi set-value $selector $value }
function Press-Enter([string] $selector) {
    [TunnelDeck.Verification.ForwardNative]::Activate($appProcess.MainWindowHandle)
    $null = Invoke-ForwardUi focus $selector
    $null = Invoke-ForwardUi wait-for $selector --property HasKeyboardFocus --value True --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
}
function Assert-Active([int] $count) { $null = Invoke-ForwardUi wait-for ActiveCount --value "$count active" --timeout 15000 }
function Read-State {
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }
    $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream)
    try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do { $state = Read-State; if ($state -and (& $condition $state)) { return $state }; Start-Sleep -Milliseconds 30 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Expected forwarding activity was not persisted.'
}
function Post-Fixture([string] $route, [hashtable] $body) {
    Invoke-RestMethod -Uri "http://$($manifest.host):$($manifest.controlPort)/$route" -Method Post -NoProxy -TimeoutSec 10 `
        -Headers @{ Authorization = 'Bearer ' + $manifest.controlToken } -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)
}
function Unused-RemotePort {
    $port = (Post-Fixture reserve @{ port = 0 }).port
    $null = Post-Fixture release @{ port = $port }
    return [int]$port
}
function Assert-RemoteStopped([int] $port) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while ((Post-Fixture listening @{ port = $port }).listening) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'A stopped remote listener is still reachable.' }
        Start-Sleep -Milliseconds 50
    }
}
function Assert-LocalPayload([int] $port) { $null = [TunnelDeck.Verification.ForwardNative]::AssertPayloadAsync($port, $payload).GetAwaiter().GetResult() }
function Assert-RemotePayload([int] $port) {
    $result = Post-Fixture probe @{ port = $port; payload = [Convert]::ToBase64String($payload) }
    if ($result.payload -ne [Convert]::ToBase64String($payload)) { throw 'Remote forwarding changed the payload.' }
}
function Tunnel-Ids {
    @((Invoke-ForwardUi search 'TunnelDetails_').matches | ForEach-Object { $_.automationId.Substring('TunnelDetails_'.Length) })
}
function Supply-Credential {
    Wait-Ui ForwardCredentialDialog
    # Never print the PasswordBox setter response, command arguments, or disposable password.
    & winapp ui set-value ForwardCredentialPassword $manifest.password -a $appProcess.Id --json *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not set the isolated forwarding credential.' }
    $null = Invoke-ForwardUi invoke PrimaryButton
}
function Capture([string] $name) {
    Start-Sleep -Milliseconds 350
    $null = Invoke-ForwardUi screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function Close-App {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000)) { throw 'The forwarding test app did not finish shutdown.' }
        if ($appProcess.ExitCode -ne 0) { throw 'The forwarding test app exited with an error.' }
    }
}
function Open-App {
    $script:appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Wait-Ui DashboardPage
}

try {
    $fixturePath = & (Join-Path $repoRoot 'test-infra\Start-SshFixture.ps1') -Distribution $Distribution -NoBuild:$NoFixtureBuild
    $manifest = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $portBlock = [TunnelDeck.Verification.ForwardPortBlock]::new(4)
    $basePort = $portBlock.First
    $portBlock.Release(2)
    $portBlock.Release(3)
    $null = Post-Fixture start-echo @{ port = $basePort }
    $localEcho = [TunnelDeck.Verification.ForwardEchoServer]::new()
    $payload = [byte[]]::new(65536)
    [Security.Cryptography.RandomNumberGenerator]::Fill($payload)

    Open-App
    if ((Invoke-ForwardUi get-property ForwardButton --property IsEnabled).properties.IsEnabled -ne 'False') { throw 'An empty workspace enabled Forward.' }
    $null = Invoke-ForwardUi invoke DashboardAddServer
    Wait-Ui ServerDetailPage
    Set-Input ServerDisplayName 'Disposable forwarding server'
    Set-Input ServerHost $manifest.host
    Set-Input ServerPort ([string]$manifest.allowPort)
    Set-Input ServerUsername $manifest.username
    $null = Invoke-ForwardUi invoke SaveServer
    $null = Wait-State { param($state) $state.servers.Count -eq 1 }
    $null = Invoke-ForwardUi invoke DashboardButton
    Wait-Ui QuickServer
    $null = Invoke-ForwardUi focus QuickServer
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x28)
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Invoke-ForwardUi invoke QuickPin
    $null = Wait-State { param($state) $state.settings.pinnedQuickServerId -eq $state.servers[0].id }
    Close-App
    Open-App
    $null = Invoke-ForwardUi wait-for QuickServer --value 'Disposable forwarding server' --contains --timeout 5000
    $null = Invoke-ForwardUi wait-for QuickPortText --property HasKeyboardFocus --value 'True' --timeout 5000
    $checks.emptyAddAndPinnedSelection = 'Passed'

    foreach ($raw in '3000.5', '65536', '1e3', 'invalid') {
        Set-Input QuickPortText ([string]$basePort)
        Set-Input QuickPortText $raw
        Press-Enter QuickPortText
        $null = Invoke-ForwardUi wait-for QuickPortError --value '65535' --contains --timeout 5000
        Assert-Active 0
        if ((Read-State).statistics.totalForwards -ne 0) { throw 'Invalid raw text started a tunnel.' }
    }
    $checks.rawTextAndEnterValidation = 'Passed'

    Set-Input QuickPortText ([string]$basePort)
    Press-Enter QuickPortText
    Supply-Credential
    Wait-Ui HostKeyApproval
    $null = Invoke-ForwardUi wait-for HostKeyFingerprint --value $manifest.hostKeyFingerprint --contains --timeout 5000
    $null = Invoke-ForwardUi invoke PrimaryButton
    Assert-Active 1
    $state = Wait-State { param($state) $state.statistics.totalForwards -eq 1 }
    $started = $state.recentActivity | Where-Object outcome -eq 0 | Select-Object -First 1
    if ($started.requested.localPort -ne $basePort -or $started.actual.local.port -ne ($basePort + 2) -or $started.actual.remote.port -ne $basePort) { throw 'Requested/Actual local allocation was incorrect.' }
    $firstId = @(Tunnel-Ids)[0]
    Assert-LocalPayload ($basePort + 2)
    $null = Invoke-ForwardUi invoke "PortAdjustment_$firstId"
    $null = Invoke-ForwardUi wait-for 'Port adjusted' --timeout 5000
    Capture 'local-port-adjusted'
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    $null = Invoke-ForwardUi invoke "TunnelDetails_$firstId"
    $null = Invoke-ForwardUi wait-for RequestedRoute --value ([string]$basePort) --contains --timeout 5000
    $null = Invoke-ForwardUi wait-for ActualRoute --value ([string]($basePort + 2)) --contains --timeout 5000
    $durationBefore = (Invoke-ForwardUi get-value TunnelDuration).text
    $revisionBefore = (Read-State).revision
    Start-Sleep -Milliseconds 1300
    if ((Invoke-ForwardUi get-value TunnelDuration).text -eq $durationBefore) { throw 'Open details did not update duration.' }
    if ((Read-State).revision -ne $revisionBefore) { throw 'A duration tick wrote configuration.' }
    Capture 'running-details'
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    Press-Enter QuickPortText
    Assert-Active 1
    if (@(Tunnel-Ids).Count -ne 1 -or @(Tunnel-Ids)[0] -ne $firstId -or (Read-State).statistics.totalForwards -ne 1) { throw 'Duplicate submit created another runtime instance.' }
    $checks.localAllocationPayloadDuplicateAndDetails = 'Passed'

    $null = Invoke-ForwardUi invoke Settings
    Wait-Ui SettingsPage
    $null = Invoke-ForwardUi invoke ThemeSelector
    $null = Invoke-ForwardUi invoke ThemeDark
    $null = Invoke-ForwardUi invoke DashboardButton
    $null = Invoke-ForwardUi wait-for QuickPortText --value ([string]$basePort) --timeout 5000
    $null = Invoke-ForwardUi invoke QuickAdvanced
    Wait-Ui QuickLocalPortText
    Set-Input QuickLocalPortText ([string]($basePort + 3))
    Set-Input QuickRemotePortText ([string]$basePort)
    Press-Enter QuickLocalPortText
    Assert-Active 2
    $null = Wait-State { param($state) $state.statistics.totalForwards -eq 2 }
    $secondId = @(Tunnel-Ids | Where-Object { $_ -ne $firstId })[0]
    $null = Invoke-ForwardUi invoke "StopTunnel_$firstId"
    Assert-Active 1
    $null = Wait-State { param($state) $state.recentActivity[0].outcome -eq 1 }
    [TunnelDeck.Verification.ForwardNative]::AssertReusable($basePort + 2)
    Assert-LocalPayload ($basePort + 3)
    $checks.advancedDraftNavigationAndIndependentStop = 'Passed'

    $remotePort = Unused-RemotePort
    $null = Invoke-ForwardUi invoke QuickRemoteMode
    $null = Invoke-ForwardUi wait-for AutoFindLocalPort --gone --timeout 5000
    Set-Input QuickLocalPortText ([string]$localEcho.Port)
    Set-Input QuickRemotePortText ([string]$remotePort)
    Press-Enter QuickRemotePortText
    Supply-Credential
    Assert-Active 2
    $null = Wait-State { param($state) $state.statistics.totalForwards -eq 3 }
    $remoteId = @(Tunnel-Ids | Where-Object { $_ -ne $secondId })[0]
    Assert-RemotePayload $remotePort
    Capture 'mixed-running-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 900, 760)
    Capture 'advanced-900-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 640, 680)
    Capture 'advanced-640-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    $checks.remotePayloadAndAdaptiveUi = 'Passed'

    $refusedPort = [int](Post-Fixture reserve @{ port = 0 }).port
    Set-Input QuickRemotePortText ([string]$refusedPort)
    Press-Enter QuickRemotePortText
    Supply-Credential
    $state = Wait-State { param($state) @($state.recentActivity | Where-Object { $_.outcome -eq 2 -and $_.error.kind -eq 8 }).Count -gt 0 }
    Assert-Active 2
    $failedId = @(Tunnel-Ids | Where-Object { $_ -ne $secondId -and $_ -ne $remoteId })[0]
    Wait-Ui "RetryTunnel_$failedId"
    Capture 'remote-refusal'
    if (@((Invoke-ForwardUi search "PortAdjustment_$failedId").matches).Count -ne 0) { throw 'Remote failure displayed an allocation marker.' }
    $null = Post-Fixture release @{ port = $refusedPort }
    $null = Invoke-ForwardUi invoke "RetryTunnel_$failedId"
    Supply-Credential
    Assert-Active 3
    $null = Wait-State { param($state) $state.statistics.totalForwards -eq 4 }
    Assert-RemotePayload $refusedPort
    $checks.remoteRefusalAndManualRetry = 'Passed'

    $null = Invoke-ForwardUi invoke "StopTunnel_$remoteId"
    Assert-Active 2
    Assert-RemoteStopped $remotePort
    Assert-LocalPayload ($basePort + 3)
    Assert-RemotePayload $refusedPort
    $closed = [Diagnostics.Stopwatch]::StartNew()
    Close-App
    $closed.Stop()
    [TunnelDeck.Verification.ForwardNative]::AssertReusable($basePort + 3)
    Assert-RemoteStopped $refusedPort
    $state = Read-State
    if ($state.statistics.totalForwards -ne 4 -or @($state.recentActivity | Where-Object outcome -eq 1).Count -ne 4) { throw 'Shutdown did not persist the four completed runtime instances.' }
    if ([IO.File]::ReadAllText($statePath).Contains($manifest.password)) { throw 'A disposable password leaked into configuration.' }
    $checks.liveLocalAndRemoteShutdown = 'Passed'
    $checks.shutdownMilliseconds = $closed.ElapsedMilliseconds
    $checks.ephemeralCredentialsAndActivityPersistence = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; dataDirectory = $dataPath; humanGuiReview = 'Pending GUI C' } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'verification.json') -Encoding utf8
    Write-Host 'M7 native forwarding verification passed.'
}
catch {
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'verification-partial.json') -Encoding utf8
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture 'verification-failure' } } catch { }
    throw
}
finally {
    try { Close-App }
    finally {
        if ($localEcho) { $localEcho.Dispose() }
        if ($portBlock) { $portBlock.Dispose() }
        if ($fixturePath) { & (Join-Path $repoRoot 'test-infra\Stop-SshFixture.ps1') -ManifestPath $fixturePath }
    }
}
