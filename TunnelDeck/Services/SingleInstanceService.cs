using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppLifecycle;

namespace TunnelDeck.Services;

/// <summary>Registers before any configuration or credential services open the data directory.</summary>
internal sealed class SingleInstanceService : IDisposable
{
    private readonly AppInstance _instance;
    private readonly object _sync = new();
    private Action? _activate;
    private bool _pending;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstanceService(string dataDirectory)
    {
        string identity = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        string key = "TunnelDeck:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        _instance = AppInstance.FindOrRegisterForKey(key);
        IsPrimary = _instance.IsCurrent;
        if (IsPrimary) { _instance.Activated += OnActivated; }
    }

    public void Redirect()
    {
        AppActivationArguments arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        AllowSetForegroundWindow((uint)_instance.ProcessId);
        // Keep the entry thread STA for WinUI/Narrator. Redirect from MTA, as in the SDK sample.
        Task redirect = Task.Run(async () => await _instance.RedirectActivationToAsync(arguments));
        if (!redirect.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException("The existing instance did not accept activation."); }
        redirect.GetAwaiter().GetResult();
    }

    public void Attach(Action activate)
    {
        bool pending;
        lock (_sync) { _activate = activate; pending = _pending; _pending = false; }
        if (pending) { activate(); }
    }

    private void OnActivated(object? sender, AppActivationArguments arguments)
    {
        Action? activate;
        lock (_sync)
        {
            if (_disposed) { return; }
            activate = _activate;
            if (activate is null) { _pending = true; }
        }
        activate?.Invoke();
    }

    public void Dispose()
    {
        lock (_sync) { if (_disposed) { return; } _disposed = true; _activate = null; }
        if (IsPrimary) { _instance.Activated -= OnActivated; _instance.UnregisterKey(); }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
