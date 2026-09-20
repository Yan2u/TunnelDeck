using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Tunnels;

/// <summary>A safe failure from a forwarding operation; never retains an SSH library exception.</summary>
public sealed class ForwardException(TunnelFailure failure) : Exception(failure.Message)
{
    public TunnelFailure Failure { get; } = failure;
}
