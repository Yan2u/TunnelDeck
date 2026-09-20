using TunnelDeck.Infrastructure.Persistence;

namespace TunnelDeck.Infrastructure.Tests.Fixtures;

public enum FileFault { None, PartialWrite, Flush, Publish, AfterPublish, Cleanup }

internal sealed class FaultingFileOperations : AtomicFileOperations
{
    public FileFault Fault { get; set; }
    public Func<Task>? BeforeFlush { get; set; }
    public Action? AfterPublish { get; set; }

    public override Stream CreateTemporaryFile(string path)
    {
        Stream stream = base.CreateTemporaryFile(path);
        return Fault == FileFault.PartialWrite ? new PartialWriteStream(stream) : stream;
    }

    public override async Task FlushToDiskAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (BeforeFlush is not null) { await BeforeFlush(); }
        if (Fault == FileFault.Flush) { throw new IOException("Injected flush failure."); }
        await base.FlushToDiskAsync(stream, cancellationToken);
    }

    public override void Publish(string temporaryPath, string path, string? backupPath)
    {
        if (Fault == FileFault.Publish) { throw new IOException("Injected replacement failure."); }
        base.Publish(temporaryPath, path, backupPath);
        AfterPublish?.Invoke();
        if (Fault == FileFault.AfterPublish) { throw new IOException("Injected error after publication."); }
    }

    public override void DeleteTemporaryFile(string path)
    {
        if (Fault == FileFault.Cleanup) { throw new IOException("Injected temporary-file cleanup failure."); }
        base.DeleteTemporaryFile(path);
    }

    private sealed class PartialWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer[..Math.Max(1, buffer.Length / 2)], cancellationToken);
            throw new IOException("Injected failure after a partial write.");
        }
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
