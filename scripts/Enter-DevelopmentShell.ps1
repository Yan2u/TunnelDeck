$ErrorActionPreference = 'Stop'
$developmentShell = 'D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1'
if (-not (Test-Path -LiteralPath $developmentShell)) {
    throw "Visual Studio development shell is missing: $developmentShell. Update the documented toolchain path for this machine."
}
$taskWorkingDirectory = $PWD.Path
& $developmentShell -Arch amd64 -HostArch amd64
Set-Location -LiteralPath $taskWorkingDirectory
