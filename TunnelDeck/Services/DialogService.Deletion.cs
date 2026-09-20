using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Controls;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Services;

public sealed partial class DialogService
{
    public async Task<bool> ConfirmDeletionAsync(DeletionImpact impact, string updateNotice = "",
        CancellationToken cancellationToken = default) =>
        await ShowAsync(() => new DeletionDialog(new DeletionConfirmationModel(impact, updateNotice)), cancellationToken)
            .ConfigureAwait(false) == ContentDialogResult.Primary;
}
