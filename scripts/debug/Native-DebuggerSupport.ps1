$ErrorActionPreference = 'Stop'

function Start-NativeCrashCapture([Diagnostics.Process] $process, [string] $debuggerPath, [string] $directory) {
    if (-not (Test-Path -LiteralPath $debuggerPath -PathType Leaf)) { throw 'The requested x64 CDB executable was not found.' }
    $directory = [IO.Path]::GetFullPath($directory)
    $null = New-Item -ItemType Directory -Path $directory -Force
    $commandPath = Join-Path $directory 'capture.txt'
    $symbolCache = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.test-output/debug-tools/symbols'
    $symbolPath = if (Test-Path -LiteralPath $symbolCache) {
        @(Get-ChildItem -LiteralPath $symbolCache -Recurse -File -Filter '*.pdb' | ForEach-Object { $_.DirectoryName } | Select-Object -Unique) -join ';'
    } else { $directory }
    @'
.logopen debugger.log
sxd av
sxe -c "q" epr
sxe -c ".dump /ma stowed.dmp; .ecxr; k; qd" 0xc000027b
.echo TunnelDeckCrashCaptureReady
g
'@ | Set-Content -LiteralPath $commandPath -Encoding ascii
    # Attach only to the explicitly supplied isolated test process. -pd prevents debugger exit
    # from terminating it; neither the executable nor its exception handling is changed.
    $debugger = Start-Process -FilePath ([IO.Path]::GetFullPath($debuggerPath)) -WorkingDirectory $directory -WindowStyle Hidden -PassThru `
        -ArgumentList @('-p', $process.Id, '-pd', '-y', ('"' + $symbolPath + '"'), '-cf', ('"' + $commandPath + '"')) `
        -RedirectStandardOutput (Join-Path $directory 'stdout.log') -RedirectStandardError (Join-Path $directory 'stderr.log')
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $logPath = Join-Path $directory 'debugger.log'
        if ((Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern '^TunnelDeckCrashCaptureReady$' -Quiet)) { return $debugger }
        if ($debugger.HasExited) { throw 'The native debugger exited before attaching. Inspect the capture logs.' }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    $debugger.Kill()
    throw 'The native debugger did not attach within 15 seconds.'
}

function Stop-NativeCrashCapture([Diagnostics.Process] $debugger) {
    if ($debugger -and -not $debugger.HasExited -and -not $debugger.WaitForExit(3000)) { $debugger.Kill() }
}
