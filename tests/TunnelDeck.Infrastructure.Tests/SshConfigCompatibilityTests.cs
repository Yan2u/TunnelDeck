using System.Diagnostics;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class SshConfigCompatibilityTests
{
    [Theory]
    [InlineData("User name#suffix")]
    [InlineData("User 'name'")]
    [InlineData("User \"name\"suffix")]
    [InlineData("User domain\\user")]
    [InlineData("User \"name\\\"quote\"")]
    [InlineData("User 'name\\\"quote'")]
    [InlineData("User \"name\\'quote\"")]
    [InlineData("User 'domain\\\\user'")]
    public async Task SupportedStaticValuesMatchOpenSshFirstValueSemantics(string userLine)
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string path = Path.Combine(fixture.Directory.RootPath, "static.config");
        // The entire fixture is controlled static text; no Include, Match or command options are passed to ssh -G.
        string text = $"Host alpha beta\n HostName fixture.example\n {userLine}\n Port 2222\nHost *\n User ignored\n Port 3333\n";
        await File.WriteAllTextAsync(path, text);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-G", "-F", path, "alpha" }) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
        string result = await output;
        Assert.Empty(await errors);
        var imported = SshConfigParser.Plan(text, new(), fixture.Directory.RootPath, _ => true).Servers[0];
        Assert.Contains($"user {imported.Username}\n", result.Replace("\r\n", "\n"));
        Assert.Contains($"hostname {imported.Host}\n", result.Replace("\r\n", "\n"));
        Assert.Contains($"port {imported.Port}\n", result.Replace("\r\n", "\n"));
    }
}
