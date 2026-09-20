[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $Distribution = 'Ubuntu',
    [switch] $NoFixtureBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dataPath = [IO.Path]::GetFullPath((Join-Path $repoRoot ('.test-output\m5-ui\' + [Guid]::NewGuid().ToString('N'))))
$outputPath = Join-Path $repoRoot 'artifacts\m5-review'
$statePath = Join-Path $dataPath 'state.json'
New-Item -ItemType Directory -Path $dataPath, $outputPath -Force | Out-Null
$credentialPrefix = 'TunnelDeck/Isolated/' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($dataPath.ToUpperInvariant())))
$credentialTargets = [Collections.Generic.HashSet[string]]::new()
$checks = [ordered]@{}
$fixturePath = $null
$appProcess = $null
$blackhole = $null

if (-not ('TunnelDeck.Verification.ConnectionNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TunnelDeck.Verification
{
    public static class ConnectionNative
    {
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("advapi32.dll", EntryPoint="CredReadW", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint="CredDeleteW", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
        public static bool CredentialExists(string target)
        {
            if (CredRead(target, 1, 0, out IntPtr credential)) { CredFree(credential); return true; }
            if (Marshal.GetLastWin32Error() == 1168) { return false; }
            throw new InvalidOperationException("Could not inspect the isolated test credential.");
        }
        public static void DeleteCredential(string target)
        {
            if (!target.StartsWith("TunnelDeck/Isolated/", StringComparison.Ordinal)) { throw new InvalidOperationException("Unsafe credential cleanup target."); }
            if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) { throw new InvalidOperationException("Could not delete the isolated test credential."); }
        }
        public static void Resize(IntPtr window, int width, int height)
        {
            IntPtr old = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                double scale = GetDpiForWindow(window) / 96.0;
                if (!SetWindowPos(window, IntPtr.Zero, 0, 0, (int)(width * scale), (int)(height * scale), 6)) { throw new InvalidOperationException("Could not resize the test window."); }
            }
            finally { SetThreadDpiAwarenessContext(old); }
        }
    }
}
'@
}

function Invoke-ConnectionUi {
    $result = & winapp ui @args -w $appProcess.MainWindowHandle --json
    $commandExit = $LASTEXITCODE
    $parsed = $result | ConvertFrom-Json
    $emptySearch = $args[0] -eq 'search' -and $parsed.PSObject.Properties['matchCount'] -and $parsed.matchCount -eq 0
    if ($commandExit -ne 0 -and -not $emptySearch) { throw "Connection UI verification failed: $($args[0]) $($args[1]). $result" }
    return $parsed
}
function Wait-Ui([string] $selector) {
    $result = Invoke-ConnectionUi wait-for $selector --timeout 15000
    if (-not $result.found) { throw "Expected connection UI did not appear: $selector" }
}
function Set-Secret([string] $secret) {
    # Never echo the CLI response or its arguments: ValuePattern results may include the value.
    $null = & winapp ui set-value ServerSecret $secret -w $appProcess.MainWindowHandle --json 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Could not enter the disposable credential into PasswordBox.' }
}
function Read-State {
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }
    $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream)
    try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
function Wait-State([scriptblock] $condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $state = Read-State
        if ($state -and (& $condition $state)) { return $state }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The expected server edit did not reach disk.'
}
function Remember-Target($reference) {
    $target = $credentialPrefix + '/' + ([Guid]$reference).ToString('D')
    $null = $credentialTargets.Add($target)
    return $target
}
function Open-SavedServer {
    $null = Invoke-ConnectionUi invoke AllServers
    Wait-Ui ServersPage
    $server = (Read-State).servers[0]
    $null = Invoke-ConnectionUi invoke ('ServerRow_' + ([Guid]$server.id).ToString('N'))
    Wait-Ui ServerDetailPage
}
function Select-Authentication([string] $selector) {
    $null = Invoke-ConnectionUi invoke AuthenticationMethod
    $null = Invoke-ConnectionUi invoke $selector
}
function Capture-Connection([string] $name) {
    Start-Sleep -Milliseconds 200
    $null = Invoke-ConnectionUi screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function Close-TestApp {
    if ($appProcess -and -not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(12000)) { throw 'The connection test app did not finish cleanup.' }
        if ($appProcess.ExitCode -ne 0) { throw 'The connection test app exited with an error.' }
    }
}

try {
    $fixturePath = & (Join-Path $repoRoot 'test-infra\Start-SshFixture.ps1') -Distribution $Distribution -NoBuild:$NoFixtureBuild
    $manifest = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $dataPath -Configuration $Configuration
    Wait-Ui DashboardPage
    $null = Invoke-ConnectionUi invoke AllServers
    Wait-Ui ServersPage
    $null = Invoke-ConnectionUi invoke EmptyAddResource
    Wait-Ui ServerDetailPage
    $null = Invoke-ConnectionUi invoke SaveServer
    Wait-Ui 'Enter an IP address or hostname'
    $checks.formValidation = 'Passed'

    $null = Invoke-ConnectionUi set-value ServerDisplayName 'Disposable SSH verification'
    $null = Invoke-ConnectionUi set-value ServerHost $manifest.host
    $null = Invoke-ConnectionUi set-value ServerPort ([string]$manifest.allowPort)
    $null = Invoke-ConnectionUi set-value ServerUsername $manifest.username
    Set-Secret $manifest.password
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui HostKeyApproval
    Capture-Connection 'host-key-approval'
    $null = Invoke-ConnectionUi invoke CloseButton
    Wait-Ui 'The host key was not trusted'
    if (Test-Path -LiteralPath (Join-Path $dataPath 'host-keys.json')) { throw 'Declining host trust wrote a trust file.' }
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui HostKeyApproval
    $null = Invoke-ConnectionUi invoke PrimaryButton
    Wait-Ui 'Connection successful'
    if (Test-Path -LiteralPath $statePath) { throw 'Testing an unsaved form saved server metadata.' }
    Capture-Connection 'password-test-success'
    $checks.unsavedTestAndHostApproval = 'Passed'

    $null = Invoke-ConnectionUi invoke RememberCredential
    $null = Invoke-ConnectionUi invoke SaveServer
    $saved = Wait-State { param($state) $state.servers.Count -eq 1 -and $state.servers[0].credentialReference }
    $firstReference = $saved.servers[0].credentialReference
    $firstTarget = Remember-Target $firstReference
    if (-not [TunnelDeck.Verification.ConnectionNative]::CredentialExists($firstTarget)) { throw 'Remember did not save an isolated Windows credential.' }
    $persistedText = Get-Content -LiteralPath $statePath -Raw
    if ($persistedText.Contains($manifest.password) -or $persistedText.Contains($manifest.privateKeyPassphrase)) { throw 'Server JSON contains a credential.' }
    Close-TestApp
    $appProcess = & (Join-Path $PSScriptRoot 'Run.ps1') -DataDirectory $dataPath -Configuration $Configuration
    Wait-Ui DashboardPage
    Open-SavedServer
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connection successful'
    if ((Invoke-ConnectionUi search HostKeyApproval).matchCount -ne 0) { throw 'Persisted host trust prompted again.' }
    $checks.rememberAndRestart = 'Passed'

    Set-Secret 'incorrect-disposable-ui-password'
    $null = Invoke-ConnectionUi invoke TestConnection
    $null = Invoke-ConnectionUi wait-for CredentialError --value 'SSH authentication failed' --contains --timeout 15000
    Capture-Connection 'authentication-error'
    Open-SavedServer
    [TunnelDeck.Verification.ConnectionNative]::DeleteCredential($firstTarget)
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui CredentialError
    Set-Secret $manifest.password
    $null = Invoke-ConnectionUi invoke SaveServer
    $replaced = Wait-State { param($state) $state.servers[0].credentialReference -and $state.servers[0].credentialReference -ne $firstReference }
    $passwordTarget = Remember-Target $replaced.servers[0].credentialReference
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connection successful'
    $checks.authenticationErrorAndMissingCredential = 'Passed'

    $blackhole = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $blackhole.Start()
    $blackholePort = $blackhole.LocalEndpoint.Port
    $null = Invoke-ConnectionUi set-value ServerPort ([string]$blackholePort)
    Set-Secret $manifest.password
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connecting…'
    if ((Invoke-ConnectionUi get-property ServerHost --property IsEnabled).properties.IsEnabled -ne 'False') { throw 'Connection identity fields stayed editable during a test.' }
    if ((Invoke-ConnectionUi get-property ServerDisplayName --property IsEnabled).properties.IsEnabled -ne 'True') { throw 'Display name was unnecessarily locked.' }
    if (-not $appProcess.Responding) { throw 'The UI stopped responding during SSH connect.' }
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connection test cancelled'
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connecting…'
    $null = Invoke-ConnectionUi invoke Settings
    Wait-Ui SettingsPage
    $null = Invoke-ConnectionUi invoke ThemeSelector
    $null = Invoke-ConnectionUi invoke ThemeDark
    Open-SavedServer
    $null = Invoke-ConnectionUi wait-for ServerPort --value ([string]$manifest.allowPort) --timeout 5000
    if ((Read-State).servers[0].port -ne $manifest.allowPort) { throw 'An unsaved test overwrote the saved SSH port.' }
    $connectionDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $pending = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpConnections() | Where-Object { $_.RemoteEndPoint.Port -eq $blackholePort -and $_.State -eq 'Established' })
        if ($pending.Count -eq 0) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $connectionDeadline)
    if ($pending.Count -ne 0) { throw 'Navigation left a pending connection behind.' }
    $blackhole.Stop()
    $blackhole = $null
    $checks.cancelNavigationAndIdentityGuard = 'Passed'

    Select-Authentication PrivateKeyAuthentication
    $null = Invoke-ConnectionUi set-value IdentityFilePath $manifest.privateKeyPath
    Set-Secret $manifest.privateKeyPassphrase
    $null = Invoke-ConnectionUi invoke RememberCredential
    $null = Invoke-ConnectionUi invoke SaveServer
    $encrypted = Wait-State { param($state) $state.servers[0].authMethod -eq 1 -and $state.servers[0].credentialReference }
    $keyTarget = Remember-Target $encrypted.servers[0].credentialReference
    if ([TunnelDeck.Verification.ConnectionNative]::CredentialExists($passwordTarget)) { throw 'Authentication change retained the old password.' }
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connection successful'
    Capture-Connection 'encrypted-key-dark'
    $checks.encryptedKeyAndCredentialReplacement = 'Passed'

    $null = Invoke-ConnectionUi set-value IdentityFilePath $manifest.plainPrivateKeyPath
    $null = Invoke-ConnectionUi invoke RememberCredential
    $null = Invoke-ConnectionUi invoke SaveServer
    $null = Wait-State { param($state) -not $state.servers[0].credentialReference -and $state.servers[0].identityFilePath -eq $manifest.plainPrivateKeyPath }
    if ([TunnelDeck.Verification.ConnectionNative]::CredentialExists($keyTarget)) { throw 'Forget left the passphrase in Credential Manager.' }
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui 'Connection successful'
    $null = Invoke-ConnectionUi set-value IdentityFilePath (Join-Path $dataPath 'missing-key')
    $null = Invoke-ConnectionUi invoke TestConnection
    $null = Invoke-ConnectionUi wait-for IdentityFileError --value 'The private-key file could not be read' --contains --timeout 15000
    Open-SavedServer
    [TunnelDeck.Verification.ConnectionNative]::Resize($appProcess.MainWindowHandle, 900, 700)
    Capture-Connection 'private-key-900-dark'
    [TunnelDeck.Verification.ConnectionNative]::Resize($appProcess.MainWindowHandle, 640, 600)
    Capture-Connection 'private-key-640-dark'
    [TunnelDeck.Verification.ConnectionNative]::Resize($appProcess.MainWindowHandle, 1200, 760)
    $checks.unencryptedKeyForgetAndUnreadableFile = 'Passed'
    $checks.narrowDarkForm = 'Passed'

    $null = Invoke-ConnectionUi set-value ServerPort ([string]$manifest.mutablePort)
    $null = Invoke-ConnectionUi invoke TestConnection
    Wait-Ui HostKeyApproval
    $closing = [Diagnostics.Stopwatch]::StartNew()
    Close-TestApp
    $checks.closeDuringHostApproval = 'Passed'
    $checks.closeMilliseconds = $closing.ElapsedMilliseconds
    [ordered]@{ milestone = 'M5'; recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; humanGuiReview = 'Pending' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'verification.json') -Encoding utf8
    Write-Host 'M5 native connection UI verification passed.'
}
catch {
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'verification-partial.json') -Encoding utf8
    try { if ($appProcess -and -not $appProcess.HasExited) { Capture-Connection 'verification-failure' } }
    catch { }
    throw
}
finally {
    try { Close-TestApp }
    finally {
        if ($blackhole) { $blackhole.Stop() }
        $lastState = Read-State
        if ($lastState) { foreach ($server in $lastState.servers) { if ($server.credentialReference) { $null = Remember-Target $server.credentialReference } } }
        try { foreach ($target in $credentialTargets) { [TunnelDeck.Verification.ConnectionNative]::DeleteCredential($target) } }
        finally { if ($fixturePath) { & (Join-Path $repoRoot 'test-infra\Stop-SshFixture.ps1') -ManifestPath $fixturePath } }
    }
}
