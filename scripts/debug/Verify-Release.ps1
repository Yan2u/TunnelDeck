[CmdletBinding()]
param([Parameter(Mandatory)][string] $ArchivePath, [string] $PreviousExecutable, [string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/release-smoke/' + [guid]::NewGuid().ToString('N'))
$installPath = Join-Path $runPath 'App with spaces 私人'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/release-smoke' }
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $installPath
$manifest = Get-Content -Raw -LiteralPath (Join-Path $installPath 'package-manifest.json') | ConvertFrom-Json
$rootPrefix = [IO.Path]::GetFullPath($installPath).TrimEnd('\') + '\'
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $installPath $file.path))
    if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The manifest references a path outside the package.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Package integrity failed: $($file.path)" }
}
$smoke = Join-Path $installPath 'Smoke-Test.ps1'
$smokeArguments = @('-NoProfile', '-File', $smoke, '-InstallDirectory', $installPath, '-OutputDirectory', $outputPath)
if ($PreviousExecutable) { $smokeArguments += @('-PreviousExecutable', $PreviousExecutable) }
& powershell.exe @smokeArguments
if ($LASTEXITCODE -ne 0) { throw "Package smoke failed ($LASTEXITCODE)." }
