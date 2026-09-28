using System.Collections.ObjectModel;
using TunnelDeck.Core.Models;

namespace TunnelDeck.ViewModels;

public sealed class ResourceListViewModel : ObservableObject
{
    private ResourceKind _kind;
    private bool _canEdit;
    private bool _showEmpty;
    private string _operationError = string.Empty;
    private string _transferNotice = string.Empty;
    public ResourceKind Kind { get => _kind; private set { SetProperty(ref _kind, value); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(PageId)); OnPropertyChanged(nameof(AddLabel)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyDescription)); } }
    public string Title => Kind == ResourceKind.Profile ? UiText.Get("MainWindow_Profiles") : UiText.Get("MainWindow_Servers");
    public string PageId => Kind == ResourceKind.Profile ? "ProfilesPage" : "ServersPage";
    public string AddLabel => Kind == ResourceKind.Profile ? UiText.Get("ResourceListViewModel_AddProfile") : UiText.Get("DashboardPage_AddServer");
    public string PinnedLabel => UiText.Get("ResourceListViewModel_Pinned", Pinned.Count);
    public string OtherLabel => HasPinned ? UiText.Get("ResourceListViewModel_Other", Other.Count) : UiText.Get("ResourceListViewModel_All", Other.Count);
    public string DeleteAllLabel => Kind == ResourceKind.Profile ? UiText.Get("ResourceListViewModel_DeleteAllProfiles") : UiText.Get("ResourceListViewModel_DeleteAllServers");
    public string DeleteAllId => Kind == ResourceKind.Profile ? "DeleteAllProfiles" : "DeleteAllServers";
    public bool CanDeleteAll => CanEdit && Pinned.Count + Other.Count > 0;
    public string EmptyTitle => Kind == ResourceKind.Profile ? UiText.Get("ResourceListViewModel_NoProfilesYet") : UiText.Get("ResourceListViewModel_NoServersYet");
    public string EmptyDescription => Kind == ResourceKind.Profile ? UiText.Get("ResourceListViewModel_SaveGroupsOfForwardingRulesAndStartThem") : UiText.Get("ResourceListViewModel_AddARemoteSSHServerToStartForwarding");
    public ObservableCollection<ResourceRow> Pinned { get; } = [];
    public ObservableCollection<ResourceRow> Other { get; } = [];
    public string OperationError { get => UiText.Translate(_operationError); set => SetProperty(ref _operationError, value); }
    public string TransferNotice { get => _transferNotice; set => SetProperty(ref _transferNotice, value); }
    public bool HasPinned => Pinned.Count > 0;
    public bool HasOther => Other.Count > 0;
    public bool ShowEmpty { get => _showEmpty; private set => SetProperty(ref _showEmpty, value); }
    public bool CanEdit { get => _canEdit; private set => SetProperty(ref _canEdit, value); }
    public void SetAvailability(bool canEdit) { CanEdit = canEdit; OnPropertyChanged(nameof(CanDeleteAll)); }

    public void Update(ShellViewModel shell, ResourceKind kind)
    {
        Kind = kind;
        Reconcile(Pinned, shell.Resources(kind, true));
        Reconcile(Other, shell.Resources(kind, false));
        OnPropertyChanged(nameof(HasPinned));
        OnPropertyChanged(nameof(HasOther));
        OnPropertyChanged(nameof(PinnedLabel)); OnPropertyChanged(nameof(OtherLabel));
        CanEdit = shell.CanEdit;
        ShowEmpty = shell.IsReady && Pinned.Count + Other.Count == 0;
        OnPropertyChanged(nameof(DeleteAllLabel)); OnPropertyChanged(nameof(DeleteAllId)); OnPropertyChanged(nameof(CanDeleteAll));
    }

    private static void Reconcile(ObservableCollection<ResourceRow> target, IReadOnlyList<ResourceRow> source)
    {
        if (target.SequenceEqual(source)) { return; }
        target.Clear(); foreach (ResourceRow row in source) { target.Add(row); }
    }
}
