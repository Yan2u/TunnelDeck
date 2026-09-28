[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Activity-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sample = New-ActivityReviewData (Join-Path $repoRoot ('.test-output/authentication-review/' + [guid]::NewGuid().ToString('N')))
$statePath = $sample.statePath
# Preserve the UTC strings required by the saved-state contract when extending the sample.
$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json -AsHashtable -DateKind String
$state.schemaVersion = 2
$state.settings.defaultAuthenticationMethod = 1
$state.settings.defaultIdentityFilePath = $null
$state | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$outputPath = Join-Path $repoRoot 'artifacts/m12-auth-defaults/gui-review'
$null = New-Item -ItemType Directory -Path $outputPath -Force
$appProcess = $null

try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $sample.dataPath
    Transfer-Wait DashboardPage
    Transfer-Invoke Settings
    Transfer-Wait SettingsPage
    $null = Transfer-Ui wait-for DefaultAuthentication --property IsEnabled --value True --timeout 15000
    $null = Transfer-Ui scroll SettingsScroll --to bottom
    Transfer-Wait DefaultKeyPath
    $null = Transfer-Visible BrowseDefaultKey
    Transfer-Capture 'authentication-review'
    $appProcess.Refresh()
    if (-not $appProcess.Responding) { throw 'The authentication review window is not responding.' }
    [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        processId = $appProcess.Id
        dataPath = $sample.dataPath
        isolation = 'Dedicated data directory; reserved example hosts; sample history; no connections started'
        page = 'Settings / Server authentication'
        defaultAuthenticationMethod = 'PrivateKey (review data only)'
        keyDetection = 'Only checks whether the user profile .ssh/id_rsa exists; no key contents are read'
        guiReview = 'M12 GUI H authentication feedback pending user evaluation'
        userDataModified = $false
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'review.json') -Encoding utf8NoBOM
    Get-Content -Raw -LiteralPath (Join-Path $outputPath 'review.json')
}
catch {
    try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'authentication-review-failure' } } catch { }
    Transfer-Close
    throw
}
