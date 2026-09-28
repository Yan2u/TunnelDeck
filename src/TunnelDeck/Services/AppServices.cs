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
using TunnelDeck.Core.Transfer;
using TunnelDeck.Design;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Infrastructure.Terminal;
using TunnelDeck.Infrastructure.Transfer;
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
    public ResourceImportService Import { get; }
    public ResourceTransferFiles TransferFiles { get; }
    public ITerminalLauncher Terminal { get; } = new WindowsTerminalLauncher();
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
        Import = new(Configuration, Connections.Work, File.Exists);
        TransferFiles = new(directory.RootPath);
        List<ShutdownStep> steps =
        [
            new(UiText.Get("MainWindowLifecycle_CancellingPendingWork"), Connections.Work),
            new(UiText.Get("AppServices_FinishingImportAndExport"), TransferFiles),
            new(UiText.Get("AppServices_FinishingResourceChanges"), Deletion),
            new(UiText.Get("AppServices_StoppingAutomaticReconnect"), Reconnect),
            new(UiText.Get("AppServices_FinishingProfileCommands"), Profiles),
            new(UiText.Get("AppServices_FinishingForwardingCommands"), Forwarding),
            new(UiText.Get("AppServices_StoppingForwards"), Tunnels),
            new(UiText.Get("AppServices_ClosingSSHConnections"), Connections),
            new(UiText.Get("AppServices_SavingRecentActivity"), Activity, true),
            new(UiText.Get("AppServices_SavingConfiguration"), Configuration, true),
        ];
        if (windowProbe) { steps.Add(new(UiText.Get("AppServices_FinishingWindowVerification"), new WindowProbeShutdown())); }
        Shutdown = new(steps);
    }

    internal static AppServices Create(AppLaunchOptions launch) => new(launch.Directory, launch.WindowProbe, launch.Isolated, launch.Preview);

    public void BeginClosing()
    {
        Forwarding.SetAvailable(false);
        _ = TransferFiles.BeginClosing();
        _ = Connections.Work.BeginShutdown();
    }

    private sealed class WindowProbeShutdown : IShutdownParticipant
    {
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.Delay(750, cancellationToken);
    }
}
