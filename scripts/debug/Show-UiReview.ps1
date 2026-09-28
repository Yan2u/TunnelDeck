[CmdletBinding()]
param([string] $ExecutablePath, [string] $OutputDirectory)

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runPath = Join-Path $repoRoot ('.test-output/ui-review/' + [guid]::NewGuid().ToString('N'))
$sample = New-ActivityReviewData $runPath
$dataPath = $sample.dataPath
$state = Get-Content -Raw -LiteralPath $sample.statePath | ConvertFrom-Json -AsHashtable -DateKind String
$state.schemaVersion = 2
$state.settings.defaultAuthenticationMethod = 0
$state.settings.defaultIdentityFilePath = $null
$state.order.pinnedServerIds = @($sample.servers[0..1].id)
$state.order.otherServerIds = @($sample.servers[2..11].id)
$state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $sample.statePath -Encoding utf8NoBOM
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/ui-refresh/user-review' }
$null = New-Item -ItemType Directory -Path $outputPath -Force
$appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
Transfer-Wait DashboardPage
$null = Transfer-Ui wait-for StorageNotice --gone --timeout 5000
Transfer-Capture 'dashboard'
[ordered]@{
    processId = $appProcess.Id; windowHandle = $appProcess.MainWindowHandle.ToInt64()
    executable = $appProcess.Path; dataPath = $dataPath
    isolation = 'Dedicated configuration and credential namespace; example hosts and demo history; no connections started.'
    acceptance = 'Current UI ready for user review.'
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding utf8NoBOM
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'review.json')
