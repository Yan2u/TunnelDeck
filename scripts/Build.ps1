[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $LockedRestore
)

. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$buildArguments = @('build', (Join-Path $repoRoot 'TunnelDeck.slnx'), '-c', $Configuration, '-p:Platform=x64', '-v:minimal')
if ($LockedRestore) { $buildArguments += '-p:RestoreLockedMode=true' }
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
