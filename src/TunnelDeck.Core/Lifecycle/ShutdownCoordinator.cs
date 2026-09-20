namespace TunnelDeck.Core.Lifecycle;

public sealed record ShutdownStep(string Message, IShutdownParticipant Participant, bool SavesData = false);
public enum ShutdownState { NotStarted, Running, TimedOut, Failed, Complete }
public sealed record ShutdownProgress(long Revision, int CompletedSteps, int TotalSteps, string Message, bool SavesData, ShutdownState State);

/// <summary>Coalesces close requests, retains timed-out work, and never repeats completed shutdown steps.</summary>
public sealed class ShutdownCoordinator
{
    private readonly ShutdownStep[] _steps;
    private readonly object _sync = new();
    private Task? _shutdown;
    private Task? _stepWork;
    private int _next;
    private long _revision;
    private ShutdownProgress _current;
    public bool IsShutdownRequested { get; private set; }
    public ShutdownProgress Current { get { lock (_sync) { return _current; } } }
    public event Action<ShutdownProgress>? StateChanged;

    public ShutdownCoordinator(IEnumerable<IShutdownParticipant> participants)
        : this(participants.Select(participant => new ShutdownStep("Closing TunnelDeck…", participant))) { }
    public ShutdownCoordinator(IEnumerable<ShutdownStep> steps)
    {
        _steps = steps.ToArray();
        _current = new(0, 0, _steps.Length, string.Empty, false, ShutdownState.NotStarted);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            IsShutdownRequested = true;
            if (_shutdown is null || _shutdown.IsFaulted || _shutdown.IsCanceled)
            { _shutdown = StopParticipantsAsync(cancellationToken); }
            return _shutdown;
        }
    }

    private async Task StopParticipantsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_next < _steps.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ShutdownStep step = _steps[_next];
                Publish(ShutdownState.Running, step.Message, step.SavesData);
                Task work;
                lock (_sync)
                {
                    // A caller deadline bounds waiting, never ownership of accepted cleanup.
                    if (_stepWork is null || _stepWork.IsFaulted || _stepWork.IsCanceled)
                    {
                        _stepWork = step.Participant.ShutdownAsync(CancellationToken.None);
                        _ = _stepWork.ContinueWith(done => _ = done.Exception, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                    work = _stepWork;
                }
                await work.WaitAsync(cancellationToken).ConfigureAwait(false);
                lock (_sync) { _next++; _stepWork = null; }
            }
            Publish(ShutdownState.Complete, "Ready to close.", false);
        }
        catch (OperationCanceledException)
        {
            Publish(ShutdownState.TimedOut, Current.SavesData ? "Saving is still in progress." : "Some cleanup is still in progress.", Current.SavesData);
            throw;
        }
        catch (Exception)
        {
            Publish(ShutdownState.Failed, Current.SavesData ? "Changes could not be saved." : "Cleanup could not finish.", Current.SavesData);
            throw;
        }
    }

    private void Publish(ShutdownState state, string message, bool savesData)
    {
        ShutdownProgress snapshot;
        lock (_sync) { _current = snapshot = new(++_revision, _next, _steps.Length, message, savesData, state); }
        if (StateChanged is not { } handlers) { return; }
        foreach (Action<ShutdownProgress> handler in handlers.GetInvocationList().Cast<Action<ShutdownProgress>>())
        { try { handler(snapshot); } catch (Exception) { /* Observers cannot interrupt shutdown. */ } }
    }
}
