using Renci.SshNet.Common;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class AuthenticationTests(SshFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitlyTrustedHostSupportsPasswordAndEncryptedPrivateKey(bool privateKey)
    {
        using TestConnection connection = fixture.CreateConnection(privateKey: privateKey);
        await connection.Client.ConnectAsync(CancellationToken.None);
        Assert.True(connection.Client.IsConnected);
        Assert.False(connection.Verifier.WasRejected);
        Assert.Equal(fixture.Manifest.HostKeyFingerprint, connection.Verifier.PresentedKey?.Fingerprint);
        output.WriteLine(privateKey ? "AUTH: encrypted Ed25519 key authentication passed." : "AUTH: password authentication passed.");
    }

    [Fact]
    public async Task UnknownHostIsRejectedBeforeAuthentication()
    {
        using TestConnection connection = fixture.CreateConnection(trust: false);
        await Assert.ThrowsAsync<SshConnectionException>(() => connection.Client.ConnectAsync(CancellationToken.None));
        Assert.False(connection.Client.IsConnected);
        Assert.True(connection.Verifier.WasRejected);
        Assert.NotNull(connection.Verifier.PresentedKey);
        Assert.False(connection.Client.ConnectionInfo.IsAuthenticated);
        output.WriteLine("AUTH-01 unknown host: key captured, handshake rejected, IsAuthenticated=false.");
    }

    [Fact]
    public async Task ChangedHostKeyOnTheSameEndpointIsRejected()
    {
        await fixture.PostAsync("restart", new { server = "mutable", alternateKey = false });
        using (TestConnection original = fixture.CreateConnection(fixture.Manifest.MutablePort))
        {
            await original.Client.ConnectAsync(CancellationToken.None);
            Assert.True(original.Client.IsConnected);
        }
        await fixture.PostAsync("restart", new { server = "mutable", alternateKey = true });
        try
        {
            using TestConnection changed = fixture.CreateConnection(fixture.Manifest.MutablePort);
            await Assert.ThrowsAsync<SshConnectionException>(() => changed.Client.ConnectAsync(CancellationToken.None));
            Assert.True(changed.Verifier.WasRejected);
            Assert.False(changed.Client.ConnectionInfo.IsAuthenticated);
            Assert.Equal(fixture.Manifest.AlternateHostKeyFingerprint, changed.Verifier.PresentedKey?.Fingerprint);
            output.WriteLine("AUTH-01 changed host: same host/port, replacement server key rejected before authentication.");
        }
        finally
        {
            await fixture.PostAsync("restart", new { server = "mutable", alternateKey = false });
        }
    }

    [Fact]
    public async Task AlreadyCancelledConnectCannotCreateASession()
    {
        using TestConnection connection = fixture.CreateConnection();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.Client.ConnectAsync(cancellation.Token));
        Assert.False(connection.Client.IsConnected);
    }
}
