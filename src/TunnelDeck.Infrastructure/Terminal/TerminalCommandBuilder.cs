using System.Diagnostics;
using System.Globalization;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Infrastructure.Terminal;

/// <summary>All values are argv entries, never a shell command. Sensitive values never enter this adapter.</summary>
public static class TerminalCommandBuilder
{
    public static ProcessStartInfo Build(RemoteServer server, string sshPath, string? terminalPath, Func<string, bool> keyFileExists)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!ConfigurationValidator.ValidateServer(server).IsValid || server.Host.StartsWith('-'))
        { throw new TerminalLaunchException(UiText.Invariant("Diagnostic_TerminalCommandBuilder_SaveAValidHostUsernameAndSSHPort")); }
        if (string.IsNullOrWhiteSpace(sshPath) || !Path.IsPathFullyQualified(sshPath))
        { throw new TerminalLaunchException(UiText.Invariant("Diagnostic_TerminalCommandBuilder_OpenSSHClientWasNotFoundInstallItFrom")); }
        var arguments = new List<string>
        {
            "-F", "none", "-p", server.Port.ToString(CultureInfo.InvariantCulture), "-l", server.Username,
        };
        if (server.AuthMethod == AuthenticationMethod.PrivateKey)
        {
            if (!Path.IsPathFullyQualified(server.IdentityFilePath!) || !keyFileExists(server.IdentityFilePath!))
            { throw new TerminalLaunchException(UiText.Invariant("Diagnostic_TerminalCommandBuilder_ThePrivateKeyFileWasNotFoundSelect")); }
            arguments.AddRange(["-i", Path.GetFullPath(server.IdentityFilePath!), "-o", "IdentitiesOnly=yes"]);
        }
        arguments.AddRange(["--", HostAddress.Normalize(server.Host)]);

        // wt has a second command parser: semicolons split commands even inside an argv value.
        // Literal values with that syntax use a direct console, so no unverified secondary escaping is needed.
        bool useTerminal = terminalPath is not null && Path.IsPathFullyQualified(terminalPath) &&
            !arguments.Prepend(sshPath).Any(value => value.Contains(';') || value.Contains('"'));
        var start = new ProcessStartInfo
        {
            FileName = useTerminal ? terminalPath! : sshPath,
            UseShellExecute = false, CreateNoWindow = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (useTerminal) { foreach (string value in new[] { "-w", "new", "new-tab", "--", sshPath }) { start.ArgumentList.Add(value); } }
        foreach (string value in arguments) { start.ArgumentList.Add(value); }
        return start;
    }
}
