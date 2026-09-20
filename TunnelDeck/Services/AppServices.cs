using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Design;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Services;

namespace TunnelDeck;

/// <summary>Small composition root; no global service locator or background SSH connections.</summary>
public sealed class AppServices
{
    public ShutdownCoordinator Shutdown { get; }
    public bool IsWindowProbe { get; }
    public DesignScenarioKind? PreviewScenario { get; }
    public AppDataDirectory DataDirectory { get; }
    public ConfigurationService Configuration { get; }
    public ICredentialStore Credentials { get; }
    public IHostKeyStore HostKeys { get; }
    public SshConnectionManager Connections { get; }
    public TunnelManager Tunnels { get; }
    public TunnelActivityRecorder Activity { get; }
    public TunnelWorkspace Forwarding { get; }
    public ProfileRunner Profiles { get; }
    public ResourceDeletionService Deletion { get; }
    public ReconnectCoordinator Reconnect { get; }
    public DialogService Dialogs { get; } = new();
    public IntPtr WindowHandle { get; set; }

    private AppServices(AppDataDirectory directory, bool windowProbe, bool isolated, DesignScenarioKind? previewScenario)
    {
        IsWindowProbe = windowProbe;
        PreviewScenario = previewScenario;
        DataDirectory = directory;
        string credentialPrefix = isolated
            ? "TunnelDeck/Isolated/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.RootPath.ToUpperInvariant())))
            : "TunnelDeck/Credentials";
        Credentials = new WindowsCredentialStore(credentialPrefix);
        HostKeys = new JsonHostKeyStore(directory);
        Configuration = new(new JsonStateStore(directory), Credentials, new JsonCredentialCleanupStore(directory),
            new FileConfigurationLock(directory.TransactionLockPath));
        Connections = new(Configuration, new SshSessionFactory(), Credentials, HostKeys, Dialogs, previewScenario is null);
        Tunnels = new(Connections);
        Activity = new(Tunnels, Configuration);
        Forwarding = new(Tunnels, Activity, Configuration, new ViewDispatcher(DispatcherQueue.GetForCurrentThread()),
            Dialogs.RequestForwardCredentialsAsync, previewScenario is not null);
        Profiles = new(Configuration, Tunnels, Activity, Dialogs.RequestForwardCredentialsAsync, previewScenario is null);
        Deletion = new(Configuration, Connections, Tunnels, Activity);
        Reconnect = new(Tunnels);
        List<ShutdownStep> steps =
        [
            new("Cancelling pending work…", Connections.Work),
            new("Finishing resource changes…", Deletion),
            new("Stopping automatic reconnect…", Reconnect),
            new("Finishing profile commands…", Profiles),
            new("Finishing forwarding commands…", Forwarding),
            new("Stopping forwards…", Tunnels),
            new("Closing SSH connections…", Connections),
            new("Saving recent activity…", Activity, true),
            new("Saving configuration…", Configuration, true),
        ];
        if (windowProbe) { steps.Add(new("Finishing window verification…", new WindowProbeShutdown())); }
        Shutdown = new(steps);
    }

    internal static AppServices Create(AppLaunchOptions launch) => new(launch.Directory, launch.WindowProbe, launch.Isolated, launch.Preview);

    public void BeginClosing()
    {
        Forwarding.SetAvailable(false);
        _ = Connections.Work.BeginShutdown();
    }

    private sealed class WindowProbeShutdown : IShutdownParticipant
    {
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.Delay(750, cancellationToken);
    }
}
