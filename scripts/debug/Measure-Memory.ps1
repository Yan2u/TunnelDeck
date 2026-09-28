[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $GcDumpPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateRange(2, 5000)][int] $ResourceCount = 250,
    [ValidateRange(1, 50)][int] $NavigationCycles = 12,
    [switch] $LeaveRunning
)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
. (Join-Path $PSScriptRoot 'Scale-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$toolPath = [IO.Path]::GetFullPath($GcDumpPath)
if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) { throw 'Provide the dotnet-gcdump executable path.' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$sample = New-ScaleTestData (Join-Path $repoRoot ('.test-output/memory/' + [guid]::NewGuid().ToString('N'))) $ResourceCount
$appProcess = $null
$samples = [Collections.Generic.List[object]]::new()
$passed = $false

function Record-Memory([string] $phase) {
    $snapshot = Get-ScaleProcessSample $appProcess
    $snapshot.phase = $phase
    $snapshot.cpuSeconds = $appProcess.TotalProcessorTime.TotalSeconds
    $snapshot.capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $samples.Add($snapshot)
}

function Collect-ManagedHeap([string] $phase) {
    $heapPath = Join-Path $outputPath "$phase.gcdump"
    & $toolPath collect --process-id $appProcess.Id --output $heapPath --timeout 30 |
        Set-Content -LiteralPath (Join-Path $outputPath "$phase-collection.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Managed heap collection failed: $phase" }
    & $toolPath report $heapPath | Set-Content -LiteralPath (Join-Path $outputPath "$phase-heap.txt") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Managed heap report failed: $phase" }
    Start-Sleep -Milliseconds 300
    Record-Memory "$phase-after-gc"
}

function Navigate-MemoryScenario {
    foreach ($target in @(@('AllServers', 'ServersPage'), @('AllProfiles', 'ProfilesPage'), @('Settings', 'SettingsPage'), @('DashboardButton', 'DashboardPage'))) {
        Transfer-Invoke $target[0]
        Transfer-Wait $target[1]
    }
}

try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $sample.dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for QuickServer --property IsEnabled --value True --timeout 10000
    Start-Sleep -Milliseconds 750
    Record-Memory 'startup'
    Navigate-MemoryScenario
    Collect-ManagedHeap 'warm'
    for ($cycle = 1; $cycle -le $NavigationCycles; $cycle++) {
        Navigate-MemoryScenario
        Record-Memory "navigation-$cycle"
        if ($cycle % 4 -eq 0) { Write-Host "Memory navigation cycle $cycle / $NavigationCycles" }
    }
    Collect-ManagedHeap 'final'
    $passed = $true
}
finally {
    [ordered]@{
        passed = $passed; executable = [IO.Path]::GetFullPath($ExecutablePath)
        processId = if ($appProcess) { $appProcess.Id } else { $null }
        dataPath = $sample.dataPath; servers = $ResourceCount; profiles = $ResourceCount; navigationCycles = $NavigationCycles
        methodology = 'Release GUI, no SSH. One warm-up traversal then repeated Servers / Profiles / Settings / Dashboard navigation. gcdump induces a full GC; after-gc samples represent retention, not ordinary idle memory.'
        samples = $samples
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'memory.json') -Encoding utf8NoBOM
    if (-not $LeaveRunning -or -not $passed) { Transfer-Close }
}

[ordered]@{
    passed = $passed; resultPath = Join-Path $outputPath 'memory.json'; processId = $appProcess.Id
    startupPrivateMiB = [Math]::Round($samples[0].privateBytes / 1MB, 2)
    warmPrivateMiB = [Math]::Round($samples[1].privateBytes / 1MB, 2)
    finalPrivateMiB = [Math]::Round($samples[$samples.Count - 1].privateBytes / 1MB, 2)
} | ConvertTo-Json
