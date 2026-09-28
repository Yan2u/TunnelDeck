using System.ComponentModel;
using System.Diagnostics;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Infrastructure.Terminal;

/// <summary>Owns only the process handle while creating a user-owned terminal. Never retains or kills the process.</summary>
public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<ProcessStartInfo, Process?> _start;
    private readonly string? _terminalPath;
    private readonly string _sshPath;

    public WindowsTerminalLauncher() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe"),
        File.Exists, Process.Start) { }

    internal WindowsTerminalLauncher(string sshPath, string? terminalPath, Func<string, bool> fileExists,
        Func<ProcessStartInfo, Process?> start)
    { _sshPath = sshPath; _terminalPath = terminalPath; _fileExists = fileExists; _start = start; }

    public Task OpenAsync(RemoteServer server, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileExists(_sshPath))
        { throw new TerminalLaunchException(UiText.Invariant("Diagnostic_TerminalCommandBuilder_OpenSSHClientWasNotFoundInstallItFrom")); }
        string? terminal = _terminalPath is not null && _fileExists(_terminalPath) ? _terminalPath : null;
        ProcessStartInfo command = TerminalCommandBuilder.Build(server, _sshPath, terminal, _fileExists);
        try { Start(command); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            if (command.FileName == _sshPath) { throw Failed(); }
            cancellationToken.ThrowIfCancellationRequested();
            try { Start(TerminalCommandBuilder.Build(server, _sshPath, null, _fileExists)); }
            catch (Exception fallback) when (fallback is Win32Exception or InvalidOperationException or FileNotFoundException)
            { throw Failed(); }
        }
        return Task.CompletedTask;
    }

    private void Start(ProcessStartInfo command)
    {
        using Process? process = _start(command);
        if (process is null) { throw new InvalidOperationException(UiText.Invariant("Diagnostic_WindowsTerminalLauncher_ProcessCreationDidNotReturnAProcess")); }
    }

    private static TerminalLaunchException Failed() => new(UiText.Invariant("Diagnostic_WindowsTerminalLauncher_TheSSHTerminalCouldNotBeOpenedCheck"));
}
