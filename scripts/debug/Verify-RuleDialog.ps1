[CmdletBinding()]
param([string] $ExecutablePath, [string] $OutputDirectory, [ValidateRange(2, 30)][int] $Cycles = 10)

. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output/rule-dialog/' + [guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/rule-dialog' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$statePath = Join-Path $dataPath 'state.json'
$serverId = [guid]::NewGuid().ToString()
@{
    schemaVersion = 2; revision = 0
    servers = @(@{ id = $serverId; displayName = 'Rule fixture'; host = 'release.example'; port = 22; username = 'smoke'; authMethod = 0 })
    profiles = @()
    order = @{ pinnedServerIds = @(); otherServerIds = @($serverId); pinnedProfileIds = @(); otherProfileIds = @() }
    settings = @{ sidebarMaximumItems = 5; defaultServerId = $serverId; pinnedQuickServerId = $null; autoFindAvailablePortDefault = $true; defaultAuthenticationMethod = 0; defaultIdentityFilePath = $null }
    recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
$appProcess = $null
$passed = $false
$stage = 'Startup'
$completed = 0
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    $null = Transfer-Ui wait-for QuickServer --property IsEnabled --value True --timeout 10000
    Transfer-Invoke AllProfiles
    Transfer-Wait ProfilesPage
    Transfer-Invoke AddProfile
    Transfer-Wait ProfileDetailPage
    $null = Transfer-Ui set-value ProfileName 'Rule dialog transitions'
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        $stage = "Open rule $cycle"
        $null = Transfer-Ui invoke AddRule
        $null = Transfer-Ui wait-for RuleServer --value 'Rule fixture' --contains --timeout 5000
        $null = Transfer-Ui set-value RuleLocalPortText '3000.5'
        $null = Transfer-Ui set-value RuleRemotePortText '4000'
        $stage = "Reject invalid rule $cycle"
        $null = Transfer-Ui invoke ApplyRule
        $null = Transfer-Ui wait-for RuleLocalPortError --value '65535' --contains --timeout 5000
        $null = Transfer-Ui set-value RuleLocalPortText ([string](3100 + $cycle))
        $stage = "Apply rule $cycle"
        $null = Transfer-Ui invoke ApplyRule
        $null = Transfer-Ui wait-for RuleServer --gone --timeout 5000
        if ((Transfer-ReadState).profiles.Count -ne 0) { throw 'Applying a rule saved an uncommitted profile.' }
        $completed++
    }
    $stage = 'Cancel and save'
    $null = Transfer-Ui invoke AddRule
    Transfer-Wait RuleServer
    $null = Transfer-Ui invoke CancelRule
    $null = Transfer-Ui wait-for RuleServer --gone --timeout 5000
    Transfer-Invoke SaveProfile
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $saved = Transfer-ReadState
        if ($saved.profiles.Count -eq 1 -and $saved.profiles[0].rules.Count -eq $Cycles) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($saved.profiles.Count -ne 1 -or $saved.profiles[0].rules.Count -ne $Cycles) { throw 'The saved profile lost or added a rule during dialog transitions.' }
    Transfer-Close
    $passed = $true
}
finally {
    [ordered]@{ passed = $passed; stage = $stage; requestedCycles = $Cycles; completedCycles = $completed; dataPath = $dataPath; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    Transfer-Close
}
Get-Content -LiteralPath (Join-Path $outputPath 'result.json')
