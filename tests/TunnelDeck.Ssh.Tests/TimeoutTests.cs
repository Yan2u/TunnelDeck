using System.Net;
using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class TimeoutTests(SshFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainRemoteStartRetiresTransportBeforeAnyNextOperation(bool cancel)
    {
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        using TestConnection connection = fixture.CreateConnection(proxy.Port, timeout: TimeSpan.FromSeconds(5));
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var echo = new LocalEchoServer();
        await using var session = new SshControlSession(connection.Client, TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        int remotePort = await fixture.UnusedPortAsync();
        using var forward = new ForwardedPortRemote(IPAddress.Loopback, (uint)remotePort, IPAddress.Loopback, (uint)echo.Port);
        proxy.HoldServerReplies();
        Task start = session.StartAsync(forward, cancellation.Token);
        await proxy.WaitForHeldChunkAsync();
        await fixture.ExpectListeningAsync(remotePort, true);
        if (cancel) { cancellation.Cancel(); }
        Exception? error = await Record.ExceptionAsync(() => start);
        Assert.NotNull(error);
        if (cancel) { Assert.IsAssignableFrom<OperationCanceledException>(error); }
        else { Assert.True(error is SshOperationTimeoutException or TimeoutException, error.GetType().Name); }
        Assert.True(session.IsRetired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(forward));
        proxy.ResumeServerReplies();
        await fixture.ExpectListeningAsync(remotePort, false);

        using TestConnection fresh = fixture.CreateConnection();
        await fresh.Client.ConnectAsync(CancellationToken.None);
        using var retry = new ForwardedPortRemote(IPAddress.Loopback, (uint)remotePort, IPAddress.Loopback, (uint)echo.Port);
        fresh.Client.AddForwardedPort(retry);
        retry.Start();
        await fixture.AssertRemotePayloadAsync(remotePort, [4, 0, 4, 255]);
        retry.Stop();
        await fixture.ExpectListeningAsync(remotePort, false);
        output.WriteLine($"SSH-04 {(cancel ? "cancelled" : "timed out")}: real sshd had already created listener; old session retired; late reply cannot be reused; fresh session forwards successfully.");
    }
}
