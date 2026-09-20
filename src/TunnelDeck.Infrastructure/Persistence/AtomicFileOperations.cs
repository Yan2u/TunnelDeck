namespace TunnelDeck.Infrastructure.Persistence;

/// <summary>Small injectable filesystem boundary for real partial-write/flush/publish fault tests.</summary>
internal class AtomicFileOperations
{
    public virtual Stream CreateTemporaryFile(string path) => new FileStream(path,
        FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);

    public virtual async Task FlushToDiskAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ((FileStream)stream).Flush(flushToDisk: true);
    }

    public virtual void Publish(string temporaryPath, string path, string? backupPath)
    {
        if (File.Exists(path)) { File.Replace(temporaryPath, path, backupPath); }
        else { File.Move(temporaryPath, path); }
    }

    public virtual void DeleteTemporaryFile(string path) => File.Delete(path);
}
