using System.Net.Http;
using System.Text;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class QuickForwardHttpTests(SshFixture fixture)
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task LocalhostTargetInQuickForwardCanReachAnIpv4OrIpv6OnlyWebApplication(string remoteBind)
    {
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        await using var web = new LocalHttpServer(Encoding.UTF8.GetBytes("<!doctype html><title>MobileGym fixture</title><p>Forwarding works</p>"));
        int remotePort = await fixture.UnusedPortAsync();
        RuleLaunchSnapshot remote = scope.Rule(web.Port, remotePort, ForwardMode.Remote);
        remote = remote with { Requested = remote.Requested with { RemoteHost = remoteBind } };
        var remoteStarted = await scope.Tunnels.StartAsync(remote);
        Assert.True(remoteStarted.Succeeded, remoteStarted.Tunnel.LastError?.Message);
        await fixture.ExpectListeningAsync(remotePort, true, remoteBind);

        using var port = LocalPortBlock.Reserve(1);
        port.Release(0);
        var quick = new QuickForwardModel();
        quick.Reconcile(scope.Connections.Configuration.Current!, preview: false, enabled: true);
        quick.SelectedServerId = scope.Server.Id;
        quick.PortText = port.First.ToString();
        quick.UseAdvanced = true;
        quick.RemotePortText = remotePort.ToString();
        quick.RemoteHost = "localhost";
        RuleLaunchSnapshot request = quick.Capture()!;
        var forwarded = await scope.Tunnels.StartAsync(request);
        Assert.True(forwarded.Succeeded, forwarded.Tunnel.LastError?.Message);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
        string html = await client.GetStringAsync($"http://127.0.0.1:{forwarded.Tunnel.Actual!.Local.Port}/");
        Assert.Contains("<title>MobileGym fixture</title>", html);
        Assert.Equal("localhost", forwarded.Tunnel.Requested.RemoteHost);
    }
}
