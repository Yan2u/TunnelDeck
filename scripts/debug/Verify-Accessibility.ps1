[CmdletBinding()]
param([string] $OutputDirectory, [string] $ExecutablePath)
. (Join-Path $PSScriptRoot '../Enter-DevelopmentShell.ps1')
. (Join-Path $PSScriptRoot 'Transfer-TestSupport.ps1')
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dataPath = Join-Path $repoRoot ('.test-output/accessibility/' + [guid]::NewGuid().ToString('N'))
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts/m13-review/accessibility' }
$null = New-Item -ItemType Directory -Path $dataPath, $outputPath -Force
$sshPath = Join-Path $dataPath 'keyboard.config'
@'
Host keyboard-ready
 HostName keyboard.example
 User developer
Host missing-user
 HostName unavailable.example
'@ | Set-Content -LiteralPath $sshPath -Encoding utf8NoBOM
$appProcess = $null
$passed = $false
$checks = [ordered]@{}
function Assert-Named([string[]] $selectors) {
    foreach ($selector in $selectors) {
        $name = (Transfer-Ui get-property $selector --property Name).properties.Name
        if ([string]::IsNullOrWhiteSpace($name)) { throw "An accessible control has no name: $selector" }
    }
}
function Wait-Focus([string] $selector) { $null = Transfer-Ui wait-for $selector --property HasKeyboardFocus --value True --timeout 5000 }
try {
    $appProcess = & (Join-Path $PSScriptRoot '../Run.ps1') -DataDirectory $dataPath -ExecutablePath $ExecutablePath
    Transfer-Wait DashboardPage
    Assert-Named @('DashboardButton', 'Settings', 'OpenQuickTerminal')
    Transfer-Invoke AllServers
    Transfer-Wait ServersPage
    $null = Transfer-Ui focus AddServer
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Transfer-Wait ServerDetailPage
    Wait-Focus ServerDisplayName
    $checks.focusAfterKeyboardNavigation = Transfer-Ui get-focused
    Assert-Named @('SaveServer', 'CancelServer', 'ServerDisplayName', 'ServerHost', 'ServerPort', 'ServerUsername', 'AuthenticationMethod')
    $null = Transfer-Ui focus ServerDisplayName
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09)
    Wait-Focus ServerHost
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09, 0x10)
    Wait-Focus ServerDisplayName
    foreach ($selector in @('ServerHost', 'ServerPort', 'ServerUsername', 'AuthenticationMethod')) {
        [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09)
        Wait-Focus $selector
    }
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x28, 0x12)
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x1b)
    Wait-Focus AuthenticationMethod
    $checks.tabShiftTabAndEscape = 'Passed'
    $null = Transfer-Ui set-value ServerHost 'bad host'
    $null = Transfer-Ui set-value ServerPort '70000'
    $null = Transfer-Ui set-value ServerUsername 'developer'
    Transfer-Invoke SaveServer
    Transfer-Wait ServerHostError
    Transfer-Wait ServerPortError
    Assert-Named @('ServerHostError', 'ServerPortError')
    $checks.validationErrorsHaveAccessibleNames = 'Passed'
    $checks.liveRegionMetadata = 'Polite in XAML; winapp returned null for LiveSetting, so runtime announcement behavior is not verified'
    Transfer-Capture 'keyboard-form-errors'
    $null = Transfer-Ui focus CancelServer
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Transfer-Wait ServersPage
    $checks.enterActivatesFocusedAction = 'Passed'
    Transfer-Invoke AllProfiles
    Transfer-Wait ProfilesPage
    $null = Transfer-Ui focus AddProfile
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Transfer-Wait ProfileDetailPage
    Wait-Focus ProfileName
    Transfer-Invoke CancelProfile
    $checks.newProfileInitialFocus = 'Passed'

    Transfer-Preview $sshPath -Ssh
    $null = Transfer-Ui scroll ImportPreview --to bottom
    $rows = @((Transfer-Ui search 'keyboard-ready').matches | Where-Object type -eq 'ListItem')
    if ($rows.Count -ne 1) { throw 'The read-only preview row is not exposed as one named list item.' }
    if ((Transfer-Ui get-property $rows[0].selector --property IsKeyboardFocusable).properties.IsKeyboardFocusable -ne 'False') { throw 'A read-only preview row unexpectedly became a tab stop.' }
    $null = Transfer-Ui focus SkipUnavailableImports
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x20)
    $null = Transfer-Ui wait-for CommitImport --property IsEnabled --value True --timeout 5000
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09)
    Wait-Focus CommitImport
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09)
    Wait-Focus CancelImport
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x09, 0x10)
    Wait-Focus CommitImport
    $null = Transfer-Ui focus CancelImport
    [TunnelDeck.Verification.ForwardNative]::Press($appProcess.MainWindowHandle, 0x0d)
    Transfer-Wait ServersPage
    $checks.importRowSemanticsAndFooterKeyboard = 'Passed'
    if (Test-Path -LiteralPath (Join-Path $dataPath 'state.json')) { throw 'Invalid form submission or cancelled import unexpectedly wrote configuration.' }
    $checks.invalidAndCancelledOperationsAreReadOnly = 'Passed'
    $passed = $true
}
catch { try { if ($appProcess -and -not $appProcess.HasExited) { Transfer-Capture 'failure' } } catch { }; throw }
finally {
    try { Transfer-Close }
    finally {
        [ordered]@{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); passed = $passed; checks = $checks; dataPath = $dataPath; narratorSpeech = 'Not measured by this UI Automation and keyboard check' } |
            ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputPath 'result.json') -Encoding utf8NoBOM
    }
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'result.json')
