[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sample = New-ActivityReviewData (Join-Path $repoRoot ('.test-output/m12-review/' + [guid]::NewGuid().ToString('N')))
$outputPath = Join-Path $repoRoot 'artifacts/m12-review/gui-review'
$null = New-Item -ItemType Directory -Path $outputPath -Force
$appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $sample.dataPath
Transfer-Wait DashboardPage
$null = Transfer-Ui scroll DashboardScroll --to bottom
Transfer-Capture 'activity-review'
[ordered]@{ processId = $appProcess.Id; dataPath = $sample.dataPath; isolation = 'Dedicated data directory; reserved example hosts; sample history; no connections started'; guiReview = 'M12 GUI H pending user evaluation' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding utf8NoBOM
Get-Content -Raw (Join-Path $outputPath 'review.json')
