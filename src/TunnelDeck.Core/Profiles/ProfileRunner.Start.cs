using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Profiles;

public sealed partial class ProfileRunner
{
    private async Task RunRuleAsync(Operation operation, RuleLaunchSnapshot launch, ServerWorkLease work)
    {
        CancellationToken token = work.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            Task<ConnectionCredentials?>? pendingInput;
            lock (_sync) { pendingInput = operation.Inputs.GetValueOrDefault(launch.Server.Id); }
            bool prompted = !_tunnels.CanReuseForStart(launch) && (pendingInput is not null ||
                launch.Server.AuthMethod == AuthenticationMethod.Password && launch.Server.CredentialReference is null);
            ConnectionCredentials? input = null;
            if (prompted)
            {
                input = await (pendingInput ?? RequestInput(operation, launch.Server, UiText.Invariant("Diagnostic_ProfileRunnerStart_EnterTheSSHPasswordForThisProfileS"))).ConfigureAwait(false);
                if (input is null) { PublishCancelled(operation, launch); return; }
            }
            token.ThrowIfCancellationRequested();
            ProfileRuleResult? previous = operation.Previous?.Rules.FirstOrDefault(item => item.Launch.Requested == launch.Requested &&
                item.Outcome is ProfileRuleOutcome.Failed or ProfileRuleOutcome.Cancelled);
            if (previous?.TunnelId is Guid oldId && _tunnels.Current.Tunnels.Any(item => item.Id == oldId && item.State is TunnelState.Failed or TunnelState.Cancelled or TunnelState.Stopped))
            { await _tunnels.RemoveAsync(oldId, token).ConfigureAwait(false); }

            TunnelStartResult result = await StartTrackedAsync(operation, launch, input, token).ConfigureAwait(false);
            input = null;
            if (!prompted && !result.Reused && result.Tunnel.LastError is { Kind: TunnelFailureKind.Credentials } failure)
            {
                input = await RequestInput(operation, launch.Server, failure.Message).ConfigureAwait(false);
                if (input is not null)
                {
                    token.ThrowIfCancellationRequested();
                    if (await _tunnels.RemoveAsync(result.Tunnel.Id, token).ConfigureAwait(false))
                    { result = await StartTrackedAsync(operation, launch, input, token).ConfigureAwait(false); }
                }
            }
            bool recovering = result.Reused && result.Tunnel.State == TunnelState.Reconnecting;
            ProfileRuleOutcome outcome = recovering ? ProfileRuleOutcome.AlreadyReconnecting :
                result.Succeeded ? result.Reused ? ProfileRuleOutcome.AlreadyRunning : ProfileRuleOutcome.Started :
                result.Tunnel.State is TunnelState.Cancelled or TunnelState.Stopped ? ProfileRuleOutcome.Cancelled : ProfileRuleOutcome.Failed;
            Publish(operation, new(launch, outcome, result.Tunnel.Id, result.Succeeded || recovering ? null : result.Tunnel.LastError));
        }
        catch (OperationCanceledException) { PublishCancelled(operation, launch); }
        catch (Exception error) { Publish(operation, new(launch, ProfileRuleOutcome.Failed, RegisteredId(operation, launch), TunnelErrors.From(error))); }
        finally { work.FinishRule(launch.Requested.Id); }
    }

    private async Task<TunnelStartResult> StartTrackedAsync(Operation operation, RuleLaunchSnapshot launch, ConnectionCredentials? input, CancellationToken token)
    {
        // Linked server-work cancellation can lag behind CancelAsync dispatching its callbacks.
        operation.Cancellation.Token.ThrowIfCancellationRequested();
        Task<TunnelStartResult> start = _tunnels.StartAsync(launch, input, token, operation.ServerWork[launch.Server.Id]);
        ForwardRequestKey key = ForwardRequestKey.FromRule(launch.Requested);
        ActiveTunnel? registered = _tunnels.Current.Tunnels.LastOrDefault(tunnel => ForwardRequestKey.FromRule(tunnel.Requested) == key);
        if (registered is not null) { Publish(operation, new(launch, ProfileRuleOutcome.Starting, registered.Id)); }
        return await start.ConfigureAwait(false);
    }

    private Task<ConnectionCredentials?> RequestInput(Operation operation, RemoteServer server, string message)
    {
        lock (_sync)
        {
            if (!operation.Inputs.TryGetValue(server.Id, out Task<ConnectionCredentials?>? input))
            {
                // Shared only inside this invocation, including separate Remote transports. Never persisted.
                input = Task.Run(() => _credentials(server, message, operation.ServerWork[server.Id].Token));
                operation.Inputs.Add(server.Id, input);
            }
            return input;
        }
    }

    private void PublishCancelled(Operation operation, RuleLaunchSnapshot launch) => Publish(operation,
        new(launch, ProfileRuleOutcome.Cancelled, RegisteredId(operation, launch), new(TunnelFailureKind.Cancelled, UiText.Invariant("Diagnostic_ProfileRunnerStart_StartingThisRuleWasCancelled"))));

    private Guid? RegisteredId(Operation operation, RuleLaunchSnapshot launch)
    { lock (_sync) { return operation.Snapshot.Rules.First(item => item.Launch.Requested.Id == launch.Requested.Id).TunnelId; } }
}
