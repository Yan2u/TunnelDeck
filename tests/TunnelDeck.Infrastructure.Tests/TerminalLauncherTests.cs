using System.ComponentModel;
using System.Diagnostics;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Infrastructure.Terminal;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class TerminalLauncherTests
{
    private const string Ssh = @"C:\tools\ssh.exe", Terminal = @"C:\tools\wt.exe";

    [Theory]
    [InlineData("host whitespace", "dev")]
    [InlineData("example.org;calc", "dev")]
    [InlineData("-oProxyCommand=anything", "dev")]
    [InlineData("example.org", "user whitespace")]
    [InlineData("example.org", "dev\nmore")]
    public void InvalidServerArgumentsAreRejectedBeforeStartingAProcess(string host, string username)
    {
        RemoteServer server = StorageFixture.Server() with { Host = host, Username = username };
        Assert.Throws<TerminalLaunchException>(() => TerminalCommandBuilder.Build(server, Ssh, Terminal, _ => true));
    }

    [Fact]
    public void CredentialsNeverEnterArgumentsEnvironmentOrCommandTemplates()
    {
        RemoteServer server = StorageFixture.Server() with { CredentialReference = Guid.NewGuid() };
        ProcessStartInfo start = TerminalCommandBuilder.Build(server, Ssh, Terminal, _ => true);
        Assert.Equal(Terminal, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Empty(start.Arguments);
        Assert.Contains("-F", start.ArgumentList); Assert.Contains("none", start.ArgumentList);
        Assert.DoesNotContain(server.CredentialReference!.Value.ToString(), string.Join(" ", start.ArgumentList));
        Assert.DoesNotContain(start.Environment.Keys, key => key.StartsWith("TUNNELDECK_PASSWORD", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["--", server.Host], start.ArgumentList.TakeLast(2).ToArray());
    }

    [Theory]
    [InlineData("semi;colon")]
    [InlineData("quoted\"name")]
    public void WindowsTerminalMetacharactersUseLiteralConsoleArguments(string username)
    {
        RemoteServer server = StorageFixture.Server() with { Username = username };
        ProcessStartInfo start = TerminalCommandBuilder.Build(server, Ssh, Terminal, _ => true);
        Assert.Equal(Ssh, start.FileName);
        Assert.Equal(username, start.ArgumentList[start.ArgumentList.IndexOf("-l") + 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnstartableTerminalFallsBackToConsole(bool failsToStart)
    {
        var starts = new List<string>();
        var launcher = new WindowsTerminalLauncher(Ssh, Terminal, path => path == Ssh || failsToStart, start =>
        {
            starts.Add(start.FileName);
            if (start.FileName == Terminal) { throw new Win32Exception("Injected terminal alias failure."); }
            return Process.GetCurrentProcess();
        });
        await launcher.OpenAsync(StorageFixture.Server());
        Assert.Equal(Ssh, starts[^1]);
        Assert.Equal(failsToStart ? 2 : 1, starts.Count);
    }

    [Fact]
    public async Task MissingSshAndProcessFailureHaveActionableErrors()
    {
        var missing = new WindowsTerminalLauncher(Ssh, Terminal, _ => false, _ => throw new Exception("Should not start"));
        Assert.Contains("Optional Features", (await Assert.ThrowsAsync<TerminalLaunchException>(() => missing.OpenAsync(StorageFixture.Server()))).Message);
        var failing = new WindowsTerminalLauncher(Ssh, Terminal, _ => true, _ => throw new Win32Exception("private backend detail"));
        TerminalLaunchException error = await Assert.ThrowsAsync<TerminalLaunchException>(() => failing.OpenAsync(StorageFixture.Server()));
        Assert.Contains("could not be opened", error.Message);
        Assert.DoesNotContain("private backend detail", error.Message);
    }

    [Fact]
    public async Task MissingKeyAndCancellationDoNotLaunchAnything()
    {
        int starts = 0;
        var launcher = new WindowsTerminalLauncher(Ssh, null, path => path == Ssh, _ => { starts++; return Process.GetCurrentProcess(); });
        RemoteServer server = StorageFixture.Server() with { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = @"C:\missing\key" };
        await Assert.ThrowsAsync<TerminalLaunchException>(() => launcher.OpenAsync(server));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.OpenAsync(StorageFixture.Server(), cancellation.Token));
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData("开发用户", "key with spaces")]
    [InlineData("name;new-tab", "key;semicolon")]
    [InlineData("name\"quote", "key")]
    [InlineData("-oProxyCommand=anything", "key")]
    [InlineData("domain\\user", "key")]
    public async Task RealArgvReceiverGetsLiteralValuesWithoutExecutingAnyShell(string username, string keyName)
    {
        using var probe = new ProcessProbeFixture();
        string key = Path.Combine(probe.Root, keyName);
        await File.WriteAllTextAsync(key, "non-secret fixture path");
        RemoteServer server = StorageFixture.Server() with
        { Username = username, AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = key };
        ProcessStartInfo command = TerminalCommandBuilder.Build(server, probe.Executable, null, File.Exists);
        string[] expected = command.ArgumentList.ToArray();
        command.CreateNoWindow = true;
        using Process process = Process.Start(command)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(expected, await probe.ReadArgumentsAsync());
        Assert.False(File.Exists(Path.Combine(probe.Root, "must-not-run")));
    }

    [Fact]
    public async Task AvailableWindowsTerminalOrConsolePreservesWhitespaceAndUnicode()
    {
        using var probe = new ProcessProbeFixture();
        string terminal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        string key = Path.Combine(probe.Root, "开发 key");
        await File.WriteAllTextAsync(key, "non-secret fixture path");
        RemoteServer server = StorageFixture.Server() with
        { Username = "开发用户", AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = key };
        ProcessStartInfo command = TerminalCommandBuilder.Build(server, probe.Executable, File.Exists(terminal) ? terminal : null, File.Exists);
        string[] expected = TerminalCommandBuilder.Build(server, probe.Executable, null, File.Exists).ArgumentList.ToArray();
        string title = "TunnelDeck argv verification " + Guid.NewGuid().ToString("N");
        if (command.FileName == terminal)
        {
            command.ArgumentList.Insert(3, "--title"); command.ArgumentList.Insert(4, title);
            command.ArgumentList.Insert(5, "--suppressApplicationTitle");
        }
        else { command.CreateNoWindow = true; }
        try
        {
            using Process process = Process.Start(command)!;
            Assert.Equal(expected, await probe.ReadArgumentsAsync());
        }
        finally { ProcessProbeFixture.CloseTestWindow(title); }
    }
}
