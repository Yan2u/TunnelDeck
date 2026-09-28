using System.Diagnostics;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Persistence;

namespace TunnelDeck.Infrastructure.Persistence;

public sealed class FileConfigurationLock(string path) : IConfigurationLock
{
    private readonly string _path = Path.GetFullPath(path);

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                return new Lease(new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 or 11 &&
                Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new StateStoreException(StateStoreFailure.Unavailable,
                    UiText.Invariant("Diagnostic_FileConfigurationLock_SavedDataIsUnavailableOrInUseCheck"));
            }
        }
    }

    private sealed class Lease(FileStream stream) : IDisposable
    {
        public void Dispose()
        {
            // Releasing a lock must not turn an already committed transaction into a reported failure.
            try { stream.Dispose(); }
            catch (IOException) { }
        }
    }
}
