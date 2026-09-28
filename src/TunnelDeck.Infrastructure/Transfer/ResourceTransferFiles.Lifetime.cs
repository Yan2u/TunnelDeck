using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.Infrastructure.Transfer;

public sealed partial class ResourceTransferFiles : IShutdownParticipant
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _pending = [];
    private readonly CancellationTokenSource _closing = new();
    private bool _accepting = true;

    public Task<ImportPlan> PreviewAsync(string path, ImportSource source, AppState state, CancellationToken cancellationToken = default) =>
        TrackAsync(token => PreviewCoreAsync(path, source, state, token), cancellationToken);

    public Task ExportAsync(string path, TunnelDeckExport export, CancellationToken cancellationToken = default) =>
        TrackAsync(async token => { await ExportCoreAsync(path, export, token).ConfigureAwait(false); return true; }, cancellationToken);

    private Task<T> TrackAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_accepting) { return Task.FromCanceled<T>(new CancellationToken(true)); }
            Task<T> task = Task.Run(async () =>
            {
                using var stopping = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, cancellationToken);
                stopping.Token.ThrowIfCancellationRequested();
                return await operation(stopping.Token).ConfigureAwait(false);
            });
            Task observed = ObserveAsync(task);
            _pending.Add(observed);
            _ = observed.ContinueWith(done => { lock (_sync) { _pending.Remove(done); } }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* Each caller owns its result; shutdown waits for cleanup without replaying an old file error. */ }
    }

    public Task BeginClosing()
    {
        lock (_sync) { _accepting = false; }
        return _closing.CancelAsync();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await BeginClosing().ConfigureAwait(false);
        Task[] pending;
        lock (_sync) { pending = [.. _pending]; }
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
