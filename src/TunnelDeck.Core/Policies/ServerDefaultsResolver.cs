using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Policies;

public sealed record ServerAuthenticationDefaults(AuthenticationMethod Method, string? IdentityFilePath);

public static class ServerDefaultsResolver
{
    public static string? DetectIdentityFile(string homeDirectory, Func<string, bool> fileExists)
    {
        string candidate = Path.Combine(homeDirectory, ".ssh", "id_rsa");
        return fileExists(candidate) ? candidate : null;
    }

    public static ServerAuthenticationDefaults Authentication(AppSettings settings, string homeDirectory, Func<string, bool> fileExists) =>
        settings.DefaultAuthenticationMethod switch
        {
            AuthenticationMethod.Password => new(AuthenticationMethod.Password, null),
            AuthenticationMethod.PrivateKey => new(AuthenticationMethod.PrivateKey, settings.DefaultIdentityFilePath ?? DetectIdentityFile(homeDirectory, fileExists)),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), UiText.Invariant("Diagnostic_ServerDefaultsResolver_UnknownDefaultAuthenticationMethod")),
        };

    public static ServerDraft CreateDraft(AppSettings settings, string homeDirectory, Func<string, bool> fileExists)
    {
        ServerAuthenticationDefaults authentication = Authentication(settings, homeDirectory, fileExists);
        return new() { AuthMethod = authentication.Method, IdentityFilePath = authentication.IdentityFilePath };
    }
}
