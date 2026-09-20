using Renci.SshNet;
using TunnelDeck.Infrastructure.Ssh;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class SshControlSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndDisposalWaitForTheNativeWorkerEvenAfterTheClientHasClosed(bool cancel)
    {
        using var client = new SshClient("127.0.0.1", "fixture", "not-used");
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
        await using var control = new SshControlSession(client, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool finished = false;
        Task work = control.ExecuteAsync(() => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); finished = true; }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal;
        try
        {
            if (cancel) { cancellation.Cancel(); }
            disposal = control.DisposeAsync().AsTask();
            Assert.True(control.IsRetired);
            Assert.False(work.IsCompleted);
            Assert.False(disposal.IsCompleted);
            Assert.False(finished);
        }
        finally { release.TrySetResult(); }
        if (cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); }
        else { await work; }
        await disposal;
        Assert.True(finished);
        await Assert.ThrowsAsync<InvalidOperationException>(() => control.ExecuteAsync(() => throw new Exception("Must not run."), CancellationToken.None));
    }

    [Fact]
    public void ControlDeadlineMustPrecedeTheLibrarySilentStopTimeout()
    {
        using var client = new SshClient("127.0.0.1", "fixture", "not-used");
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SshControlSession(client, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SshControlSession(client, TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SshControlSession(client, TimeSpan.FromSeconds(6)));
    }
}
