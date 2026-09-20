using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ConnectionIdentityTests
{
    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("username")]
    [InlineData("method")]
    [InlineData("key-file")]
    [InlineData("credential-reference")]
    [InlineData("remember")]
    [InlineData("forget")]
    public async Task PendingWorkWithoutATransportStillGuardsEveryIdentityField(string edit)
    {
        RemoteServer original = DomainData.Server() with { CredentialReference = Guid.NewGuid() };
        await using var fixture = await ConnectionFixture.CreateAsync(true, original);
        using ServerUseLease reservation = await fixture.Manager.ReserveAsync(original.Id);
        RemoteServer candidate = edit switch
        {
            "host" => original with { Host = "different.example" },
            "port" => original with { Port = 2222 },
            "username" => original with { Username = "different" },
            "method" => original with { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = "test-key" },
            "key-file" => original with { IdentityFilePath = "test-key" },
            "credential-reference" => original with { CredentialReference = Guid.NewGuid() },
            _ => original,
        };
        CredentialChange change = edit switch
        {
            "remember" => CredentialChange.Remember("disposable-new-input"),
            "forget" => CredentialChange.Forget,
            _ => CredentialChange.Keep,
        };
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.SaveServerAsync(candidate, change));
        Assert.Equal(ConnectionFailure.ServerInUse, error.Failure);
        Assert.Equal(original, fixture.Configuration.Current!.Servers[0]);
        Assert.Empty(fixture.Factory.Requests);
    }

    [Fact]
    public async Task DisplayNameCanChangeWithoutReplacingASharedSession()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        RemoteServer original = DomainData.Server();
        await using SshSessionLease first = await fixture.Manager.AcquireAsync(original.Id, ForwardMode.Local, ConnectionFixture.Input);
        await fixture.Manager.SaveServerAsync(original with { DisplayName = "Renamed" }, CredentialChange.Keep);
        await using SshSessionLease second = await fixture.Manager.AcquireAsync(original.Id, ForwardMode.Local, ConnectionFixture.Input);
        Assert.Same(first.Session, second.Session);
        Assert.Equal("Renamed", fixture.Configuration.Current!.Servers[0].DisplayName);
        Assert.Equal(2, fixture.Factory.Requests.Count);
    }

    [Fact]
    public async Task StartingConnectionCannotSlipBetweenIdentityCheckAndSaveCommit()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.BeforeCommit = async () => { saving.TrySetResult(); await commit.Task; };
        RemoteServer updated = DomainData.Server() with { Host = "new-target.example" };
        Task save = fixture.Manager.SaveServerAsync(updated, CredentialChange.Forget);
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<SshSessionLease> connecting = fixture.Manager.AcquireAsync(updated.Id, ForwardMode.Local, ConnectionFixture.Input);
        try { Assert.False(connecting.IsCompleted); Assert.Empty(fixture.Factory.Requests); }
        finally { commit.TrySetResult(); }
        await save;
        await using SshSessionLease lease = await connecting;
        Assert.All(fixture.Factory.Requests, request => Assert.Equal(updated.Host, request.Server.Host));
    }

    [Fact]
    public async Task AnUnsavedTestCannotReplaceTheIdentityReservedByActiveWork()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        using ServerUseLease reservation = await fixture.Manager.ReserveAsync(DomainData.Id(1));
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(
            DomainData.Server() with { Host = "other.example" }, ConnectionFixture.Input));
        Assert.Equal(ConnectionFailure.ServerInUse, error.Failure);
        Assert.Empty(fixture.Factory.Requests);
    }

    [Fact]
    public async Task AuthenticationTargetChangesCannotImplicitlyReuseSavedCredentials()
    {
        RemoteServer original = DomainData.Server() with { CredentialReference = Guid.NewGuid() };
        await using var fixture = await ConnectionFixture.CreateAsync(true, original);
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.SaveServerAsync(
            original with { Host = "other.example" }, CredentialChange.Keep));
        Assert.Equal(ConnectionFailure.CredentialRequired, error.Failure);
    }

    [Fact]
    public async Task ShutdownPreventsBothNewConnectionsAndServerEdits()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        await fixture.Manager.ShutdownAsync(CancellationToken.None);
        ConnectionException connect = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input));
        ConnectionException edit = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.SaveServerAsync(DomainData.Server(), CredentialChange.Keep));
        Assert.Equal(ConnectionFailure.ShuttingDown, connect.Failure);
        Assert.Equal(ConnectionFailure.ShuttingDown, edit.Failure);
    }
}
