# Dot-sourced by Verify-Lifecycle.ps1 after the instance checks; requires $manifest from a running SSH fixture.
# Fault injection stays with the agent: the sshd restart and file lock below are scripted, the user only reviews presentation.
$payload = [byte[]]::new(4096)
[Security.Cryptography.RandomNumberGenerator]::Fill($payload)
function Life-Local([int] $port) { $null = [TunnelDeck.Verification.ForwardNative]::AssertPayloadAsync($port, $payload).GetAwaiter().GetResult() }
function Life-Remote([int] $port) {
    $result = Life-Post probe @{ port = $port; payload = [Convert]::ToBase64String($payload) }
    if ($result.payload -ne [Convert]::ToBase64String($payload)) { throw 'The recovered remote forward changed the payload.' }
}
function Life-Reconnecting([string] $tunnelId) { $null = Life-Ui wait-for ('Reconnect_' + $tunnelId) --timeout 15000 }
function Life-ReconnectText([string] $tunnelId) { (Life-Ui get-value ('Reconnect_' + $tunnelId)).text }
function Life-DismissFlyout {
    # Light dismiss by clicking the page heading; keyboard input is reserved for the shutdown dialog checks.
    $null = Life-Ui click DashboardPage
    $null = Life-Ui wait-for ActualRoute --gone --timeout 5000
}
function Life-Find([string] $selector) { (Life-Ui search $selector).matches | Where-Object automationId -eq $selector | Select-Object -First 1 }
function Life-WaitStopped([string] $tunnelId) {
    # A stopped row is removed once its stop record is saved; a row that remains must read Stopped.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $row = Life-Find ('Tunnel_' + $tunnelId)
        if (-not $row -or $row.name -match 'Stopped') { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The stopped tunnel row neither cleared nor reported Stopped.'
}

$ports = [TunnelDeck.Verification.ForwardPortBlock]::new(2)
$ports.Release(0); $ports.Release(1)
$echo = [TunnelDeck.Verification.ForwardEchoServer]::new()
$remotePort = Life-RemotePort
$server = New-LifeServer
$server.port = $manifest.mutablePort
$rules = @((New-LifeRule $server.id $ports.First $manifest.echoPort), (New-LifeRule $server.id $echo.Port $remotePort 1))
New-LifeState @($server) $rules 'reconnect'
Life-Open
Life-StartProfile -ApproveTrust
$null = Life-Ui wait-for ProfileBatchSummary --value '2 started' --timeout 30000
Life-Dashboard 2
Life-Local $ports.First
Life-Remote $remotePort
$tunnelIds = Life-TunnelIds
if ($tunnelIds.Count -ne 2) { throw 'Two lifecycle forwards were expected on the dashboard.' }
$checks.realProfileForwardsBeforeFault = 'Passed'

# Fault 1: terminate the sshd service and its client sessions. Recovery must wait for the listener to return.
$null = Life-Post stop @{ server = 'mutable' }
foreach ($id in $tunnelIds) { Life-Reconnecting $id }
$null = Life-Ui wait-for ActiveCount --value '0 active' --timeout 15000
$reconnectText = Life-ReconnectText $tunnelIds[0]
if ($reconnectText -notmatch 'Retry in \d+ s · attempt \d+' -and $reconnectText -notmatch 'Restoring the forward' -and $reconnectText -notmatch 'Preparing to reconnect') { throw "Unexpected reconnect message: $reconnectText" }
Life-Capture 'reconnecting-light'
$null = Life-Ui invoke ('TunnelDetails_' + $tunnelIds[0])
$null = Life-Ui wait-for ActualRoute --timeout 5000
$null = Life-Ui wait-for 'Previous listener' --timeout 5000
Life-Capture 'reconnecting-details-light'
Life-DismissFlyout
$stopTunnel = Life-Find ('StopTunnel_' + $tunnelIds[0])
if (-not $stopTunnel -or $stopTunnel.isEnabled -eq $false) { throw 'Stop must remain available while a forward is reconnecting.' }
$revisionDuringBackoff = (Life-ReadState).revision
$laterText = $reconnectText
for ($tick = 0; $tick -lt 4 -and $laterText -eq $reconnectText; $tick++) { Start-Sleep -Milliseconds 800; $laterText = Life-ReconnectText $tunnelIds[0] }
if ($laterText -eq $reconnectText) { throw 'The reconnect countdown did not update.' }
if ((Life-ReadState).revision -ne $revisionDuringBackoff) { throw 'Reconnect backoff wrote configuration.' }
$checks.reconnectPresentationCountdownAndStop = 'Passed'

# Fault 1 recovery: the listener returns and both forwards resume with the same rows and Actual ports.
$null = Life-Post restart @{ server = 'mutable' }
$null = Life-Ui wait-for ActiveCount --value '2 active' --timeout 60000
foreach ($id in $tunnelIds) { $null = Life-Ui wait-for ('Reconnect_' + $id) --gone --timeout 15000 }
$afterIds = Life-TunnelIds
if (@(Compare-Object $tunnelIds $afterIds).Count -ne 0) { throw 'Recovery replaced the runtime rows instead of restoring them.' }
Life-Local $ports.First
Life-Remote $remotePort
$null = Life-Ui invoke ('TunnelDetails_' + $tunnelIds[0])
$null = Life-Ui wait-for TunnelReconnectSummary --value 'Automatic recoveries: 1.' --contains --timeout 5000
Life-Capture 'recovered-details-light'
Life-DismissFlyout
$state = Life-ReadState
if ($state.statistics.totalForwards -ne 2) { throw 'Automatic recovery counted as a new forward.' }
if (@($state.recentActivity | Where-Object outcome -eq 2).Count -ne 0) { throw 'A recovered forward recorded a failure.' }
$checks.recoveryRestoresRowsPayloadAndCounters = 'Passed'

# Fault 2: Stop during backoff must cancel recovery for that row only; the other row still recovers.
$null = Life-Post stop @{ server = 'mutable' }
foreach ($id in $tunnelIds) { Life-Reconnecting $id }
Life-Invoke ('StopTunnel_' + $tunnelIds[0])
$null = Life-Ui wait-for ('Reconnect_' + $tunnelIds[0]) --gone --timeout 15000
Life-WaitStopped $tunnelIds[0]
$null = Life-WaitState { param($s) @($s.recentActivity | Where-Object outcome -eq 1).Count -eq 1 }
[TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First)
$null = Life-Post restart @{ server = 'mutable' }
$null = Life-Ui wait-for ActiveCount --value '1 active' --timeout 60000
$null = Life-Ui wait-for ('Reconnect_' + $tunnelIds[1]) --gone --timeout 15000
Start-Sleep -Milliseconds 1500
$null = Life-Ui wait-for ActiveCount --value '1 active' --timeout 5000
[TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First)
Life-Remote $remotePort
Life-Capture 'stopped-during-backoff-light'
$checks.stopDuringBackoffPreventsRevival = 'Passed'

# Closing while activity cannot be saved: the shutdown dialog offers Retry and the explicit exit; Retry after the fault clears completes normally.
Life-Theme ThemeDark
$lockedFile = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$null = $appProcess.CloseMainWindow()
$null = Life-Ui wait-for ShutdownConfirmation --timeout 30000
$failureText = (Life-Ui get-value ShutdownFailureMessage).text
if ($failureText -notmatch 'saved|saving') { throw "The shutdown dialog did not explain the saving problem: $failureText" }
$null = Life-Visible PrimaryButton
$null = Life-Visible SecondaryButton
Life-Capture 'closing-save-failed-dark'
$stateHashLocked = (Get-FileHash -LiteralPath $statePath).Hash
[TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
$null = Life-Ui wait-for ShutdownConfirmation --gone --timeout 5000
$null = Life-Ui wait-for RuntimeStatus --value 'Closing paused' --timeout 5000
if ($appProcess.HasExited) { throw 'Escape on the shutdown dialog exited the app.' }
if ((Get-FileHash -LiteralPath $statePath).Hash -ne $stateHashLocked) { throw 'A failed shutdown save replaced the configuration.' }
Life-Capture 'closing-paused-dark'
$null = $appProcess.CloseMainWindow()
$null = Life-Ui wait-for ShutdownConfirmation --timeout 30000
$lockedFile.Dispose(); $lockedFile = $null
Life-Invoke PrimaryButton
Life-WaitExit
$state = Life-ReadState
if (@($state.recentActivity | Where-Object outcome -eq 1).Count -lt 2) { throw 'Retry did not save the stop history before exiting.' }
[TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First)
Life-RemoteStopped $remotePort
$checks.closingSaveFailureEscapeAndRetry = 'Passed'

# Explicit exit without saving: the app must exit even though the file stays locked, and the old JSON stays intact.
New-LifeState @($server) @((New-LifeRule $server.id ($ports.First + 1) $manifest.echoPort)) 'exit-now'
Life-Open
Life-StartProfile -ApproveTrust
$null = Life-Ui wait-for ProfileBatchSummary --value '1 started' --timeout 30000
Life-Dashboard 1
Life-Local ($ports.First + 1)
$lockedFile = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$lockedHash = (Get-FileHash -LiteralPath $statePath).Hash
$null = $appProcess.CloseMainWindow()
$null = Life-Ui wait-for ShutdownConfirmation --timeout 30000
Life-Invoke SecondaryButton
Life-WaitExit
$lockedFile.Dispose(); $lockedFile = $null
if ((Get-FileHash -LiteralPath $statePath).Hash -ne $lockedHash) { throw 'Exit without saving changed the configuration.' }
[TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First + 1)
$checks.explicitExitWithoutSavingReleasesListeners = 'Passed'

# Repeated close requests during a normal shutdown coalesce into one bounded exit.
New-LifeState @($server) @((New-LifeRule $server.id ($ports.First + 1) $manifest.echoPort)) 'repeat-close'
Life-Open
Life-StartProfile -ApproveTrust
$null = Life-Ui wait-for ProfileBatchSummary --value '1 started' --timeout 30000
Life-Dashboard 1
$closeStarted = [DateTimeOffset]::UtcNow
for ($attempt = 0; $attempt -lt 3; $attempt++) { $null = $appProcess.CloseMainWindow(); Start-Sleep -Milliseconds 60 }
Life-WaitExit
$closeSeconds = ([DateTimeOffset]::UtcNow - $closeStarted).TotalSeconds
if ($closeSeconds -gt 10) { throw "Normal close exceeded its budget: $closeSeconds s." }
$state = Life-ReadState
if (@($state.recentActivity | Where-Object outcome -eq 1).Count -ne 1 -or @($state.recentActivity | Where-Object outcome -eq 0).Count -ne 1) { throw 'Repeated close requests duplicated or lost activity.' }
[TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First + 1)
$checks.repeatedCloseCoalescesAndReleases = "Passed; exited in $([math]::Round($closeSeconds, 1)) s"
