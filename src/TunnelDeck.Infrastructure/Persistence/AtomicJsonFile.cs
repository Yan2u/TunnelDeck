using TunnelDeck.Core.Persistence;

namespace TunnelDeck.Infrastructure.Persistence;

internal sealed class AtomicJsonFile(string path, AtomicFileOperations? operations = null)
{
    private readonly AtomicFileOperations _operations = operations ?? new();
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string BackupPath => Path + ".bak";

    public ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken) =>
        new FileConfigurationLock(Path + ".lock").AcquireAsync(cancellationToken);

    public async Task<byte[]?> ReadAsync(bool backup, CancellationToken cancellationToken)
    {
        try { return await File.ReadAllBytesAsync(backup ? BackupPath : Path, cancellationToken).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StateStoreException(StateStoreFailure.Unavailable, UiText.Invariant("Diagnostic_AtomicJsonFile_SavedDataCouldNotBeReadCheckAccess"));
        }
    }

    /// <summary>Caller holds the file lock. Cancellation is honored only before atomic publication.</summary>
    public async Task WriteAsync(byte[] bytes, bool preserveBackup, CancellationToken cancellationToken)
    {
        string temporaryPath = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (Stream stream = _operations.CreateTemporaryFile(temporaryPath))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await _operations.FlushToDiskAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { _operations.Publish(temporaryPath, Path, preserveBackup ? null : BackupPath); }
            catch (IOException)
            {
                // Some filesystem adapters can report an error after replacement. Reconcile while still locked.
                if (!PublishedBytesMatch(bytes)) { throw; }
            }
            // The rename/replace above is the commit boundary. Do not await, check cancellation or throw below it.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StateStoreException(StateStoreFailure.WriteFailed,
                UiText.Invariant("Diagnostic_AtomicJsonFile_SavedDataCouldNotBeWrittenTheEdit"));
        }
        finally
        {
            try { _operations.DeleteTemporaryFile(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private bool PublishedBytesMatch(byte[] expected)
    {
        try { return File.ReadAllBytes(Path).AsSpan().SequenceEqual(expected); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }
}
