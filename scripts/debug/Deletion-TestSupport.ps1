function Ui {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) {
        throw "Deletion UI command failed [$($args -join ' ')]: $raw"
    }
    return $result
}
function Wait-Ui([string] $selector) { $null = Ui wait-for $selector --timeout 15000 }
function Invoke-Ready([string] $selector) {
    $null = Ui wait-for $selector --property IsEnabled --value True --timeout 5000
    $null = Ui invoke $selector
}
function Id([string] $value) { ([Guid]$value).ToString('N') }
function Read-State {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        try {
            $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        }
        catch [IO.IOException] {
            if (($_.Exception.GetBaseException().HResult -band 0xffff) -notin @(2, 32, 33)) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
    throw 'The state file remained unavailable after the concurrent save.'
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $state = Read-State
        if (& $condition $state) { return $state }
        Start-Sleep -Milliseconds 40
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected deletion state was not persisted.'
}
function New-TestState([object[]] $servers, [object[]] $profiles, [string] $scenario) {
    $script:dataPath = Join-Path $repoRoot ('.test-output/m9-' + $scenario + '/' + [Guid]::NewGuid().ToString('N'))
    $script:statePath = Join-Path $dataPath 'state.json'
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    [ordered]@{
        schemaVersion = 1; revision = 0; servers = $servers; profiles = $profiles
        order = @{
            pinnedProfileIds = @($profiles | Select-Object -First 2 | ForEach-Object { $_.id })
            otherProfileIds = @($profiles | Select-Object -Skip 2 | ForEach-Object { $_.id })
            pinnedServerIds = @($servers | Select-Object -First 2 | ForEach-Object { $_.id })
            otherServerIds = @($servers | Select-Object -Skip 2 | ForEach-Object { $_.id })
        }
        settings = @{ sidebarMaximumItems = 5; defaultServerId = $servers[0].id; pinnedQuickServerId = $servers[0].id; autoFindAvailablePortDefault = $true }
        recentActivity = @(); statistics = @{ totalForwards = 0; serverForwards = @{}; profileUses = @{} }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding utf8
}
function New-Rule([string] $server, [int] $localPort, [int] $remotePort, [int] $mode = 0) {
    @{ id = [Guid]::NewGuid().ToString(); serverId = $server; mode = $mode; localHost = '127.0.0.1'; localPort = $localPort; remoteHost = '127.0.0.1'; remotePort = $remotePort; autoFindAvailablePort = $false }
}
function Open-App {
    $script:appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Wait-Ui DashboardPage
    $null = Ui wait-for QuickServer --property IsEnabled --value True --timeout 15000
}
function Close-App {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The deletion test app did not close normally.' }
    }
}
function Capture([string] $name) {
    Start-Sleep -Milliseconds 300
    $null = Ui screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function Find-Ui([string] $selector) { (Ui search $selector).matches | Where-Object automationId -eq $selector | Select-Object -First 1 }
function Assert-Visible([string] $selector) {
    $element = Find-Ui $selector
    if (-not $element -or $element.isOffscreen -or $element.width -le 0 -or $element.height -le 0) { throw "The control is not visible: $selector" }
    return $element
}
function Assert-Absent([string] $selector) { $null = Ui wait-for $selector --gone --timeout 5000 }
function Assert-DialogButtons {
    $null = Assert-Visible CloseButton
    $null = Assert-Visible PrimaryButton
}
function Open-Resource([string] $kind, [string] $resourceId) {
    $null = Ui invoke ('All' + $kind + 's')
    Wait-Ui ($kind + 'sPage')
    Invoke-Ready ($kind + 'Row_' + (Id $resourceId))
    Wait-Ui ($kind + 'DetailPage')
}
function Open-SingleDeletion([string] $kind, [string] $resourceId) {
    $null = Ui invoke ('All' + $kind + 's')
    Wait-Ui ($kind + 'sPage')
    Invoke-Ready ('Menu' + $kind + '_' + (Id $resourceId))
    Invoke-Ready ('Delete' + $kind + '_' + (Id $resourceId))
    Wait-Ui DeletionConfirmation
}
function Open-BulkDeletion([string] $kind) {
    $null = Ui invoke ('All' + $kind + 's')
    Wait-Ui ($kind + 'sPage')
    $null = Ui invoke MoreButton
    Invoke-Ready ('DeleteAll' + $kind + 's')
    Wait-Ui DeletionConfirmation
}
function Cancel-Deletion([int] $key = 0x1b) {
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, $key)
    Assert-Absent DeletionConfirmation
}
function Confirm-Deletion { $null = Ui invoke PrimaryButton; Assert-Absent DeletionConfirmation }
function Set-Theme([string] $theme) {
    $null = Ui invoke Settings
    Wait-Ui SettingsPage
    $null = Ui invoke ThemeSelector
    $null = Ui invoke ('Theme' + $theme)
}
function Search-For([string] $text) {
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x4b, 0x11)
    $null = Ui focus TextBox
    $null = Ui set-value TextBox ''
    $null = Ui set-value TextBox $text
}
