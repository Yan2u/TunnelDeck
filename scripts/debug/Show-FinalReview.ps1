[CmdletBinding()]
param([string] $ExecutablePath, [string] $OutputDirectory)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/m13-review/' + [guid]::NewGuid().ToString('N'))
$sample = New-ActivityReviewData $runPath
$dataPath = $sample.dataPath
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/user-review' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$state = Get-Content -Raw -LiteralPath $sample.statePath | ConvertFrom-Json -AsHashtable -DateKind String
$state.schemaVersion = 2
$state.settings.defaultAuthenticationMethod = 0
$state.settings.defaultIdentityFilePath = $null
$state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $sample.statePath -Encoding utf8NoBOM
$sshPath = Join-Path $runPath 'review.config'
@'
# Disposable M13 review data; reserved example hosts, no real connections.
Host review-ready
 HostName preview.example
 User developer
 Port 2222
 Compression yes
 ForwardX11 yes
Host review-needs-username
 HostName unavailable.example
Host review-invalid-port
 HostName invalid.example
 User developer
 Port 70000
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
Transfer-Wait DashboardPage
$null = Transfer-Ui wait-for StorageNotice --gone --timeout 5000
Transfer-Capture 'dashboard'
Transfer-Preview $sshPath -Ssh
Transfer-Capture 'import-preview'
[ordered]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    processId = $appProcess.Id; windowHandle = $appProcess.MainWindowHandle.ToInt64()
    dataPath = $dataPath; sshConfigSample = $sshPath; executableOverride = $ExecutablePath
    isolation = 'Dedicated data and credential namespace; reserved example hosts and sample history; zero connections started'
    guiReview = 'M13 final user review pending'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding utf8NoBOM
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'review.json')
