using TunnelDeck.Design;
using TunnelDeck.Infrastructure.Persistence;

namespace TunnelDeck.Services;

internal sealed record AppLaunchOptions(bool WindowProbe, DesignScenarioKind? Preview, AppDataDirectory Directory, bool Isolated)
{
    public static AppLaunchOptions Parse(string[] arguments)
    {
        bool probe = arguments.Contains("--window-probe", StringComparer.Ordinal);
        string? data = arguments.FirstOrDefault(argument => argument.StartsWith("--data-directory=", StringComparison.Ordinal))?["--data-directory=".Length..];
        string? previewName = arguments.FirstOrDefault(argument => argument.StartsWith("--preview=", StringComparison.Ordinal))?["--preview=".Length..];
        DesignScenarioKind? preview = Enum.TryParse(previewName, true, out DesignScenarioKind scenario) && Enum.IsDefined(scenario) ? scenario : null;
        preview ??= probe ? DesignScenarioKind.Stress : null;
        bool isolated = data is not null || probe || preview is not null;
        AppDataDirectory directory = data is not null ? new(data) : isolated ? AppDataDirectory.ForWindowProbe() : AppDataDirectory.ForCurrentUser();
        return new(probe, preview, directory, isolated);
    }
}
