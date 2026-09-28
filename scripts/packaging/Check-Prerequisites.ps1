[CmdletBinding()]
param([switch] $Json)
$ErrorActionPreference = 'Stop'
$os = Get-CimInstance Win32_OperatingSystem
$processors = @(Get-CimInstance Win32_Processor)
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
$runtimes = if (Test-Path -LiteralPath $dotnet) { @(& $dotnet --list-runtimes) } else { @() }
$frameworks = @(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' -ErrorAction SilentlyContinue | Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'2.5.1.0' })
$vc = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
if (-not $vc) { $vc = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue }
$checks = [ordered]@{
    windows11X64 = [int]$os.BuildNumber -ge 22000 -and @($processors | Where-Object Architecture -eq 9).Count -gt 0
    dotnet10X64 = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.0\.\d+ ' }).Count -gt 0
    windowsAppRuntime2X64 = $frameworks.Count -gt 0
    visualCppX64 = $vc -and $vc.Installed -eq 1
}
$ready = @($checks.Values | Where-Object { -not $_ }).Count -eq 0
$result = [ordered]@{
    ready = $ready; checks = $checks; os = $os.Caption
    dotnetRuntimes = $runtimes; windowsAppRuntimeVersions = @($frameworks | ForEach-Object { $_.Version.ToString() })
    visualCppVersion = if ($vc) { $vc.Version } else { $null }
    optionalOpenSshClient = [bool](Get-Command ssh.exe -ErrorAction SilentlyContinue)
    installsOrChangesSettings = $false
}
if ($Json) { $result | ConvertTo-Json -Depth 5 }
else {
    $checks.GetEnumerator() | Format-Table Name, Value -AutoSize
    if ($ready) { Write-Host 'Prerequisites are available. Run TunnelDeck.exe.' }
    else { Write-Host 'See README.md for the official runtime download links, install the missing prerequisites, then check again.' }
}
if (-not $ready) { exit 1 }
