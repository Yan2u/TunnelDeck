[CmdletBinding()]
param([switch] $NoFixtureBuild, [string] $OutputDirectory, [string] $ExecutablePath)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Scale-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output/application-performance/' + [guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/application-performance' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$statePath = Join-Path $dataPath 'state.json'
$fixturePath = $null
$appProcess = $null
$ports = $null
$echo = $null
$startup = [Collections.Generic.List[object]]::new()

function Post-Fixture([string] $route, [hashtable] $body) {
    Invoke-RestMethod -Uri "http://$($manifest.host):$($manifest.controlPort)/$route" -NoProxy -Method Post -TimeoutSec 10 -Headers @{ Authorization = 'Bearer ' + $manifest.controlToken } -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)
}
function Wait-SavedState([scriptblock] $predicate) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do { $snapshot = Transfer-ReadState; if (& $predicate $snapshot) { return $snapshot }; Start-Sleep -Milliseconds 80 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected performance-run state was not saved.'
}

try {
    $fixturePath = & (Join-Path $repoRoot 'scripts/fixtures/Start-SshFixture.ps1') -NoBuild:$NoFixtureBuild
    $manifest = Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json
    $ports = [TunnelDeck.Verification.ForwardPortBlock]::new(10)
    $echo = [TunnelDeck.Verification.ForwardEchoServer]::new()
    $remotePorts = @(0..9 | ForEach-Object { [int](Post-Fixture reserve @{ port = 0 }).port })
    $serverId = [guid]::NewGuid()
    $profileId = [guid]::NewGuid()
    $rules = @(0..9 | ForEach-Object {
        @{ id = [guid]::NewGuid().ToString(); serverId = $serverId.ToString(); mode = 0; localHost = '127.0.0.1'; localPort = $ports.First + $_; remoteHost = '127.0.0.1'; remotePort = $manifest.echoPort; autoFindAvailablePort = $false }
        @{ id = [guid]::NewGuid().ToString(); serverId = $serverId.ToString(); mode = 1; localHost = '127.0.0.1'; localPort = $echo.Port; remoteHost = '127.0.0.1'; remotePort = $remotePorts[$_]; autoFindAvailablePort = $false }
    })
    @{
        schemaVersion = 2; revision = 0
        servers = @(@{ id = $serverId.ToString(); displayName = 'Isolated performance server'; host = $manifest.host; port = $manifest.allowPort; username = $manifest.username; authMethod = 0 })
        profiles = @(@{ id = $profileId.ToString(); name = '20 real forwards'; rules = $rules })
        order = @{ pinnedServerIds = @(); otherServerIds = @($serverId.ToString()); pinnedProfileIds = @(); otherProfileIds = @($profileId.ToString()) }
        settings = @{ sidebarMaximumItems = 5; defaultServerId = $serverId.ToString(); pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true; defaultAuthenticationMethod = 0; defaultIdentityFilePath = $null }
        recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM

    for ($iteration = 0; $iteration -lt 4; $iteration++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
        $windowMs = $watch.Elapsed.TotalMilliseconds
        Transfer-Wait DashboardPage
        $null = Transfer-Ui wait-for QuickServer --property IsEnabled --value True --timeout 15000
        $startup.Add([ordered]@{ launch = $iteration + 1; kind = if ($iteration -eq 0) { 'First process launch; OS file cache not flushed' } else { 'Warm process launch' }; toResponsiveWindowMs = $windowMs; readyIncludingUiAutomationMs = $watch.Elapsed.TotalMilliseconds })
        if ($iteration -lt 3) { Transfer-Close }
    }
    $before = Get-ScaleProcessSample $appProcess
    $cpuBefore = $appProcess.TotalProcessorTime.TotalSeconds
    $idle = [Diagnostics.Stopwatch]::StartNew()
    $idleSamples = [Collections.Generic.List[object]]::new()
    Write-Host 'Sampling the idle application for 60 seconds.'
    while ($idle.Elapsed.TotalSeconds -lt 60) {
        Start-Sleep -Milliseconds 1000
        $sample = Get-ScaleProcessSample $appProcess
        $sample.elapsedSeconds = $idle.Elapsed.TotalSeconds
        $sample.cpuSeconds = $appProcess.TotalProcessorTime.TotalSeconds - $cpuBefore
        $idleSamples.Add($sample)
    }
    $idle.Stop()
    $cpuSeconds = $appProcess.TotalProcessorTime.TotalSeconds - $cpuBefore
    $corePercent = 100 * $cpuSeconds / $idle.Elapsed.TotalSeconds
    $machinePercent = $corePercent / [Environment]::ProcessorCount
    if ($machinePercent -ge 1) { throw 'Idle average CPU exceeded the 1% machine target.' }
    $before = Get-ScaleProcessSample $appProcess
    for ($index = 0; $index -lt 10; $index++) { $ports.Release($index); $null = Post-Fixture release @{ port = $remotePorts[$index] } }
    Transfer-Invoke AllProfiles
    Transfer-Wait ProfilesPage
    Transfer-Invoke ('ProfileRow_' + $profileId.ToString('N'))
    Transfer-Wait ProfileDetailPage
    Transfer-Invoke StartProfile
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $credentialPrompts = 0
    do {
        if ((Transfer-Ui search ForwardCredentialDialog).matchCount -gt 0) {
            & winapp ui set-value ForwardCredentialPassword $manifest.password -a $appProcess.Id --json *> $null
            if ($LASTEXITCODE -ne 0) { throw 'The disposable fixture credential could not be entered.' }
            $credentialPrompts++
            Transfer-Invoke PrimaryButton
        }
        elseif ((Transfer-Ui search HostKeyApproval).matchCount -gt 0) {
            $null = Transfer-Ui wait-for HostKeyFingerprint --value $manifest.hostKeyFingerprint --contains --timeout 5000
            Transfer-Invoke PrimaryButton
        }
        elseif ((Transfer-ReadState).statistics.totalForwards -eq 20) { break }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The 20-forward profile did not finish starting.' }
        Start-Sleep -Milliseconds 80
    } while ($true)
    Transfer-Invoke DashboardButton
    $null = Transfer-Ui wait-for ActiveCount --value '20 active' --timeout 15000
    $payload = [byte[]](0..255)
    for ($index = 0; $index -lt 10; $index++) {
        $null = [TunnelDeck.Verification.ForwardNative]::AssertPayloadAsync($ports.First + $index, $payload).GetAwaiter().GetResult()
        $probe = Post-Fixture probe @{ port = $remotePorts[$index]; payload = [Convert]::ToBase64String($payload) }
        if ($probe.payload -ne [Convert]::ToBase64String($payload)) { throw 'A remote performance forward changed the payload.' }
    }
    $withTunnels = Get-ScaleProcessSample $appProcess
    Transfer-Capture 'twenty-real-forwards'
    Transfer-Invoke StopAllTunnels
    $null = Transfer-Ui wait-for ActiveCount --value '0 active' --timeout 15000
    $null = Wait-SavedState { param($state) $state.recentActivity.Count -eq 40 }
    for ($index = 0; $index -lt 10; $index++) {
        [TunnelDeck.Verification.ForwardNative]::AssertReusable($ports.First + $index)
        $listener = Post-Fixture listening @{ port = $remotePorts[$index] }
        if ($listener.listening) { throw 'A remote listener remained after Stop all.' }
    }
    $afterStop = Get-ScaleProcessSample $appProcess
    $executable = $appProcess.MainModule.FileName
    $rendererModules = @($appProcess.Modules | Where-Object { $_.ModuleName -in @('RTSSHooks64.dll', 'Microsoft.UI.Xaml.dll', 'coreclr.dll') } |
        Select-Object ModuleName, @{ Name = 'version'; Expression = { $_.FileVersionInfo.ProductVersion } })
    $closeWatch = [Diagnostics.Stopwatch]::StartNew()
    Transfer-Close
    $closeMs = $closeWatch.Elapsed.TotalMilliseconds
    [ordered]@{
        passed = $true; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); startup = $startup
        executable = $executable; executableSha256 = (Get-FileHash -LiteralPath $executable).Hash.ToLowerInvariant(); rendererModules = $rendererModules
        idle = @{ seconds = $idle.Elapsed.TotalSeconds; cpuSeconds = $cpuSeconds; corePercent = $corePercent; machinePercent = $machinePercent; logicalProcessors = [Environment]::ProcessorCount; samples = $idleSamples }
        beforeTunnels = $before; twentyTunnels = $withTunnels; afterStop = $afterStop
        incrementalPrivateBytes = $withTunnels.privateBytes - $before.privateBytes
        incrementalWorkingSetBytes = $withTunnels.workingSetBytes - $before.workingSetBytes
        credentialPrompts = $credentialPrompts; payloadsVerified = 20; allListenersReleased = $true; normalCloseMs = $closeMs
        dataPath = $dataPath; notes = 'Release GUI process; shared runtimes. Ten Local plus ten Remote forwards use an isolated SSH fixture. Memory after Stop is not a forced-GC or leak measurement; see the separate 100-cycle test.'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Write-Host "Idle CPU: $machinePercent% of the machine; 20 payloads verified and all listeners released."
}
finally {
    try { Transfer-Close }
    finally {
        if ($ports) { $ports.Dispose() }
        if ($echo) { $echo.Dispose() }
        if ($fixturePath) { & (Join-Path $repoRoot 'scripts/fixtures/Stop-SshFixture.ps1') -ManifestPath $fixturePath }
    }
}
