[CmdletBinding(SupportsShouldProcess)]
param([switch] $IncludeBuildOutputs, [string] $ReportPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$rootPrefix = $repoRoot + '\'
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'TunnelDeck.slnx'))) { throw 'Run this script from the TunnelDeck source tree.' }
$reportFile = if ($ReportPath) { [IO.Path]::GetFullPath($ReportPath) } else { Join-Path $repoRoot 'artifacts/cleanup.json' }
$candidates = [Collections.Generic.List[string]]::new()
$candidates.Add((Join-Path $repoRoot '.test-output'))
$fixtureRoot = Join-Path $repoRoot '.test-output'
if (Test-Path -LiteralPath $fixtureRoot) {
    $liveManifests = @(Get-ChildItem -LiteralPath $fixtureRoot -Directory | Where-Object { $_.Name -match '^ssh-[0-9a-f]{32}$' } |
        ForEach-Object { Join-Path $_.FullName 'fixture.json' } | Where-Object { Test-Path -LiteralPath $_ })
    if ($liveManifests.Count -gt 0) { throw 'Stop the repository SSH fixtures with scripts/fixtures/Stop-SshFixture.ps1 before deleting their data.' }
}
$artifactRoot = Join-Path $repoRoot 'artifacts'
if (Test-Path -LiteralPath $artifactRoot) {
    Get-ChildItem -LiteralPath $artifactRoot -Force | Where-Object { $_.Name -ne 'release' -and $_.FullName -ne $reportFile } |
        ForEach-Object { $candidates.Add($_.FullName) }
}
foreach ($relative in @('artifacts/release/publish.log', 'artifacts/release/validation/isolated-data', 'artifacts/release/validation/rollback-data')) {
    $candidates.Add((Join-Path $repoRoot $relative))
}
if ($IncludeBuildOutputs) {
    [xml]$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'TunnelDeck.slnx') -Raw
    foreach ($project in $solution.Solution.Folder.Project) {
        $directory = Split-Path (Join-Path $repoRoot $project.Path) -Parent
        foreach ($name in @('bin', 'obj')) { $candidates.Add((Join-Path $directory $name)) }
    }
}

function Assert-WorkspacePath([string] $path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup must stay inside the workspace: $fullPath" }
    $ancestor = $fullPath
    while ($ancestor.Length -ge $repoRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Cleanup does not traverse reparse points: $ancestor"
        }
        $ancestor = Split-Path $ancestor -Parent
    }
    return $fullPath
}

$reportFile = Assert-WorkspacePath $reportFile
$tracked = @(& git -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Tracked-file protection could not be checked.' }
$processes = @(Get-CimInstance Win32_Process | Where-Object ProcessId -NE $PID)
$inventory = [Collections.Generic.List[object]]::new()
foreach ($candidate in ($candidates | Select-Object -Unique)) {
    $target = Assert-WorkspacePath $candidate
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $targetPrefix = $target.TrimEnd('\') + '\'
    if ($reportFile -eq $target -or $reportFile.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The cleanup report cannot be inside a deleted directory.' }
    $relative = [IO.Path]::GetRelativePath($repoRoot, $target).Replace('\', '/')
    if (@($tracked | Where-Object { $_ -eq $relative -or $_.StartsWith($relative + '/', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "Cleanup target contains tracked files: $relative"
    }
    foreach ($process in $processes) {
        if (($process.ExecutablePath -and ($process.ExecutablePath -eq $target -or $process.ExecutablePath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase))) -or
            ($process.CommandLine -and $process.CommandLine.Contains($target, [StringComparison]::OrdinalIgnoreCase))) {
            throw "Close the process using $relative before cleanup (PID $($process.ProcessId))."
        }
    }
    $bytes = [long]0
    $files = 0
    $pending = [Collections.Generic.Stack[IO.FileSystemInfo]]::new()
    $pending.Push((Get-Item -LiteralPath $target -Force))
    while ($pending.Count -gt 0) {
        $entry = $pending.Pop()
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cleanup does not traverse reparse points: $($entry.FullName)" }
        if ($entry -is [IO.DirectoryInfo]) {
            foreach ($child in (Get-ChildItem -LiteralPath $entry.FullName -Force)) { $pending.Push($child) }
        } else { $bytes += $entry.Length; $files++ }
    }
    $inventory.Add([ordered]@{ path = $relative; bytes = $bytes; files = $files; deleted = $false; absolutePath = $target })
}

# All paths, files and process ownership are checked before the first deletion.
foreach ($item in $inventory) {
    if ($PSCmdlet.ShouldProcess($item.absolutePath, 'Delete generated artifacts')) {
        Remove-Item -LiteralPath $item.absolutePath -Recurse -Force
        $item.deleted = $true
    }
    $item.Remove('absolutePath')
}
$freedBytes = [long]0
$deletedFiles = [long]0
foreach ($item in $inventory) {
    if ($item.deleted) { $freedBytes += $item.bytes; $deletedFiles += $item.files }
}
$report = [ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); includeBuildOutputs = [bool]$IncludeBuildOutputs
    freedBytes = $freedBytes; deletedFiles = $deletedFiles
    sizeMethod = 'Sum of the logical sizes of deleted files, not a volume free-space delta'
    preserved = @('artifacts/release package and validation summaries', 'source and docs/evidence', 'Visual Studio preferences', 'user application data and SSH files', 'global tool and NuGet caches')
    targets = $inventory
}
if (-not $WhatIfPreference) {
    $null = New-Item -ItemType Directory -Path (Split-Path $reportFile -Parent) -Force
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile -Encoding utf8NoBOM
}
$report | ConvertTo-Json -Depth 6
