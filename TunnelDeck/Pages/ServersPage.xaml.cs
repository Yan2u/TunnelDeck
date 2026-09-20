using TunnelDeck.Core.Models;

namespace TunnelDeck.Pages;

public sealed partial class ServersPage : ShellPage
{
    public ServersPage() => InitializeComponent();
    protected override void Refresh() => ResourcesView.Update(Shell, ResourceKind.Server);
    protected override void OnAvailabilityChanged() => ResourcesView.SetAvailability(Shell.CanEdit);
}
