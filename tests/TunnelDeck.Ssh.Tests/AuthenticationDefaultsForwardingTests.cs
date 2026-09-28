using TunnelDeck.Core.Models;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class AuthenticationDefaultsForwardingTests(SshFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedDefaultOrExplicitKeyAuthenticatesAndForwardsWithoutASavedPassword(bool explicitIdentity)
    {
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        await scope.Connections.Configuration.UpdateSettingsAsync(settings => settings with
        {
            DefaultAuthenticationMethod = explicitIdentity ? AuthenticationMethod.Password : AuthenticationMethod.PrivateKey,
            DefaultIdentityFilePath = explicitIdentity ? null : fixture.Manifest.PlainPrivateKeyPath,
        });
        var endpoint = ConnectionTestScope.Server(fixture.Manifest);
        string text = $"Host imported-key\n HostName {endpoint.Host}\n User {endpoint.Username}\n Port {endpoint.Port}\n" +
            (explicitIdentity ? $" IdentityFile \"{fixture.Manifest.PlainPrivateKeyPath}\"\n" : string.Empty);
        ImportPlan plan = SshConfigParser.Plan(text, scope.Connections.Configuration.Current!, Path.GetTempPath(), File.Exists);
        var server = Assert.Single(plan.Servers);
        Assert.Equal(AuthenticationMethod.PrivateKey, server.AuthMethod);
        Assert.Equal(fixture.Manifest.PlainPrivateKeyPath, server.IdentityFilePath);
        Assert.Null(server.CredentialReference);
        await new ResourceImportService(scope.Connections.Configuration, scope.Connections.Manager.Work, File.Exists).CommitAsync(plan);
        using var port = LocalPortBlock.Reserve(1);
        port.Release(0);
        var started = await scope.Tunnels.StartAsync(scope.Rule(port.First, fixture.Manifest.EchoPort, server: server));
        Assert.True(started.Succeeded, started.Tunnel.LastError?.Message);
        await LocalEchoServer.AssertPayloadAsync(started.Tunnel.Actual!.Local.Port, [12, 0, 255, 1, 2]);
    }
}
