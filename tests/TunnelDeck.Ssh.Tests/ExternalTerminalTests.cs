using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ExternalTerminalTests(SshFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealOpenSshAuthenticationAndTerminalLifetimeAreIndependentOfForwarding(bool closeApplicationFirst)
    {
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        var forward = await scope.Tunnels.StartAsync(scope.Rule(ports.First, fixture.Manifest.EchoPort));
        Assert.True(forward.Succeeded);
        await using var terminal = await ExternalSshProcess.StartAsync(fixture, scope.Connections.Directory.RootPath);
        Assert.Equal("terminal-ready", await terminal.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await LocalEchoServer.AssertPayloadAsync(ports.First, [11, 9, 1, 255]);
        if (closeApplicationFirst)
        {
            await scope.Tunnels.ShutdownAsync(CancellationToken.None);
            await scope.Connections.Manager.ShutdownAsync(CancellationToken.None);
            Assert.False(terminal.Process.HasExited);
        }
        await terminal.Process.StandardInput.WriteLineAsync("complete");
        await terminal.Process.StandardInput.FlushAsync();
        Assert.Equal("terminal-done", await terminal.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await terminal.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, terminal.Process.ExitCode);
        if (!closeApplicationFirst)
        {
            Assert.True(scope.Tunnels.Current.Tunnels.Single().IsRunning);
            await LocalEchoServer.AssertPayloadAsync(ports.First, [11, 9, 2, 255]);
        }
    }

    [Fact]
    public async Task AppHostTrustDoesNotAutomaticallyTrustTheExternalOpenSshSession()
    {
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        Assert.True((await scope.Tunnels.StartAsync(scope.Rule(ports.First, fixture.Manifest.EchoPort))).Succeeded);
        await using var terminal = await ExternalSshProcess.StartAsync(fixture, scope.Connections.Directory.RootPath, trust: false);
        Task<string> output = terminal.Process.StandardOutput.ReadToEndAsync(), errors = terminal.Process.StandardError.ReadToEndAsync();
        await terminal.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(255, terminal.Process.ExitCode);
        Assert.DoesNotContain("terminal-ready", await output);
        Assert.Contains("Host key verification failed", await errors);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [1, 1, 9, 255]);
    }
}
