using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Drafts;

public sealed class ServerDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DisplayName { get; set; }
    public string Host { get; set; } = string.Empty;
    public string PortText { get; set; } = "22";
    public string Username { get; set; } = string.Empty;
    public AuthenticationMethod AuthMethod { get; set; }
    public string? IdentityFilePath { get; set; }
    public Guid? CredentialReference { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public static ServerDraft FromServer(RemoteServer server) => new()
    {
        Id = server.Id, DisplayName = server.DisplayName, Host = server.Host,
        PortText = server.Port.ToString(CultureInfo.InvariantCulture), Username = server.Username,
        AuthMethod = server.AuthMethod, IdentityFilePath = server.IdentityFilePath,
        CredentialReference = server.CredentialReference, LastUsedAt = server.LastUsedAt,
    };

    public RemoteServer Build()
    {
        PortNumber.TryParse(PortText, out int port);
        var server = new RemoteServer
        {
            Id = Id, DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
            Host = Host?.Trim() ?? string.Empty, Port = port, Username = Username?.Trim() ?? string.Empty,
            AuthMethod = AuthMethod, IdentityFilePath = IdentityFilePath,
            CredentialReference = CredentialReference, LastUsedAt = LastUsedAt,
        };
        ConfigurationValidator.ValidateServer(server).ThrowIfInvalid();
        return server;
    }
}
