using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.ViewModels;

public sealed partial class ShellViewModel
{
    private bool _openingTerminal;
    private (int Servers, int Profiles, int Skipped)? _importNotice;
    public string TransferNotice => _importNotice is not { } notice ? string.Empty
        : UiText.Get("ShellViewModelTransfer_ImportedAnd", ResourceDisplay.Count(notice.Servers, "server"), ResourceDisplay.Count(notice.Profiles, "profile")) +
          (notice.Skipped > 0 ? UiText.Get(notice.Skipped == 1 ? "Transfer_SkippedOne" : "Transfer_SkippedOther", notice.Skipped) : string.Empty) +
          UiText.Get("ShellViewModelTransfer_ReviewAuthenticationAndKeyPathsInTheNew");
    public bool CanOpenTerminal => CanEdit && !IsPreview && !_openingTerminal;
    public void ClearTransferNotice() { _importNotice = null; OnPropertyChanged(nameof(TransferNotice)); }

    public Task<ImportPlan> PreviewImportAsync(string path, ImportSource source, CancellationToken cancellationToken) =>
        _services.TransferFiles.PreviewAsync(path, source, State, cancellationToken);

    public async Task<bool> CommitImportAsync(ImportPlan plan, CancellationToken cancellationToken)
    {
        bool saved = await SaveAsync(() => _services.Import.CommitAsync(plan, cancellationToken));
        if (saved)
        {
            _importNotice = plan.HasWarnings ? (plan.Servers.Length, plan.Profiles.Length, plan.SkippedCount) : null;
            OnPropertyChanged(nameof(TransferNotice));
        }
        return saved;
    }

    public Task ExportAsync(string path, TunnelDeckExport export, CancellationToken cancellationToken) =>
        _services.TransferFiles.ExportAsync(path, export, cancellationToken);

    public async Task OpenTerminalAsync(Guid serverId)
    {
        if (!CanOpenTerminal) { return; }
        RemoteServer? server = State.Servers.FirstOrDefault(candidate => candidate.Id == serverId);
        if (server is null) { ReportError(UiText.Get("ShellViewModelTransfer_ThisServerNoLongerExistsSelectAnotherServer")); return; }
        _openingTerminal = true;
        OnPropertyChanged(nameof(CanOpenTerminal));
        ClearError();
        try { await _services.Terminal.OpenAsync(server); }
        catch (TerminalLaunchException exception) { ReportError(exception.Message); }
        catch (Exception) { ReportError(UiText.Get("ShellViewModelTransfer_TheSSHTerminalCouldNotBeOpenedCheck")); }
        finally { _openingTerminal = false; OnPropertyChanged(nameof(CanOpenTerminal)); }
    }
}
