[CmdletBinding()]
param([string] $OutputDirectory, [string] $ExecutablePath)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/import-scale/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/import-scale' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$sshPath = Join-Path $runPath 'scale.config'
0..999 | ForEach-Object { "Host importscale$($_.ToString('D4'))`n HostName scale-$_.example`n User developer" } |
    Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = $null
$passed = $false
$counts = [Collections.Generic.List[object]]::new()
function Read-ImportRows([string] $expected) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $matches = Transfer-Ui search 'importscale' --max 5000
        $rows = @($matches.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -match '^importscale\d{4}$' })
        if ($matches.hasMore -or $rows.Count -ge 200) { throw 'Import preview did not expose a bounded set of accessible rows.' }
        if ($expected -in $rows.name) { return $rows }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "The expected import row was not accessible: $expected"
}
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    Transfer-Preview $sshPath -Ssh
    if ((Transfer-Value ImportSummary) -ne '1000 new servers · 0 new profiles · 0 skipped') { throw 'The large import preview is incomplete.' }
    foreach ($position in @('top', 'bottom', 'top')) {
        $null = Transfer-Ui scroll ImportPreview --to $position
        # A row and its heading share the same name. Select list-item peers explicitly instead
        # of asking wait-for to resolve an ambiguous name to one element.
        $expected = if ($position -eq 'bottom') { 'importscale0999' } else { 'importscale0000' }
        $rows = @(Read-ImportRows $expected)
        $counts.Add([ordered]@{ position = $position; materializedAccessibleRows = $rows.Count })
    }
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 580)
    $null = Transfer-Ui scroll ImportPreview --to bottom
    $null = Read-ImportRows 'importscale0999'
    $null = Transfer-Visible CommitImport
    $null = Transfer-Visible CancelImport
    Transfer-Capture 'last-of-1000-narrow'
    Transfer-Invoke CancelImport
    Transfer-Wait ServersPage
    if (Test-Path -LiteralPath (Join-Path $dataPath 'state.json')) { throw 'Previewing or cancelling the large file unexpectedly wrote configuration.' }
    $passed = $true
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'failure'; Transfer-Ui inspect --depth 8 | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $outputPath 'failure-tree.json') -Encoding utf8NoBOM } } catch { }
    throw
}
finally {
    try { Transfer-Close }
    finally {
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; sourceEntries = 1000; viewportCounts = $counts; dataPath = $dataPath } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
