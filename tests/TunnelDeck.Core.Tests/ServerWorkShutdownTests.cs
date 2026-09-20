using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ServerWorkShutdownTests
{
    [Fact]
    public async Task ClosingRejectsNewCommandsImmediatelyAndDrainsOwnersAndCancellationCallbacks()
    {
        var registry = new ServerWorkRegistry();
        using var callback = new AsyncGate();
        using var work = registry.Begin(DomainData.Server(), ServerWorkKind.ConnectionTest);
        using var registration = work.Token.Register(() => callback.WaitAsync().GetAwaiter().GetResult());
        Task closing = registry.BeginShutdown();
        Assert.Same(closing, registry.BeginShutdown());
        Assert.False(registry.IsAccepting);
        Assert.Throws<ConnectionException>(() => registry.Begin(DomainData.Server(2), ServerWorkKind.ConnectionTest));
        await callback.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        work.Dispose();
        Assert.False(closing.IsCompleted);
        callback.Release.TrySetResult();
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(registry.Current.Work);
    }

    [Fact]
    public async Task ACallerDeadlineDoesNotForgetARegisteredOwner()
    {
        var registry = new ServerWorkRegistry();
        using var work = registry.Begin(DomainData.Server(), ServerWorkKind.ConnectionTest);
        using var deadline = new CancellationTokenSource();
        Task first = registry.ShutdownAsync(deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(work.Token.IsCancellationRequested);
        Assert.Single(registry.Current.Work);
        Task retry = registry.ShutdownAsync(CancellationToken.None);
        Assert.False(retry.IsCompleted);
        work.Dispose();
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(registry.Current.Work);
    }
}
