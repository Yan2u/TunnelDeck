using TunnelDeck.Core.Models;

namespace TunnelDeck.Pages;

public sealed partial class ProfilesPage : ShellPage
{
    public ProfilesPage() => InitializeComponent();
    protected override void Refresh() => ResourcesView.Update(Shell, ResourceKind.Profile);
    protected override void OnAvailabilityChanged() => ResourcesView.SetAvailability(Shell.CanEdit);
}
