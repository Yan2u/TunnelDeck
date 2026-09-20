using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ConnectionAuthenticationTests(SshFixture fixture)
{
    [Theory]
    [InlineData("password")]
    [InlineData("plain-key")]
    [InlineData("encrypted-key")]
    public async Task RealAuthenticationClosesItsTemporarySessionAndPersistsOnlyExplicitHostTrust(string method)
    {
        await using var scope = new ConnectionTestScope();
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest);
        string? secret = fixture.Manifest.Password;
        if (method != "password")
        {
            server = server with
            {
                AuthMethod = AuthenticationMethod.PrivateKey,
                IdentityFilePath = method == "plain-key" ? fixture.Manifest.PlainPrivateKeyPath : fixture.Manifest.PrivateKeyPath,
            };
            secret = method == "plain-key" ? null : fixture.Manifest.PrivateKeyPassphrase;
        }
        ConnectionTestResult result = await scope.Manager.TestAsync(server, new(secret));
        Assert.Equal(fixture.Manifest.HostKeyFingerprint, result.HostKey.Fingerprint);
        Assert.Single(scope.Approval.Requests);
        Assert.Equal(2, scope.Factory.Opens);
        Assert.True(Assert.Single(scope.Factory.Sessions).IsDisposed);
        Assert.Equal(0, scope.Manager.TrackedConnectionCount);
        Assert.False(File.Exists(scope.Directory.StatePath));

        // Reopen the trust store and the manager to prove this is persisted trust, not an in-memory shortcut.
        var reopened = new SshConnectionManager(scope.Configuration, scope.Factory, scope.Credentials,
            new JsonHostKeyStore(scope.Directory), scope.Approval);
        try
        {
            await reopened.TestAsync(server, new(secret));
            Assert.Single(scope.Approval.Requests);
            Assert.All(scope.Factory.Sessions, session => Assert.True(session.IsDisposed));
        }
        finally { await reopened.ShutdownAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("wrong-password", ConnectionFailure.AuthenticationFailed)]
    [InlineData("wrong-passphrase", ConnectionFailure.KeyUnlockFailed)]
    [InlineData("missing-passphrase", ConnectionFailure.CredentialRequired)]
    [InlineData("missing-key", ConnectionFailure.KeyFileUnreadable)]
    public async Task AuthenticationFailuresAreTypedAndNeverExposeCredentials(string fault, ConnectionFailure expected)
    {
        await using var scope = new ConnectionTestScope();
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest);
        string? secret = "incorrect-disposable-fixture-secret";
        if (fault != "wrong-password")
        {
            server = server with
            {
                AuthMethod = AuthenticationMethod.PrivateKey,
                IdentityFilePath = fault == "missing-key" ? Path.Combine(scope.Directory.RootPath, "missing-identity") : fixture.Manifest.PrivateKeyPath,
            };
            if (fault == "missing-passphrase") { secret = null; }
        }
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => scope.Manager.TestAsync(server, new(secret)));
        Assert.Equal(expected, error.Failure);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(fixture.Manifest.Password, error.ToString());
        Assert.DoesNotContain(fixture.Manifest.PrivateKeyPassphrase, error.ToString());
        Assert.Empty(scope.Factory.Sessions);
        Assert.Equal(0, scope.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task DecliningRealUnknownHostNeverCreatesAnAuthenticatedSessionOrSavesTrust()
    {
        await using var scope = new ConnectionTestScope();
        scope.Approval.Handle = (_, _) => Task.FromResult(false);
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => scope.Manager.TestAsync(
            ConnectionTestScope.Server(fixture.Manifest), new(fixture.Manifest.Password)));
        Assert.Equal(ConnectionFailure.HostKeyDeclined, error.Failure);
        Assert.Equal(1, scope.Factory.Opens);
        Assert.Empty(scope.Factory.Sessions);
        Assert.False(File.Exists(scope.Directory.HostKeysPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHostKeyChangeIsRefusedIncludingAfterTheUserApproves(bool duringApproval)
    {
        await fixture.PostAsync("restart", new { server = "mutable", alternateKey = false });
        await using var scope = new ConnectionTestScope();
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest, fixture.Manifest.MutablePort);
        try
        {
            if (duringApproval)
            {
                scope.Approval.Handle = async (_, _) => { await fixture.PostAsync("restart", new { server = "mutable", alternateKey = true }); return true; };
            }
            else
            {
                await scope.Manager.TestAsync(server, new(fixture.Manifest.Password));
                await fixture.PostAsync("restart", new { server = "mutable", alternateKey = true });
            }
            ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => scope.Manager.TestAsync(server, new(fixture.Manifest.Password)));
            Assert.Equal(ConnectionFailure.HostKeyChanged, error.Failure);
            Assert.Single(scope.Approval.Requests);
            Assert.Equal(duringApproval ? 0 : 1, scope.Factory.Sessions.Count);
            Assert.All(scope.Factory.Sessions, session => Assert.True(session.IsDisposed));
        }
        finally { await fixture.PostAsync("restart", new { server = "mutable", alternateKey = false }); }
    }
}
