[CmdletBinding()]
param([Parameter(Mandatory)][string] $ManifestPath)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$resolvedManifest = [IO.Path]::GetFullPath($ManifestPath)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.test-output')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedManifest.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only repository-owned SSH fixtures can be stopped.'
}
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
if ($manifest.containerId -notmatch '^[0-9a-f]{64}$') { throw 'Invalid fixture container identity.' }
$containerJson = & wsl.exe -d $manifest.distribution -u root --exec docker inspect $manifest.containerId
if ($LASTEXITCODE -ne 0) { throw 'Fixture container is missing. Inspect its state before removing credentials.' }
$container = ($containerJson | ConvertFrom-Json)[0]
if ($container.Config.Labels.'com.tunneldeck.fixture' -ne 'sshd' -or
    $container.Config.Labels.'com.tunneldeck.run' -ne $manifest.runId) {
    throw 'Container labels do not match this fixture run.'
}
$outputPath = Split-Path $resolvedManifest -Parent
# Request logging is disabled in the control server; retain its tracebacks without manifests or secrets.
$controlDiagnostics = @(& wsl.exe -d $manifest.distribution -u root --exec docker logs $manifest.containerId 2>&1)
if ($LASTEXITCODE -ne 0) { throw 'Could not preserve the fixture control diagnostics.' }
Set-Content -LiteralPath (Join-Path $outputPath 'control-server.log') -Value $controlDiagnostics -Encoding utf8
& wsl.exe -d $manifest.distribution -u root --exec docker rm --force $manifest.containerId | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the fixture container.' }
foreach ($fileName in @('fixture.json', 'container-manifest.json', 'identity', 'identity_plain', 'container.id')) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $outputPath $fileName))
    if (-not $filePath.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a file outside .test-output.'
    }
    if (Test-Path -LiteralPath $filePath) { Remove-Item -LiteralPath $filePath }
}
Write-Host 'SSH fixture stopped; disposable credentials removed. Sanitized sshd logs retained.'
