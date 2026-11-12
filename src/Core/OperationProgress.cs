namespace HikariHopper.Core;

/// <summary>
/// How far the operation running on this thread has got, and whether it has been asked to stop. The explorer's
/// operation queue gives each operation one (<see cref="Use"/>); the file loops in <see cref="EntryOperations"/>,
/// <see cref="RpfProvider"/> and <see cref="ArchiveWriter"/> report through <see cref="Current"/> without being told,
/// and code run anywhere else reports to <see cref="None"/>, which does nothing and never stops.
/// </summary>
public sealed class OperationProgress
{
    [ThreadStatic]
    private static OperationProgress? current;

    private readonly CancellationToken token;
    private readonly Action<OperationStep>? report;
    private readonly Action<string>? starting;
    private long extraBytes;

    /// <param name="starting">Told as each item starts, and as a tracked write starts; tests hold it there to see an
    /// operation part way.</param>
    public OperationProgress(CancellationToken token, Action<OperationStep>? report, Action<string>? starting = null)
    {
        this.token = token;
        this.report = report;
        this.starting = starting;
    }

    /// <summary>What reports nowhere and never stops.</summary>
    public static OperationProgress None { get; } = new(CancellationToken.None, null);

    /// <summary>The operation running on this thread, or <see cref="None"/>.</summary>
    public static OperationProgress Current => current ?? None;

    /// <summary>How many items it has counted, of how many it expects; nothing counted yet when zero.</summary>
    public int Done { get; private set; }

    public int Total { get; private set; }

    /// <summary>Makes this the operation of the calling thread until disposed.</summary>
    public IDisposable Use()
    {
        OperationProgress? previous = current;
        current = this;
        return new Restore(() => current = previous);
    }

    /// <summary>Stops here, with <see cref="OperationCanceledException"/>, once it has been asked to.</summary>
    public void Check() => token.ThrowIfCancellationRequested();

    /// <summary>Sets how many items it will count, and counts from none.</summary>
    public void Expect(int items)
    {
        Total = Math.Max(0, items);
        Done = 0;
    }

    /// <summary>
    /// Starts on the next item, named <paramref name="name"/>: stops first if asked to, then reports it. Every report
    /// goes out; whoever shows them keeps up with the latest.
    /// </summary>
    public void Item(string name)
    {
        Check();
        Report(new OperationStep(Done, Total, name, Percent: false));
        starting?.Invoke(name);
        if (Total > 0)
            Done = Math.Min(Done + 1, Total);
    }

    /// <summary>Bytes an archive's rewrite will write besides the archive's own: what an import adds to it.</summary>
    public void ExpectExtraBytes(long bytes) => extraBytes += Math.Max(0, bytes);

    /// <summary>
    /// <paramref name="output"/>, reporting what has been written to it as a share of <paramref name="expected"/> bytes
    /// (and any <see cref="ExpectExtraBytes"/>), and stopping a write once asked to. The share stops short of all of it,
    /// as the result may come out a little larger than expected.
    /// </summary>
    public Stream Track(Stream output, long expected, string name) =>
        ReferenceEquals(this, None) ? output : new TrackedStream(output, this, Math.Max(1, expected + extraBytes), name);

    private void Report(OperationStep step) => report?.Invoke(step);

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private sealed class TrackedStream(Stream inner, OperationProgress progress, long expected, string name) : Stream
    {
        private long written;
        private bool begun;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!begun)
            {
                begun = true;
                progress.Report(new OperationStep(0, 1000, name, Percent: true));
                progress.starting?.Invoke(name);
            }
            progress.Check();
            inner.Write(buffer);
            written += buffer.Length;
            // Per mille of what's expected, held under all of it until the write is done.
            int share = (int)Math.Min(990, written * 1000 / expected);
            progress.Report(new OperationStep(share, 1000, name, Percent: true));
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            // The caller owns the file it wraps.
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// One report of an operation's progress: <paramref name="Done"/> of <paramref name="Total"/> items, on
/// <paramref name="Current"/>; or, with <paramref name="Percent"/>, per mille of the bytes it expects to write.
/// </summary>
public readonly record struct OperationStep(int Done, int Total, string Current, bool Percent);
