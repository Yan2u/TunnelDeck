[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

. (Join-Path $PSScriptRoot 'Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$dataPath = Join-Path $repoRoot ('.test-output\m8-resources\' + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $repoRoot 'artifacts\m8-review'
$statePath = Join-Path $dataPath 'state.json'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$appProcess = $null
$lockedFile = $null
$checks = [ordered]@{}
function Ui {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "Resource UI command failed [$($args -join ' ')]: $raw" }
    return $result
}
function Wait-Ui([string] $selector) { $null = Ui wait-for $selector --timeout 15000 }
function Invoke-Ready([string] $selector) { $null = Ui wait-for $selector --property IsEnabled --value True --timeout 5000; $null = Ui invoke $selector }
function Search-For([string] $value) {
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x4b, 0x11)
    $null = Ui focus TextBox
    $null = Ui set-value TextBox ''
    $null = Ui set-value TextBox $value
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
            $code = $_.Exception.GetBaseException().HResult -band 0xffff
            if ($code -notin @(2, 32, 33)) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
    throw 'The state file remained unavailable after the concurrent save.'
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(12)
    do { $state = Read-State; if (& $condition $state) { return $state }; Start-Sleep -Milliseconds 40 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Expected resource order was not persisted.'
}
function Capture([string] $name) { Start-Sleep -Milliseconds 400; $null = Ui screenshot --output (Join-Path $outputPath ($name + '.png')) }
function Bounds([string] $selector) {
    $match = (Ui search $selector).matches | Where-Object automationId -eq $selector | Select-Object -First 1
    if (-not $match -or $match.isOffscreen -or $match.width -le 0 -or $match.height -le 0) { throw "A drag endpoint is not visible: $selector" }
    return $match
}
function Drag-To([string] $from, [string] $to) {
    $null = Ui click $from
    $focusTarget = $from -replace '^Drag(Profile|Server)_', '$1Row_'
    $null = Ui focus $focusTarget
    $null = Ui wait-for $focusTarget --property HasKeyboardFocus --value True --timeout 5000
    Start-Sleep -Milliseconds 450
    $start = Bounds $from
    $end = Bounds $to
    [ordered]@{ from = $start; to = $end } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'last-resource-drag.json') -Encoding utf8
    [TunnelDeck.Verification.ForwardNative]::Drag($appProcess.MainWindowHandle, [int]($start.x + $start.width / 2), [int]($start.y + $start.height / 2),
        [int]($start.x + $start.width / 2), [int]($end.y + $end.height - 3))
}
function Close-App {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The resource test app did not close normally.' }
    }
}
try {
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -Preview Sample -Configuration $Configuration -DataDirectory $dataPath
    Wait-Ui DashboardPage
    $null = Ui invoke AllProfiles
    Wait-Ui ProfilesPage
    $null = Ui wait-for AddProfile --property IsEnabled --value True --timeout 15000
    $state = Read-State
    $first = $state.order.pinnedProfileIds[0]
    $second = $state.order.pinnedProfileIds[1]
    $other = @($state.order.otherProfileIds)
    Drag-To ('DragProfile_' + (Id $first)) ('ProfileRow_' + (Id $second))
    $null = Wait-State { param($s) $s.order.pinnedProfileIds[0] -eq $second -and $s.order.pinnedProfileIds[1] -eq $first }
    if (((Read-State).order.otherProfileIds -join ',') -ne ($other -join ',')) { throw 'Pinned drag changed Other order.' }
    Capture 'profiles-reordered-light'
    $checks.nativePinnedProfileDrag = 'Passed'

    $null = Ui focus ('MenuProfile_' + (Id $first))
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    Wait-Ui ('MoveUpProfile_' + (Id $first))
    $null = Ui focus ('MoveUpProfile_' + (Id $first))
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Wait-State { param($s) $s.order.pinnedProfileIds[0] -eq $first }
    $revision = (Read-State).revision
    Drag-To ('DragProfile_' + (Id $first)) ('ProfileRow_' + (Id $other[0]))
    Start-Sleep -Milliseconds 350
    if ((Read-State).revision -ne $revision -or (Read-State).order.pinnedProfileIds[0] -ne $first) { throw 'A cross-list drop changed pinning or order.' }
    $checks.keyboardMoveAndCrossListRejection = 'Passed'

    $third = $other[0]
    Invoke-Ready ('PinProfile_' + (Id $third))
    $null = Wait-State { param($s) $s.order.pinnedProfileIds.Count -eq 3 -and $s.order.pinnedProfileIds[2] -eq $third }
    Invoke-Ready ('PinProfile_' + (Id $second))
    $null = Wait-State { param($s) $s.order.otherProfileIds[0] -eq $second -and $s.order.pinnedProfileIds.Count -eq 2 }
    $checks.pinAppendAndUnpinAtOtherHead = 'Passed'

    $lockedFile = [IO.File]::Open($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    Drag-To ('DragProfile_' + (Id $first)) ('ProfileRow_' + (Id $third))
    Wait-Ui ResourceOrderError
    if ((Read-State).order.pinnedProfileIds[0] -ne $first) { throw 'A failed order commit changed disk state.' }
    $a = Bounds ('ProfileRow_' + (Id $first))
    $b = Bounds ('ProfileRow_' + (Id $third))
    if ($a.y -ge $b.y) { throw 'The failed drag was not rolled back in the list.' }
    Capture 'resource-order-save-error'
    $lockedFile.Dispose(); $lockedFile = $null
    Invoke-Ready RetryResourceChange
    $null = Wait-State { param($s) $s.order.pinnedProfileIds[0] -eq $third -and $s.order.pinnedProfileIds[1] -eq $first }
    $null = Ui wait-for ResourceOrderError --gone --timeout 5000
    $checks.failedDragRollsBackAndRetries = 'Passed'

    while ((Read-State).order.pinnedProfileIds.Count -gt 0) {
        $pinned = (Read-State).order.pinnedProfileIds[0]
        Invoke-Ready ('PinProfile_' + (Id $pinned))
        $null = Wait-State { param($s) $s.order.pinnedProfileIds -notcontains $pinned }
    }
    $null = Ui wait-for PinnedHeading --gone --timeout 5000
    if ((Ui search EmptyAddResource).matchCount -ne 0) { throw 'An empty Pinned group displayed the collection empty state.' }
    Capture 'profiles-no-pinned-light'
    $checks.emptyPinnedCollapsesWithoutEmptyCollection = 'Passed'

    $null = Ui invoke AddProfile
    Wait-Ui ProfileName
    $null = Ui set-value ProfileName 'Find M8 resource'
    Invoke-Ready SaveProfile
    $state = Wait-State { param($s) @($s.profiles | Where-Object name -eq 'Find M8 resource').Count -eq 1 }
    $created = ($state.profiles | Where-Object name -eq 'Find M8 resource').id
    if ($state.order.otherProfileIds[0] -ne $created) { throw 'A new profile was not inserted at Other[0].' }
    Search-For 'Find M8 resource'
    Wait-Ui ('SearchProfile_' + (Id $created))
    $null = Ui set-value ProfileName 'Renamed M8 resource'
    Invoke-Ready SaveProfile
    $null = Wait-State { param($s) @($s.profiles | Where-Object name -eq 'Renamed M8 resource').Count -eq 1 }
    $null = Ui wait-for ('SearchProfile_' + (Id $created)) --gone --timeout 5000
    Search-For 'Find M8 resource'
    Wait-Ui SearchNoResults
    Search-For 'Renamed M8 resource'
    Wait-Ui ('SearchProfile_' + (Id $created))
    $null = Ui focus TextBox
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Ui wait-for ProfileName --value 'Renamed M8 resource' --timeout 5000
    $state = Read-State
    if (($state.profiles | Where-Object id -eq $created).lastUsedAt) { throw 'Visiting a profile changed its usage timestamp.' }
    $checks.newResourceAndLiveSearchRename = 'Passed'

    $null = Ui invoke AllServers
    Wait-Ui ServersPage
    $state = Read-State
    $serverFirst = $state.order.pinnedServerIds[0]
    $serverSecond = $state.order.pinnedServerIds[1]
    $serverOther = @($state.order.otherServerIds)
    Drag-To ('DragServer_' + (Id $serverFirst)) ('ServerRow_' + (Id $serverSecond))
    $null = Wait-State { param($s) $s.order.pinnedServerIds[0] -eq $serverSecond }
    if (((Read-State).order.otherServerIds -join ',') -ne ($serverOther -join ',')) { throw 'Server drag changed Other order.' }
    $checks.nativePinnedServerDrag = 'Passed'

    $null = Ui invoke Settings
    Wait-Ui SettingsPage
    $null = Ui set-value SidebarMaximum '3'
    $null = Ui focus SidebarMaximum
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    $null = Wait-State { param($s) $s.settings.sidebarMaximumItems -eq 3 }
    $null = Ui invoke ThemeSelector
    $null = Ui invoke ThemeDark
    $null = Ui invoke AllServers
    $profileCount = (Ui search SidebarProfile_).matchCount
    $serverCount = (Ui search SidebarServer_).matchCount
    if ($profileCount -gt 3 -or $serverCount -gt 3) { throw 'Sidebar sections exceeded their individual maximum.' }
    Wait-Ui AllProfiles
    Wait-Ui AllServers
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 900, 760)
    Capture 'servers-900-dark'
    [TunnelDeck.Verification.ForwardNative]::Resize($appProcess.MainWindowHandle, 500, 650)
    Capture 'servers-500-dark'
    $checks.sidebarLimitsAndAdaptiveLists = 'Passed'

    $before = Read-State
    $order = $before.order | ConvertTo-Json -Compress
    Close-App
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -Configuration $Configuration -DataDirectory $dataPath
    Wait-Ui DashboardPage
    $null = Ui invoke AllProfiles
    Wait-Ui ProfilesPage
    if (((Read-State).order | ConvertTo-Json -Compress) -ne $order) { throw 'Resource order changed after restarting.' }
    if ((Read-State).revision -ne $before.revision) { throw 'Restarting or visiting resource pages wrote usage.' }
    Close-App
    $checks.restartPreservesOrderAndVisitsDoNotReorder = 'Passed'
    [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; dataDirectory = $dataPath; highContrast = 'Not switched globally'; humanGuiReview = 'Pending GUI D' } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $outputPath 'resources-verification.json') -Encoding utf8
    Write-Host 'M8 native resource verification passed.'
}
catch {
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'resources-partial.json') -Encoding utf8
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture 'resources-failure' } } catch { }
    throw
}
finally {
    if ($lockedFile) { $lockedFile.Dispose() }
    Close-App
}
