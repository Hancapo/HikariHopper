using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// A GTA V RPF7 archive, read through FiveFury: the <see cref="ArchiveNode"/> the explorer lists and reads, and
/// FiveFury's own <see cref="RpfArchive"/> for what only RPF7 does here (writing, the texture tables, the texture
/// editor).
/// </summary>
public sealed class Rpf7ArchiveNode : ArchiveNode
{
    private readonly RpfReadSession? session;
    // One entry per FiveFury entry, so an entry is the same object every time it's asked for.
    private readonly Dictionary<RpfEntry, Rpf7Entry> entries = new(ReferenceEqualityComparer.Instance);

    private Rpf7ArchiveNode(RpfArchive archive, Rpf7ArchiveNode? parent, Rpf7Entry? entry, string? sourcePath,
        ArchiveKeys? keys, RpfReadSession? session)
        : base(parent, entry, sourcePath, keys)
    {
        Archive = archive;
        this.session = session;
    }

    /// <summary>FiveFury's archive.</summary>
    public RpfArchive Archive { get; }

    /// <summary>GTA V's keys it was opened with; null for an archive opened without.</summary>
    public GameCrypto? Crypto => (Keys as Rpf7Keys)?.Crypto;

    public override string Name => Archive.Name;

    public override string Format => "RPF7";

    public override string Platform => Archive.Platform.ToString().ToUpperInvariant();

    public override string Encryption => Archive.Encryption switch
    {
        RpfEncryption.None => "None",
        RpfEncryption.Open => "Open",
        RpfEncryption.Aes or RpfEncryption.Ps3Aes => "AES",
        RpfEncryption.Ng => "NG",
        var other => other.ToString(),
    };

    public override string ResourceFormat => "RSC7";

    public override ArchiveEntry RootDirectory => Wrap(Archive.Root);

    /// <summary>Opens a root archive with a read-only handle that tolerates concurrent replacement.</summary>
    public static new Rpf7ArchiveNode OpenRoot(string path, ArchiveKeys? keys)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        RpfReadSession session;
        try
        { session = RpfReadSession.Open(stream, Path.GetFileName(path), (keys as Rpf7Keys)?.Crypto); }
        catch { stream.Dispose(); throw; }
        return new Rpf7ArchiveNode(session.Archive, null, null, Path.GetFullPath(path), keys, session);
    }

    /// <summary>The entry for FiveFury's <paramref name="native"/>, one of this archive's.</summary>
    public ArchiveEntry Wrap(RpfEntry native)
    {
        if (!entries.TryGetValue(native, out Rpf7Entry? entry))
            entries[native] = entry = new Rpf7Entry(this, native);
        return entry;
    }

    /// <summary>FiveFury's entry behind <paramref name="entry"/>, one of this archive's.</summary>
    public static RpfEntry Native(ArchiveEntry entry) => ((Rpf7Entry)entry).Native;

    public override IEnumerable<ArchiveEntry> EnumerateEntries() =>
        Archive.EnumerateEntries().Where(item => !ReferenceEquals(item, Archive.Root)).Select(Wrap);

    public override ArchiveEntry? EntryAt(string path) => Archive.Find(path) is { } found ? Wrap(found) : null;

    protected override ArchiveNode ReadNested(ArchiveEntry entry)
    {
        var binary = (RpfBinaryFileEntry)Native(entry);
        RpfArchive nested;
        try
        { nested = Archive.OpenNested(binary, Crypto); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException
            or InvalidOperationException or EndOfStreamException)
        { throw new InvalidDataException($"FiveFury could not decode {binary.Name} as a nested RPF archive", exception); }
        return new Rpf7ArchiveNode(nested, this, (Rpf7Entry)entry, null, Keys, null);
    }

    protected override void ReleaseFile() => session?.Dispose();

    private sealed class Rpf7Entry(Rpf7ArchiveNode node, RpfEntry native) : ArchiveEntry(node)
    {
        private IReadOnlyList<ArchiveEntry>? children;

        public RpfEntry Native { get; } = native;

        public override string Name => Native.Name;

        public override string Path => Native.Path;

        public override bool IsDirectory => Native is RpfDirectoryEntry;

        public override IReadOnlyList<ArchiveEntry> Children => children ??= Native is RpfDirectoryEntry directory
            ? [.. directory.Children.Select(node.Wrap)]
            : [];

        public override long LogicalSize => Native is RpfFileEntry file ? file.LogicalSize : 0;

        public override long StoredSize => Native is RpfFileEntry file ? file.StoredSize : 0;

        // A resource is kept deflated; a binary file only when its entry says so.
        public override bool Compressed => Native is RpfResourceFileEntry or RpfBinaryFileEntry { Compression: not RpfBinaryCompression.None };

        public override bool IsResource => Native is RpfResourceFileEntry;

        public override int? ResourceVersion => Native is RpfResourceFileEntry resource ? (int)resource.Header.Version : null;

        public override bool IsDetached => Native.Archive is null;

        public override byte[] Read() => Native is RpfFileEntry file
            ? file.ReadStandalone()
            : throw new ArgumentException($"Cannot read a directory as a file: {Name}");
    }
}
