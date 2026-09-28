using System.Text;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Infrastructure.Persistence;

namespace TunnelDeck.Infrastructure.Transfer;

public sealed partial class ResourceTransferFiles(string? applicationDataDirectory = null)
{
    private async Task<ImportPlan> PreviewCoreAsync(string path, ImportSource source, AppState state, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
            return source switch
            {
                ImportSource.TunnelDeckJson => ImportPlanner.Plan(ExportJsonCodec.Decode(bytes), state, File.Exists),
                ImportSource.SshConfig => SshConfigParser.Plan(new UTF8Encoding(false, true).GetString(bytes), state,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists),
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_TheFileCouldNotBeReadChooseAn")); }
    }

    private async Task ExportCoreAsync(string path, TunnelDeckExport export, CancellationToken cancellationToken)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_SaveTheExportAsASeparateJsonFile")); }
            if (applicationDataDirectory is not null && fullPath.StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDataDirectory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_ChooseALocationOutsideTunnelDeckSDataDirectory")); }
            byte[] bytes = ExportJsonCodec.Encode(export);
            var file = new AtomicJsonFile(fullPath);
            using IDisposable lease = await file.AcquireAsync(cancellationToken).ConfigureAwait(false);
            await file.WriteAsync(bytes, preserveBackup: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is StateStoreException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_TheExportCouldNotBeSavedCheckThe")); }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > ExportJsonCodec.MaximumBytes) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_ChooseAFileSmallerThan8MiB")); }
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > ExportJsonCodec.MaximumBytes) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceTransferFiles_ChooseAFileSmallerThan8MiB")); }
            output.Write(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }
}
