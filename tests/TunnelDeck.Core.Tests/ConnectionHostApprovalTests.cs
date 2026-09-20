using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ConnectionHostApprovalTests
{
    [Fact]
    public async Task ConcurrentRemoteRulesShareApprovalButKeepSeparateAuthenticatedTransports()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        using var decision = new AsyncGate();
        fixture.Approval.Handle = async (_, token) => { await decision.WaitAsync(token); return true; };
        Task<SshSessionLease>[] starts = Enumerable.Range(0, 4)
            .Select(_ => fixture.Manager.AcquireAsync(DomainData.Id(1), ForwardMode.Remote, ConnectionFixture.Input)).ToArray();
        await decision.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        decision.Release.TrySetResult();
        SshSessionLease[] leases = await Task.WhenAll(starts);
        try
        {
            Assert.Single(fixture.Approval.Requests);
            Assert.Equal(1, fixture.HostKeys.Count);
            Assert.Equal(4, leases.Select(lease => lease.Session).Distinct().Count());
        }
        finally { foreach (SshSessionLease lease in leases) { await lease.DisposeAsync(); } }
    }

    [Fact]
    public async Task CancellingOneIndependentConnectionKeepsTheOtherApprovalWaiterAlive()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        using var decision = new AsyncGate();
        CancellationToken promptToken = default;
        fixture.Approval.Handle = async (_, token) => { promptToken = token; await decision.WaitAsync(token); return true; };
        using var cancellation = new CancellationTokenSource();
        Task<ConnectionTestResult> first = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input, cancellationToken: cancellation.Token);
        await decision.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var joined = new ApprovalProgress();
        Task<ConnectionTestResult> second = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input, joined);
        await joined.Joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(promptToken.IsCancellationRequested);
            Assert.Single(fixture.Approval.Requests);
            Assert.Empty(fixture.Factory.Sessions);
        }
        finally { decision.Release.TrySetResult(); }
        await second;
        Assert.Single(fixture.Factory.Sessions);
        Assert.Equal(1, fixture.HostKeys.Count);
    }

    [Fact]
    public async Task LastCancellationDrainsAnUncooperativePromptAndCannotGrantLateTrust()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        using var decision = new AsyncGate();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Approval.Handle = async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await decision.WaitAsync();
            return true;
        };
        using var cancellation = new CancellationTokenSource();
        Task<ConnectionTestResult> first = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input, cancellationToken: cancellation.Token);
        await decision.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.IsCompleted);
        decision.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(0, fixture.HostKeys.Count);
        Assert.Empty(fixture.Factory.Sessions);
        Assert.Equal(0, fixture.Manager.TrackedConnectionCount);
        fixture.Approval.Handle = (_, _) => Task.FromResult(true);
        await fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        Assert.Equal(2, fixture.Approval.Requests.Count);
    }

    [Fact]
    public async Task DecliningASharedPromptRejectsAllWaitersWithoutSavingTrust()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        using var decision = new AsyncGate();
        fixture.Approval.Handle = async (_, token) => { await decision.WaitAsync(token); return false; };
        Task<ConnectionTestResult> first = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        await decision.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var joined = new ApprovalProgress();
        Task<ConnectionTestResult> second = fixture.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input, joined);
        await joined.Joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        decision.Release.TrySetResult();
        foreach (Task<ConnectionTestResult> request in new[] { first, second })
        {
            ConnectionException failure = await Assert.ThrowsAsync<ConnectionException>(() => request);
            Assert.Equal(ConnectionFailure.HostKeyDeclined, failure.Failure);
        }
        Assert.Single(fixture.Approval.Requests);
        Assert.Empty(fixture.Factory.Sessions);
        Assert.Equal(0, fixture.HostKeys.Count);
    }

    [Fact]
    public async Task DifferentFingerprintsNeverShareApprovalOrOverwriteTheWinningTrust()
    {
        RemoteServer firstServer = DomainData.Server();
        RemoteServer secondServer = firstServer with { Id = DomainData.Id(2) };
        await using var fixture = await ConnectionFixture.CreateAsync(true, firstServer, secondServer);
        fixture.Factory.KeyFor = server => ConnectionFixture.TestSessionFactory.Key(server, server.Id == firstServer.Id ? (byte)1 : (byte)2);
        using var firstDecision = new AsyncGate();
        using var secondDecision = new AsyncGate();
        fixture.Approval.Handle = async (request, token) =>
        {
            await (request.Key.Matches(fixture.Factory.KeyFor(firstServer)) ? firstDecision : secondDecision).WaitAsync(token);
            return true;
        };
        Task<ConnectionTestResult> first = fixture.Manager.TestAsync(firstServer, ConnectionFixture.Input);
        Task<ConnectionTestResult> second = fixture.Manager.TestAsync(secondServer, ConnectionFixture.Input);
        await Task.WhenAll(firstDecision.Entered.Task, secondDecision.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
        firstDecision.Release.TrySetResult();
        await first;
        secondDecision.Release.TrySetResult();
        ConnectionException failure = await Assert.ThrowsAsync<ConnectionException>(() => second);
        Assert.Equal(ConnectionFailure.HostKeyChanged, failure.Failure);
        Assert.Equal(2, fixture.Approval.Requests.Count);
        Assert.Single(fixture.Factory.Sessions);
        Assert.Equal(1, fixture.HostKeys.Count);
    }

    private sealed class ApprovalProgress : IProgress<ConnectionStage>
    {
        public TaskCompletionSource Joined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Report(ConnectionStage stage)
        {
            if (stage == ConnectionStage.AwaitingHostApproval) { Joined.TrySetResult(); }
        }
    }
}
