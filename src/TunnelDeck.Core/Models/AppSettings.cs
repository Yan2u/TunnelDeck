using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

public sealed record AppSettings
{
    public const int MinimumSidebarItems = 3;
    public const int MaximumSidebarItems = 10;

    [JsonRequired] public int SidebarMaximumItems { get; init; } = 5;
    [JsonRequired] public Guid? DefaultServerId { get; init; }
    [JsonRequired] public Guid? PinnedQuickServerId { get; init; }
    [JsonRequired] public bool AutoFindAvailablePortDefault { get; init; } = true;
    [JsonRequired] public AuthenticationMethod DefaultAuthenticationMethod { get; init; } = AuthenticationMethod.Password;
    [JsonRequired] public string? DefaultIdentityFilePath { get; init; }
    [JsonRequired] public string Language { get; init; } = UiText.English;
}
