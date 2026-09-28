namespace TunnelDeck.Infrastructure.Persistence;

public sealed class AppDataDirectory
{
    public string RootPath { get; }
    public string StatePath => Path.Combine(RootPath, "state.json");
    public string HostKeysPath => Path.Combine(RootPath, "host-keys.json");
    public string CredentialCleanupPath => Path.Combine(RootPath, "credential-cleanup.json");
    public string TransactionLockPath => Path.Combine(RootPath, "configuration.lock");

    public AppDataDirectory(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Path.IsPathFullyQualified(rootPath)) { throw new ArgumentException(UiText.Invariant("Diagnostic_AppDataDirectory_UseAnAbsoluteDataDirectory"), nameof(rootPath)); }
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (StringComparer.OrdinalIgnoreCase.Equals(RootPath, installation) ||
            RootPath.StartsWith(installation + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(UiText.Invariant("Diagnostic_AppDataDirectory_UserDataCannotBeStoredInTheInstallation"), nameof(rootPath));
        }
    }

    public static AppDataDirectory ForCurrentUser()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) { throw new IOException(UiText.Invariant("Diagnostic_AppDataDirectory_TheUserDataDirectoryIsUnavailable")); }
        return new(Path.Combine(local, "TunnelDeck"));
    }

    public static AppDataDirectory ForWindowProbe() =>
        new(Path.Combine(Path.GetTempPath(), "TunnelDeck", "WindowProbes", Guid.NewGuid().ToString("N")));
}
