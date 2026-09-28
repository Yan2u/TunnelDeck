[CmdletBinding()]
param([string] $OutputDirectory, [string] $ExecutablePath, [ValidateRange(1, 100)][int] $Cycles = 10)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/import-focus/' + [guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/import-focus' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$sshPath = Join-Path $runPath 'focus.config'
@'
Host ready
 HostName ready.example
 User developer
 Compression yes
 ForwardX11 yes
Host invalid-port
 HostName unavailable.example
 User developer
 Port 70000
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = $null
$passed = $false
$completedCycles = 0
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    Transfer-Preview $sshPath -Ssh
    for ($cycle = 0; $cycle -lt $Cycles; $cycle++) {
        if ((Transfer-Value ImportSummary) -ne '1 new server · 0 new profiles · 1 skipped') { throw 'The expected SSH preview was lost.' }
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 580)
        $null = Transfer-Ui scroll ImportPreview --to bottom
        if ((Transfer-Value ImportSummary) -ne '1 new server · 0 new profiles · 1 skipped') { throw 'Entering the scrolled preview changed its format or plan.' }
        $null = Transfer-Visible CommitImport
        $null = Transfer-Visible CancelImport
        [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
        # Changing the ComboBox must cancel the previous automatic preview without moving focus into its items.
        Transfer-Invoke ImportFormat
        Transfer-Invoke ImportJsonFormat
        $null = Transfer-Ui wait-for ImportError --timeout 5000
        if (Transfer-Enabled CommitImport) { throw 'Changing file format left the old preview eligible for import.' }
        Transfer-Invoke ImportFormat
        Transfer-Invoke ImportSshFormat
        $null = Transfer-Ui wait-for ImportSummary --value '1 new server · 0 new profiles · 1 skipped' --timeout 5000
        $completedCycles++
    }
    Transfer-Capture 'focus-and-format-changes'
    $passed = $true
}
finally {
    try { Transfer-Close }
    finally {
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; requestedCycles = $Cycles; completedCycles = $completedCycles; executableOverride = $ExecutablePath; dataPath = $dataPath } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
