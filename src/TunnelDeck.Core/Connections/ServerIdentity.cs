using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Connections;

public static class ServerIdentity
{
    public static bool Matches(RemoteServer first, RemoteServer second) =>
        first.Id == second.Id && SameAuthenticationTarget(first, second) && first.CredentialReference == second.CredentialReference;

    public static bool SameAuthenticationTarget(RemoteServer first, RemoteServer second) =>
        HostAddress.Normalize(first.Host) == HostAddress.Normalize(second.Host) && first.Port == second.Port &&
        first.Username == second.Username && first.AuthMethod == second.AuthMethod &&
        string.Equals(first.IdentityFilePath, second.IdentityFilePath, StringComparison.Ordinal);
}
