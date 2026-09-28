using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Security;
using TunnelDeck.Infrastructure.Terminal;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class ExternalSshProcess(Process process) : IAsyncDisposable
{
    public Process Process { get; } = process;

    public static async Task<ExternalSshProcess> StartAsync(SshFixture testFixture, string directory, bool trust = true)
    {
        FixtureManifest fixture = testFixture.Manifest;
        string keyPath = Path.Combine(directory, "external key");
        File.Copy(fixture.PlainPrivateKeyPath, keyPath);
        if (OperatingSystem.IsWindows())
        {
            SecurityIdentifier owner = WindowsIdentity.GetCurrent().User!;
            var security = new FileSecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(keyPath).SetAccessControl(security);
        }
        string knownHosts = Path.Combine(directory, "external_known_hosts");
        string ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        string scanned = string.Empty;
        if (trust)
        {
            // Windows 9.5 ssh-keyscan advertises an unsupported sntrup KEX on this machine.
            // Capture the public key through a connection verified against the fixture's known fingerprint.
            using TestConnection trusted = testFixture.CreateConnection();
            byte[]? publicKey = null;
            trusted.Client.HostKeyReceived += (_, args) =>
            {
                Assert.Equal(fixture.HostKeyFingerprint, HostKeyIdentity.FromKey(fixture.Host, fixture.AllowPort, args.HostKeyName, args.HostKey).Fingerprint);
                publicKey = args.HostKey;
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await trusted.Client.ConnectAsync(deadline.Token);
            Assert.NotNull(publicKey);
            scanned = $"[{fixture.Host}]:{fixture.AllowPort} ssh-ed25519 {Convert.ToBase64String(publicKey)}\n";
        }
        await File.WriteAllTextAsync(knownHosts, scanned);
        RemoteServer server = ConnectionTestScope.Server(fixture) with { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = keyPath };
        ProcessStartInfo start = TerminalCommandBuilder.Build(server, ssh, null, File.Exists);
        start.CreateNoWindow = true; start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        // Test-only options keep known_hosts isolated and make a wrong host fail without prompting.
        string quotedHosts = "\"" + knownHosts.Replace('\\', '/') + "\"";
        string[] options = ["-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=4",
            "-o", "UserKnownHostsFile=" + quotedHosts, "-o", "GlobalKnownHostsFile=" + quotedHosts];
        for (int index = options.Length - 1; index >= 0; index--) { start.ArgumentList.Insert(0, options[index]); }
        start.ArgumentList.Add("printf 'terminal-ready\\n'; read value; printf 'terminal-done\\n'");
        return new(Process.Start(start)!);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Process.HasExited)
        {
            Process.StandardInput.Close();
            try { await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { Process.Kill(entireProcessTree: true); await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        }
        Process.Dispose();
    }
}
