[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [ValidateRange(1, 100)][int] $Cycles = 20, [string] $OutputDirectory, [string] $ExecutablePath)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/import-resize/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m12-review/import-resize' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$sshPath = Join-Path $runPath 'resize.config'
@'
Host ready
 HostName first.example
 User developer
 Compression yes
 ForwardX11 yes
 ForwardX11Trusted yes
 RemoteForward 9000 localhost:9000
Host invalid-port
 HostName second.example
 User developer
 Port 70000
 ProxyJump bastion.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = $null
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    Transfer-Preview $sshPath -Ssh
    if ((Transfer-Value ImportSummary) -ne '1 new server · 0 new profiles · 1 skipped') { throw 'The resize scenario was not loaded.' }
    for ($cycle = 0; $cycle -lt $Cycles; $cycle++) {
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 580)
        $null = Transfer-Ui scroll ImportPreview --to bottom
        $null = Transfer-Visible CommitImport
        $null = Transfer-Visible SkipUnavailableImports
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
        $null = Transfer-Ui scroll ImportPreview --to bottom
        if ((Transfer-Value ImportSummary) -ne '1 new server · 0 new profiles · 1 skipped') { throw 'Resizing changed the import plan.' }
    }
    Transfer-Capture 'after-resize-cycles'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); cycles = $Cycles; sizes = @('500x580', '1200x760'); passed = $true; dataPath = $dataPath } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Write-Host "Import preview remained responsive through $Cycles narrow/wide resize cycles."
}
finally { Transfer-Close }
