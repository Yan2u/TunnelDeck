using System.Diagnostics;
using System.Net;
using Renci.SshNet;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class UpstreamBehaviorTests(SshFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task CharacterizeRemoteStopAcknowledgementAfterAnotherRemoteStart()
    {
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        using TestConnection connection = fixture.CreateConnection(proxy.Port, timeout: TimeSpan.FromSeconds(2));
        await connection.Client.ConnectAsync(CancellationToken.None);
        int firstPort = await fixture.UnusedPortAsync();
        int secondPort = await fixture.UnusedPortAsync();
        using var first = new ForwardedPortRemote(IPAddress.Loopback, (uint)firstPort, IPAddress.Loopback, 9);
        using var second = new ForwardedPortRemote(IPAddress.Loopback, (uint)secondPort, IPAddress.Loopback, 9);
        connection.Client.AddForwardedPort(first);
        connection.Client.AddForwardedPort(second);
        first.Start();
        second.Start();
        proxy.HoldServerReplies();
        var watch = Stopwatch.StartNew();
        Task stop = Task.Run(first.Stop);
        TaskCompletionSource withheldAcknowledgement = await proxy.WaitForHeldChunkAsync();
        // This is an upstream characterization, not an assertion that early completion is safe.
        bool completedBeforeReply = await Task.WhenAny(stop, Task.Delay(300)) == stop;
        withheldAcknowledgement.TrySetResult();
        proxy.ResumeServerReplies();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();
        second.Stop();
        await fixture.ExpectListeningAsync(firstPort, false);
        await fixture.ExpectListeningAsync(secondPort, false);
        output.WriteLine($"UPSTREAM: remote Stop completed before its acknowledgement was released = {completedBeforeReply}; duration including probe={watch.ElapsedMilliseconds} ms.");
        Assert.True(completedBeforeReply, "Pinned SSH.NET behavior changed: reassess the remote control adapter and ADR.");
    }
}
