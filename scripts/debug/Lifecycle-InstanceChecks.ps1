New-LifeState -scenario 'single-instance'
Life-Open
$primaryPath = $dataPath
$primaryState = $statePath
$primary = $appProcess
$before = (Get-FileHash -LiteralPath $statePath).Hash
$lock = [IO.File]::Open((Join-Path $dataPath 'configuration.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    Life-Invoke Minimize
    if (-not [TunnelDeck.Verification.LifecycleNative]::IsIconic($primary.MainWindowHandle)) { throw 'Primary was not minimized for activation verification.' }
    # A path alias must resolve to the same instance before any configuration lock is acquired.
    $secondary = Start-Process -FilePath $executable -ArgumentList ('"--data-directory=' + $dataPath + '\."') -PassThru
    $null = $secondary.Handle
    if (-not $secondary.WaitForExit(10000) -or $secondary.ExitCode -ne 0) { throw 'The second instance did not redirect activation.' }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while ([TunnelDeck.Verification.LifecycleNative]::IsIconic($primary.MainWindowHandle) -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
    if ([TunnelDeck.Verification.LifecycleNative]::IsIconic($primary.MainWindowHandle) -or -not $primary.Responding) { throw 'Activation did not restore the primary window.' }
    Life-Wait DashboardPage
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $before) { throw 'Secondary activation rewrote configuration.' }
    Life-Capture 'single-instance-restored'
    $checks.singleInstanceBeforeConfiguration = 'Passed; same-directory alias redirected while the configuration lock was held'
    $checks.minimizedWindowRestored = 'Passed'
}
finally { $lock.Dispose() }

New-LifeState -scenario 'separate-directory'
Life-Open
if ($appProcess.Id -eq $primary.Id -or -not $primary.Responding -or -not $appProcess.Responding) { throw 'Isolated directories did not have independent test instances.' }
$checks.separateIsolatedDirectories = 'Passed'
Life-Close
$script:appProcess = $primary
$script:dataPath = $primaryPath
$script:statePath = $primaryState
Life-Close
$checks.normalClose = 'Passed'
