[CmdletBinding()]
param([string] $OutputDirectory, [switch] $Baseline, [string] $ExecutablePath)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Scale-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sample = New-ScaleTestData (Join-Path $repoRoot ('.test-output/resource-scale/' + [guid]::NewGuid().ToString('N')))
$statePath = $sample.statePath
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/resource-scale' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$sourceHash = (Get-FileHash -LiteralPath $statePath).Hash
$appProcess = $null
$measurements = [ordered]@{}
$failure = $null
$stage = 'Core search'

# Measure the exact search implementation separately from UIA process/IPC overhead.
Add-Type -Path (Join-Path $repoRoot 'src/TunnelDeck.Core/bin/x64/Release/net10.0/TunnelDeck.Core.dll')
Add-Type -Path (Join-Path $repoRoot 'src/TunnelDeck.Infrastructure/bin/x64/Release/net10.0/TunnelDeck.Infrastructure.dll')
$state = [TunnelDeck.Infrastructure.Persistence.AppStateJsonCodec]::new().Decode([IO.File]::ReadAllBytes($statePath)).State
$queries = @('scale', 'Scale server 0999', 'Scale profile 0999', 'developer scale-9', 'nothing-matches')
$search = @($queries | ForEach-Object {
    $query = $_
    for ($warmup = 0; $warmup -lt 10; $warmup++) { $null = [TunnelDeck.Core.Presentation.ResourceSearch]::Find($state, $query, 12) }
    $samples = @(0..49 | ForEach-Object {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $null = [TunnelDeck.Core.Presentation.ResourceSearch]::Find($state, $query, 12)
        $timer.Elapsed.TotalMilliseconds
    }) | Sort-Object
    [ordered]@{ query = $query; samples = $samples.Count; p50Ms = $samples[24]; p95Ms = $samples[47]; maximumMs = $samples[-1] }
})
$search | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'search.json') -Encoding utf8NoBOM

try {
    $stage = 'Dashboard startup'
    Write-Host 'Opening the 1000-server / 1000-profile dataset.'
    $startup = [Diagnostics.Stopwatch]::StartNew()
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $sample.dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for QuickServer --property IsEnabled --value True --timeout 30000
    $measurements.startupAndAutomationMs = $startup.Elapsed.TotalMilliseconds
    $measurements.dashboard = Get-ScaleProcessSample $appProcess
    foreach ($kind in @('Server', 'Profile')) {
        $stage = "$kind navigation"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Transfer-Invoke "All${kind}s"
        Transfer-Wait "${kind}sPage"
        $items = if ($kind -eq 'Server') { $sample.servers } else { $sample.profiles }
        Transfer-Wait ("${kind}Row_" + ([guid]$items[0].id).ToString('N'))
        $navigationMs = $timer.Elapsed.TotalMilliseconds
        $rows = Transfer-Ui search "${kind}Row_" --max 2000
        Write-Host "$kind page exposes $($rows.matchCount) materialized row buttons."
        $measurements[$kind] = [ordered]@{
            navigationAndAutomationMs = $navigationMs
            materializedRowButtons = $rows.matchCount
            process = Get-ScaleProcessSample $appProcess
        }
        Transfer-Capture ($kind.ToLowerInvariant() + '-1000')
        if (-not $Baseline -and $rows.matchCount -gt 160) { throw "$kind created too many row controls: $($rows.matchCount)." }
        $scrollTarget = if ($Baseline) { 'ResourceScroll' } else { 'OtherResources' }
        $stage = "$kind scroll to final item"
        $null = Transfer-Ui scroll $scrollTarget --to bottom
        Transfer-Wait ("${kind}Row_" + ([guid]$items[-1].id).ToString('N'))
        if ((Transfer-Ui search SidebarServer_).matchCount -gt 5 -or (Transfer-Ui search SidebarProfile_).matchCount -gt 5) { throw 'The scale dataset exceeded the sidebar limits.' }
    }
    $stage = 'Search and open final server'
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x4b, 0x11)
    $null = Transfer-Ui focus TextBox
    $null = Transfer-Ui set-value TextBox 'Scale server 0999'
    Transfer-Wait ('SearchServer_' + ([guid]$sample.servers[-1].id).ToString('N'))
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Transfer-Wait ServerDetailPage
    if ((Transfer-Value ServerHost) -ne 'scale-999.example') { throw 'Scale search navigated to the wrong server.' }
    if ((Get-FileHash -LiteralPath $statePath).Hash -ne $sourceHash) { throw 'Browsing the scale dataset wrote saved configuration.' }
    if (-not $Baseline -and @($search | Where-Object { $_.p95Ms -ge 100 }).Count -gt 0) { throw 'Local search exceeded the 100 ms P95 target.' }
    [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); baseline = [bool]$Baseline
        servers = $sample.servers.Count; profiles = $sample.profiles.Count; pinnedPerKind = $sample.pinned
        search = $search; native = $measurements; dataPath = $sample.dataPath
        notes = 'Search timings call the real Core implementation. Native navigation/startup measurements include UIA and command overhead; row counts refer to actual row buttons, not virtual item peers.'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
}
catch {
    $failure = $_.Exception.Message
    [ordered]@{ stage = $stage; error = $failure; search = $search; native = $measurements; dataPath = $sample.dataPath } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'failure.json') -Encoding utf8NoBOM
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'resource-scale-failure' } } catch { }
    throw
}
finally {
    try { Transfer-Close }
    catch { if (-not $failure) { throw }; Write-Warning "The failed scale run also reported a shutdown error: $($_.Exception.Message)" }
}
