using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Persistence;

public enum CredentialChangeKind { Keep, Forget, Remember }

/// <summary>Transient input. Neither serialization nor diagnostic formatting reveals the secret.</summary>
public sealed class CredentialChange
{
    public CredentialChangeKind Kind { get; }
    [JsonIgnore] public string? Secret { get; }

    private CredentialChange(CredentialChangeKind kind, string? secret = null) { Kind = kind; Secret = secret; }

    public static CredentialChange Keep { get; } = new(CredentialChangeKind.Keep);
    public static CredentialChange Forget { get; } = new(CredentialChangeKind.Forget);
    public static CredentialChange Remember(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new(CredentialChangeKind.Remember, secret);
    }

    public override string ToString() => UiText.Invariant("Diagnostic_CredentialChange_CredentialChangeKind", Kind);
}
