[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$')][string] $BuildLabel,
    [switch] $Verify
)

. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot 'src/TunnelDeck/TunnelDeck.csproj'
$projectDirectory = Split-Path $projectPath -Parent
$inputsJson = & dotnet msbuild $projectPath -nologo -p:Configuration=Release -p:Platform=x64 -getProperty:Version -getItem:Page,ApplicationDefinition
if ($LASTEXITCODE -ne 0) { throw 'The release project metadata could not be read.' }
$inputs = ($inputsJson -join "`n") | ConvertFrom-Json
$version = $inputs.Properties.Version
if (-not $BuildLabel) { $BuildLabel = $version }
if ($BuildLabel -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$') { throw 'The build label is not a valid package filename.' }
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/release' }
$packagePath = Join-Path $outputPath "TunnelDeck-$BuildLabel-win-x64"
$zipPath = $packagePath + '.zip'
if (Test-Path -LiteralPath $packagePath) { throw 'Use a new output directory; an existing package will not be overwritten.' }
if (Test-Path -LiteralPath $zipPath) { throw 'Use a new output directory; an existing archive will not be overwritten.' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$publishArguments = @(
    'publish', $projectPath, '-c', 'Release', '-p:Platform=x64', '-p:PublishProfile=ReleaseX64',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-p:RestoreLockedMode=true', '-o', $packagePath, '-v:minimal'
)
& dotnet @publishArguments | Tee-Object -FilePath (Join-Path $outputPath 'publish.log')
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
$xamlResources = @($inputs.Items.ApplicationDefinition) + @($inputs.Items.Page) | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($projectDirectory, $_.FullPath)
    if ($relative.StartsWith('..')) { throw 'Linked XAML outside the UI project needs an explicit publish mapping.' }
    [IO.Path]::ChangeExtension($relative, '.xbf')
}
foreach ($resource in @('TunnelDeck.pri') + $xamlResources) {
    if (-not (Test-Path -LiteralPath (Join-Path $packagePath $resource))) { throw "A required WinUI publish resource is missing: $resource" }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/packaging/Check-Prerequisites.ps1') -Destination $packagePath
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/packaging/Smoke-Test.ps1') -Destination $packagePath
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/release.md') -Destination (Join-Path $packagePath 'README.md')
$files = @(Get-ChildItem -LiteralPath $packagePath -File -Recurse)
if (@($files | Where-Object { $_.Name -in @('state.json', 'state.json.bak', 'host-keys.json', 'credential-cleanup.json') -or $_.Extension -in @('.key', '.pem') }).Count -gt 0) {
    throw 'Unexpected user-state or key material exists in the publish output.'
}
$manifest = [ordered]@{
    product = 'TunnelDeck'; version = $version; configuration = 'Release'; platform = 'x64'
    buildLabel = $BuildLabel; status = 'Unsigned local release package'
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitBase = (& git -C $repoRoot rev-parse HEAD).Trim()
    workingTreeHasUncommittedChanges = @(& git -C $repoRoot status --porcelain).Count -gt 0
    architecture = 'win-x64'; deployment = 'Unpackaged, framework-dependent, multiple files'
    internalSchema = 2; exportSchema = 1
    dependencies = @('.NET Runtime 10.0.x x64', 'Windows App Runtime 2.5.1 or compatible later 2.x x64', 'Microsoft Visual C++ Redistributable x64')
    files = @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($packagePath, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
    })
}
$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $packagePath 'package-manifest.json') -Encoding utf8NoBOM
[IO.Compression.ZipFile]::CreateFromDirectory($packagePath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$archiveHash = (Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant()
$archiveName = [IO.Path]::GetFileName($zipPath)
"$archiveHash  $archiveName" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ascii
[ordered]@{
    version = $version; configuration = 'Release'; architecture = 'win-x64'
    packagePath = $packagePath; archivePath = $zipPath; archiveBytes = (Get-Item -LiteralPath $zipPath).Length
    unpackedBytes = [long](Get-ChildItem -LiteralPath $packagePath -File -Recurse | Measure-Object Length -Sum).Sum
    sha256 = $archiveHash; signed = $false; publishedExternally = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'package.json') -Encoding utf8NoBOM
if ($Verify) {
    & (Join-Path $PSScriptRoot 'debug/Verify-Release.ps1') -ArchivePath $zipPath -OutputDirectory (Join-Path $outputPath 'validation')
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'package.json')
