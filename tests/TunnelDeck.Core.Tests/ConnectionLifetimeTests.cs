using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ConnectionLifetimeTests
{
    [Fact]
    public async Task ConcurrentLocalRequestsCoalesceAndLastReleaseClosesTheTransport()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var entered = Signal();
        var release = Signal();
        fixture.Factory.BeforeConnect = async (_, _) => { entered.TrySetResult(); await release.Task; };
        Task<SshSessionLease>[] requests = Enumerable.Range(0, 12).Select(_ => fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(2, fixture.Factory.Requests.Count); // One rejecting probe, one verified authentication.
            Assert.True(fixture.Manager.IsServerInUse(DomainData.Id(1)));
        }
        finally { release.TrySetResult(); }
        SshSessionLease[] leases = await Task.WhenAll(requests);
        Assert.All(leases, lease => Assert.Same(leases[0].Session, lease.Session));
        var session = Assert.Single(fixture.Factory.Sessions);
        foreach (SshSessionLease lease in leases.Skip(1)) { await lease.DisposeAsync(); }
        Assert.Equal(0, session.DisposeCount);
        await leases[0].DisposeAsync();
        await leases[0].DisposeAsync();
        Assert.Equal(1, session.DisposeCount);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
        Assert.False(fixture.Manager.IsServerInUse(DomainData.Id(1)));
    }

    [Fact]
    public async Task RemoteRulesAlwaysOwnSeparateTransportsFromEachOtherAndLocalRules()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        SshSessionLease[] leases = await Task.WhenAll(new[] { ForwardMode.Local, ForwardMode.Remote, ForwardMode.Remote }
            .Select(mode => fixture.Manager.AcquireAsync(DomainData.Id(1), mode, ConnectionFixture.Input)));
        Assert.Equal(3, leases.Select(lease => lease.Session).Distinct().Count());
        await leases[1].DisposeAsync();
        Assert.False(leases[0].Session.Disconnected.IsCancellationRequested);
        Assert.False(leases[2].Session.Disconnected.IsCancellationRequested);
        await leases[0].DisposeAsync();
        await leases[2].DisposeAsync();
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task OneCancelledWaiterDoesNotCancelOtherWaiters()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var entered = Signal();
        var release = Signal();
        CancellationToken connectToken = default;
        fixture.Factory.BeforeConnect = async (_, token) => { connectToken = token; entered.TrySetResult(); await release.Task; };
        using var cancellation = new CancellationTokenSource();
        Task<SshSessionLease> first = fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<SshSessionLease> second = fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(connectToken.IsCancellationRequested);
        }
        finally { release.TrySetResult(); }
        await using SshSessionLease lease = await second;
        Assert.Single(fixture.Factory.Sessions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastCancellationOrShutdownOwnsAndDisposesLateSuccess(bool shutdown)
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        var entered = Signal();
        var release = Signal();
        fixture.Factory.BeforeConnect = async (_, _) => { entered.TrySetResult(); await release.Task; };
        using var cancellation = new CancellationTokenSource();
        Task<SshSessionLease> request = fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? closing = null;
        try
        {
            if (shutdown) { closing = fixture.Manager.ShutdownAsync(CancellationToken.None); }
            else { cancellation.Cancel(); }
            Assert.False(request.IsCompleted);
            Assert.Equal(1, fixture.Manager.TrackedConnectionCount);
            using var deadline = new CancellationTokenSource();
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Manager.ShutdownAsync(deadline.Token));
            Assert.Equal(1, fixture.Manager.TrackedConnectionCount);
        }
        finally { release.TrySetResult(); }
        Exception? failure = await Record.ExceptionAsync(() => request);
        Assert.True(failure is OperationCanceledException or ConnectionException);
        if (closing is not null) { await closing; }
        await fixture.Manager.ShutdownAsync(CancellationToken.None);
        Assert.Equal(1, Assert.Single(fixture.Factory.Sessions).DisposeCount);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task DisconnectRetiresTheSessionAndNextRequestUsesAFreshTransport()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        await using SshSessionLease first = await fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        var session = Assert.IsType<ConnectionFixture.TestSession>(first.Session);
        session.Disconnect();
        await session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using SshSessionLease second = await fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        Assert.NotSame(first.Session, second.Session);
        Assert.False(second.Session.Disconnected.IsCancellationRequested);
    }

    [Fact]
    public async Task FailedDisposalRemainsTrackedAndShutdownRetriesIt()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        fixture.Factory.DisposeSession = session => session.DisposeCount == 1 ? Task.FromException(new IOException("Synthetic cleanup failure.")) : Task.CompletedTask;
        SshSessionLease lease = await fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        Assert.Equal(1, fixture.Manager.TrackedConnectionCount);
        Assert.True(fixture.Manager.IsServerInUse(DomainData.Id(1)));
        await fixture.Manager.ShutdownAsync(CancellationToken.None);
        Assert.Equal(2, Assert.Single(fixture.Factory.Sessions).DisposeCount);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task AFailedLeaseDisposalCanBeRetriedAndReleasesItsOriginalOwnership()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        fixture.Factory.DisposeSession = session => session.DisposeCount == 1 ? Task.FromException(new IOException("Synthetic cleanup failure.")) : Task.CompletedTask;
        SshSessionLease lease = await fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        Assert.Equal(1, fixture.Manager.TrackedConnectionCount);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(2, Assert.Single(fixture.Factory.Sessions).DisposeCount);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
        Assert.False(fixture.Manager.IsServerInUse(DomainData.Id(1)));
        fixture.Factory.DisposeSession = null;
        await using SshSessionLease next = await fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Local, ConnectionFixture.Input);
        Assert.NotSame(lease.Session, next.Session);
    }

    [Fact]
    public async Task TemporaryTestUsesUnsavedValuesAndDoesNotReuseOrLeaveAnIdleSession()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        RemoteServer original = DomainData.Server();
        RemoteServer draft = original with { Host = "unsaved.example", Port = 2222 };
        await fixture.Manager.TestAsync(draft, ConnectionFixture.Input);
        Assert.Equal(original, fixture.Configuration.Current!.Servers[0]);
        Assert.Equal(1, Assert.Single(fixture.Factory.Sessions).DisposeCount);
        Assert.All(fixture.Factory.Requests, request => Assert.Equal(draft, request.Server));
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
        Assert.Empty(fixture.Configuration.Current.RecentActivity);
        Assert.Equal(0, fixture.Configuration.Current.Statistics.TotalForwards);
        await using SshSessionLease active = await fixture.Manager.AcquireAsync(original.Id, ForwardMode.Local, ConnectionFixture.Input);
        await fixture.Manager.TestAsync(original, ConnectionFixture.Input);
        Assert.Equal(3, fixture.Factory.Sessions.Count);
        Assert.False(active.Session.Disconnected.IsCancellationRequested);
        Assert.Equal(1, fixture.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task PreviewCannotOpenEvenAnExplicitUnsavedEndpoint()
    {
        await using var fixture = await ConnectionFixture.CreateAsync(networkEnabled: false);
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input));
        Assert.Equal(ConnectionFailure.PreviewMode, error.Failure);
        Assert.Empty(fixture.Factory.Requests);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
