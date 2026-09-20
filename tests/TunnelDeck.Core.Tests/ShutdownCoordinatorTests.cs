using TunnelDeck.Core.Lifecycle;

namespace TunnelDeck.Core.Tests;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public async Task RepeatedCloseSharesOneCleanupAndPreventsNewWork()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var coordinator = new ShutdownCoordinator([new Participant(_ => { calls++; return completion.Task; })]);
        Task first = coordinator.ShutdownAsync(CancellationToken.None);
        Task second = coordinator.ShutdownAsync(CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.True(coordinator.IsShutdownRequested);
        completion.SetResult();
        await first;
    }

    [Fact]
    public async Task FailureIsReportedAndCanBeRetried()
    {
        int calls = 0;
        var coordinator = new ShutdownCoordinator([new Participant(_ =>
            ++calls == 1 ? Task.FromException(new IOException("fixture failure")) : Task.CompletedTask)]);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ShutdownAsync(CancellationToken.None));
        await coordinator.ShutdownAsync(CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DeadlineEndsTheWaitEvenWhenParticipantHasNotFinished()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource();
        var coordinator = new ShutdownCoordinator([new Participant(_ => completion.Task)]);
        Task cleanup = coordinator.ShutdownAsync(deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanup);
        completion.SetResult();
    }

    [Fact]
    public async Task RetrySkipsCompletedStepsAndOnlyRepeatsTheFailedSave()
    {
        int stopped = 0, saves = 0;
        var coordinator = new ShutdownCoordinator(new ShutdownStep[]
        {
            new("Stopping forwards…", new Participant(_ => { stopped++; return Task.CompletedTask; })),
            new("Saving changes…", new Participant(_ => ++saves == 1 ? Task.FromException(new IOException()) : Task.CompletedTask), true),
        });
        await Assert.ThrowsAsync<IOException>(() => coordinator.ShutdownAsync(CancellationToken.None));
        Assert.True(coordinator.Current.SavesData); Assert.Equal(ShutdownState.Failed, coordinator.Current.State);
        await coordinator.ShutdownAsync(CancellationToken.None);
        Assert.Equal(1, stopped); Assert.Equal(2, saves); Assert.Equal(ShutdownState.Complete, coordinator.Current.State);
    }

    [Fact]
    public async Task ATimeoutRetryJoinsTheActualWorkInsteadOfStartingAnotherCleanup()
    {
        int calls = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ShutdownCoordinator([new Participant(_ => { calls++; return completion.Task; })]);
        using var deadline = new CancellationTokenSource();
        Task first = coordinator.ShutdownAsync(deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Task retry = coordinator.ShutdownAsync(CancellationToken.None);
        Assert.Equal(1, calls); Assert.False(retry.IsCompleted);
        completion.SetResult();
        await retry;
        Assert.Equal(1, calls);
    }

    private sealed class Participant(Func<CancellationToken, Task> callback) : IShutdownParticipant
    {
        public Task ShutdownAsync(CancellationToken cancellationToken) => callback(cancellationToken);
    }
}
