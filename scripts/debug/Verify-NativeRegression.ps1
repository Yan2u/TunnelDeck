[CmdletBinding()]
param([switch] $NoFixtureBuild, [string] $OutputDirectory, [string[]] $Suites)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/native-regression' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$names = @('Resources', 'Shell', 'Window', 'Dashboard', 'Activity', 'AuthenticationDefaults', 'Transfer', 'Connections', 'Forwards', 'Profiles', 'Deletions', 'Lifecycle', 'Storage')
if ($Suites) {
    if (@($Suites | Where-Object { $_ -notin $names }).Count -gt 0) { throw 'An unknown native regression suite was requested.' }
    $names = $Suites
}
$results = [Collections.Generic.List[object]]::new()
$pwsh = (Get-Command pwsh).Source
try {
    foreach ($name in $names) {
        $scriptPath = Join-Path $PSScriptRoot "Verify-$name.ps1"
        $command = Get-Command $scriptPath
        if (-not $command.Parameters.ContainsKey('OutputDirectory')) { throw "$name does not support isolated evidence output." }
        $logPath = Join-Path $outputPath "$name.log"
        $arguments = @('-NoProfile', '-WindowStyle', 'Hidden', '-File', $scriptPath, '-OutputDirectory', (Join-Path $outputPath $name.ToLowerInvariant()))
        if ($NoFixtureBuild -and $command.Parameters.ContainsKey('NoFixtureBuild')) { $arguments += '-NoFixtureBuild' }
        Write-Host "Native regression: $name"
        & $pwsh @arguments *> $logPath
        $passed = $LASTEXITCODE -eq 0
        $results.Add([ordered]@{ name = $name; passed = $passed; log = $logPath })
        if (-not $passed) { throw "$name failed. Inspect $logPath." }
    }
}
finally {
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); expectedSuites = $names.Count; completedSuites = $results.Count; passed = $results.Count -eq $names.Count -and @($results | Where-Object { -not $_.passed }).Count -eq 0; results = $results } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
