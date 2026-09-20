using System.Text.Json;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ConnectionAuthenticationTests
{
    [Fact]
    public async Task DecliningUnknownKeyNeverAuthenticatesOrSavesTrust()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        fixture.Approval.Handle = (_, _) => Task.FromResult(false);
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input));
        Assert.Equal(ConnectionFailure.HostKeyDeclined, error.Failure);
        Assert.Single(fixture.Factory.Requests);
        Assert.Empty(fixture.Factory.Sessions);
        Assert.Equal(0, fixture.HostKeys.Count);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task ApprovedKeyIsSavedAndReconnectionUsesTheExactFingerprintWithoutPromptingAgain()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        ConnectionTestResult first = await fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        ConnectionTestResult second = await fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        Assert.True(first.HostKey.Matches(second.HostKey));
        Assert.Single(fixture.Approval.Requests);
        Assert.Equal(1, fixture.HostKeys.Count);
        Assert.All(fixture.Factory.Requests.Where(request => request.TrustedKey is not null), request => Assert.True(first.HostKey.Matches(request.TrustedKey!)));
        Assert.All(fixture.Factory.Sessions, session => Assert.Equal(1, session.DisposeCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedKeyIsRejectedIncludingAChangeWhileApprovalWasOpen(bool duringApproval)
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        RemoteServer server = DomainData.Server();
        if (duringApproval)
        {
            fixture.Approval.Handle = (_, _) =>
            {
                fixture.Factory.KeyFor = candidate => ConnectionFixture.TestSessionFactory.Key(candidate, 2);
                return Task.FromResult(true);
            };
        }
        else
        {
            await fixture.HostKeys.TrustAsync(new(ConnectionFixture.TestSessionFactory.Key(server, 2), DomainData.Now));
        }
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(server, ConnectionFixture.Input));
        Assert.Equal(ConnectionFailure.HostKeyChanged, error.Failure);
        Assert.Empty(fixture.Factory.Sessions);
        Assert.Equal(duringApproval ? 1 : 0, fixture.Approval.Requests.Count);
        Assert.Equal(duringApproval ? 2 : 1, fixture.Factory.Requests.Count);
    }

    [Fact]
    public async Task ApprovalWaitDoesNotHoldServerEditOrOtherServerConnectionLocks()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Approval.Handle = (request, token) =>
        {
            if (request.Key.Host != DomainData.Server().Host) { return Task.FromResult(true); }
            entered.TrySetResult();
            return decision.Task.WaitAsync(token);
        };
        Task<ConnectionTestResult> first = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await fixture.Manager.SaveServerAsync(DomainData.Server() with { DisplayName = "While confirming" }, CredentialChange.Keep).WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Manager.TestAsync(DomainData.Server(2), ConnectionFixture.Input).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(first.IsCompleted);
            ConnectionException blocked = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.SaveServerAsync(
                DomainData.Server() with { Host = "other.example" }, CredentialChange.Keep));
            Assert.Equal(ConnectionFailure.ServerInUse, blocked.Failure);
        }
        finally { decision.TrySetResult(true); }
        await first;
    }

    [Fact]
    public async Task ClosingDuringApprovalCancelsThePromptWithoutGrantingTrust()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Approval.Handle = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return true; };
        Task<ConnectionTestResult> test = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Manager.ShutdownAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
        Assert.Equal(0, fixture.HostKeys.Count);
        Assert.Single(fixture.Factory.Requests);
        Assert.Empty(fixture.Factory.Sessions);
    }

    [Fact]
    public async Task UnreadableTrustFailsClosedWithoutShowingAnApprovalPrompt()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        fixture.HostKeys.Fail = true;
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input));
        Assert.Equal(ConnectionFailure.TrustStoreUnavailable, error.Failure);
        Assert.Empty(fixture.Approval.Requests);
        Assert.Empty(fixture.Factory.Sessions);
    }

    [Theory]
    [InlineData(AuthenticationMethod.Password)]
    [InlineData(AuthenticationMethod.PrivateKey)]
    public async Task MissingSavedCredentialRequiresInlineInputBeforeOpeningTheNetwork(AuthenticationMethod method)
    {
        RemoteServer server = DomainData.Server() with { AuthMethod = method, IdentityFilePath = method == AuthenticationMethod.PrivateKey ? "test-key" : null, CredentialReference = Guid.NewGuid() };
        await using var fixture = await ConnectionFixture.CreateAsync(true, server);
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(server));
        Assert.Equal(ConnectionFailure.CredentialRequired, error.Failure);
        Assert.Empty(fixture.Factory.Requests);
        await fixture.Manager.TestAsync(server, ConnectionFixture.Input);
        Assert.Single(fixture.Factory.Sessions);
    }

    [Fact]
    public async Task SavedCredentialsAreResolvedAtUseAndSecretsStayOutOfDiagnosticFormattingAndJson()
    {
        RemoteServer server = DomainData.Server() with { CredentialReference = Guid.NewGuid() };
        await using var fixture = await ConnectionFixture.CreateAsync(true, server);
        string secret = "unique-transient-fixture-secret";
        fixture.Credentials.Values[server.CredentialReference!.Value] = secret;
        await fixture.Manager.TestAsync(server);
        Assert.All(fixture.Factory.Requests, request => Assert.Equal(secret, request.Credentials.Secret));
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(fixture.Configuration.Current));
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(new ConnectionCredentials(secret)));
        Assert.DoesNotContain(secret, new ConnectionCredentials(secret).ToString());
        Assert.All(Enum.GetValues<ConnectionFailure>(), failure => Assert.DoesNotContain(secret, new ConnectionException(failure).ToString()));
    }
}
