using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Connections;

public sealed record HostKeyApprovalRequest(string ServerName, string Username, HostKeyIdentity Key);

public interface IHostKeyApproval
{
    Task<bool> ConfirmAsync(HostKeyApprovalRequest request, CancellationToken cancellationToken);
}

public enum ConnectionStage { ReadingCredentials, Connecting, AwaitingHostApproval, VerifyingHostKey, ClosingTest }

public sealed record ConnectionTestResult(HostKeyIdentity HostKey);
