using System.Collections.ObjectModel;
using TunnelDeck.Core.Models;

namespace TunnelDeck.ViewModels;

public sealed class ResourceListViewModel : ObservableObject
{
    private ResourceKind _kind;
    private bool _canEdit;
    private bool _showEmpty;
    private string _operationError = string.Empty;
    public ResourceKind Kind { get => _kind; private set { SetProperty(ref _kind, value); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(PageId)); OnPropertyChanged(nameof(AddLabel)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyDescription)); } }
    public string Title => Kind == ResourceKind.Profile ? "Profiles" : "Servers";
    public string PageId => Kind == ResourceKind.Profile ? "ProfilesPage" : "ServersPage";
    public string AddLabel => Kind == ResourceKind.Profile ? "Add profile" : "Add server";
    public string DeleteAllLabel => Kind == ResourceKind.Profile ? "Delete all profiles" : "Delete all servers";
    public string DeleteAllId => Kind == ResourceKind.Profile ? "DeleteAllProfiles" : "DeleteAllServers";
    public bool CanDeleteAll => CanEdit && Pinned.Count + Other.Count > 0;
    public string EmptyTitle => Kind == ResourceKind.Profile ? "No profiles yet" : "No servers yet";
    public string EmptyDescription => Kind == ResourceKind.Profile ? "Save groups of forwarding rules and start them together." : "Add a remote SSH server to start forwarding ports.";
    public ObservableCollection<ResourceRow> Pinned { get; } = [];
    public ObservableCollection<ResourceRow> Other { get; } = [];
    public string OperationError { get => _operationError; set => SetProperty(ref _operationError, value); }
    public bool HasPinned => Pinned.Count > 0;
    public bool ShowEmpty { get => _showEmpty; private set => SetProperty(ref _showEmpty, value); }
    public bool CanEdit { get => _canEdit; private set => SetProperty(ref _canEdit, value); }
    public void SetAvailability(bool canEdit) { CanEdit = canEdit; OnPropertyChanged(nameof(CanDeleteAll)); }

    public void Update(ShellViewModel shell, ResourceKind kind)
    {
        Kind = kind;
        Reconcile(Pinned, shell.Resources(kind, true));
        Reconcile(Other, shell.Resources(kind, false));
        OnPropertyChanged(nameof(HasPinned));
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
