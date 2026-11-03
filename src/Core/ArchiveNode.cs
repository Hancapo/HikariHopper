using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// One opened RPF in a chain of nested archives. FiveFury opens nested archives as independent
/// trees, so this records where each one came from: its owner, the binary entry it was read
/// from and its <see cref="Prefix"/>, the entry's full path from the root archive (empty for
/// the root), matching FiveFury Python's <c>RpfArchive.prefix</c>.
/// </summary>
public sealed class ArchiveNode
{
    private readonly List<ArchiveNode> children = [];
    private readonly RpfReadSession? session;

    private ArchiveNode(RpfArchive archive, ArchiveNode? parent, RpfBinaryFileEntry? entry,
        string? sourcePath, GameCrypto? crypto, RpfReadSession? session)
    {
        Archive = archive;
        Parent = parent;
        Entry = entry;
        SourcePath = sourcePath;
        Crypto = crypto;
        this.session = session;
        Prefix = parent is null || entry is null ? "" : parent.FullPath(entry);
    }

    public RpfArchive Archive { get; }

    public ArchiveNode? Parent { get; }

    /// <summary>The binary entry in <see cref="Parent"/> this archive was read from.</summary>
    public RpfBinaryFileEntry? Entry { get; }

    /// <summary>Full path of <see cref="Entry"/> from the root archive; empty for the root.</summary>
    public string Prefix { get; }

    /// <summary>The root archive's file; null for nested archives.</summary>
    public string? SourcePath { get; }

    public GameCrypto? Crypto { get; }

    public string Name => Archive.Name;

    public IReadOnlyList<ArchiveNode> Children => children;

    public ArchiveNode Root
    {
        get
        {
            ArchiveNode node = this;
            while (node.Parent is not null)
                node = node.Parent;
            return node;
        }
    }

    public bool IsClosed { get; private set; }

    /// <summary>Opens a root archive with a read-only handle that tolerates concurrent replacement.</summary>
    public static ArchiveNode OpenRoot(string path, GameCrypto? crypto)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        RpfReadSession session;
        try
        { session = RpfReadSession.Open(stream, Path.GetFileName(path), crypto); }
        catch { stream.Dispose(); throw; }
        return new ArchiveNode(session.Archive, null, null, Path.GetFullPath(path), crypto, session);
    }

    /// <summary>Full path of an entry of this archive from the root archive.</summary>
    public string FullPath(RpfEntry entry)
    {
        string local = entry.Path;
        if (Prefix.Length == 0)
            return local;
        return local.Length == 0 ? Prefix : $"{Prefix}/{local}";
    }

    /// <summary>The already loaded child archive read from <paramref name="entry"/>, if any.</summary>
    public ArchiveNode? LoadedChild(RpfEntry entry) =>
        children.FirstOrDefault(child => ReferenceEquals(child.Entry, entry));

    /// <summary>Loads (once) the nested archive stored in <paramref name="entry"/>.</summary>
    public ArchiveNode OpenNested(RpfBinaryFileEntry entry)
    {
        if (!ReferenceEquals(entry.Archive, Archive))
            throw new ArgumentException("The entry does not belong to this archive.", nameof(entry));
        if (LoadedChild(entry) is { } existing)
            return existing;
        RpfArchive nested;
        try
        { nested = Archive.OpenNested(entry, Crypto); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException
            or InvalidOperationException or EndOfStreamException)
        { throw new InvalidDataException($"FiveFury could not decode {entry.Name} as a nested RPF archive", exception); }
        var node = new ArchiveNode(nested, this, entry, null, Crypto, null);
        children.Add(node);
        return node;
    }

    /// <summary>Directory at a local path; <c>"."</c>, <c>""</c> or <c>"/"</c> select the root.</summary>
    public RpfDirectoryEntry? DirectoryAt(string path)
    {
        string normalized = EntryNames.NormalizedDirectory(path);
        return normalized == "." ? Archive.Root : Archive.Find(normalized) as RpfDirectoryEntry;
    }

    /// <summary>Resolves a path relative to this archive, descending into nested archives on the way.</summary>
    public (ArchiveNode Node, RpfEntry Entry)? FindEntry(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');
        if (Archive.Find(normalized) is { } direct)
            return (this, direct);
        foreach (RpfBinaryFileEntry nested in Archive.EnumerateEntries().OfType<RpfBinaryFileEntry>()
            .Where(item => item.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
        {
            string nestedPath = nested.Path;
            if (!normalized.StartsWith(nestedPath + "/", StringComparison.OrdinalIgnoreCase))
                continue;
            ArchiveNode child;
            try
            { child = OpenNested(nested); }
            catch (InvalidDataException)
            { continue; }
            return child.FindEntry(normalized[(nestedPath.Length + 1)..]);
        }
        return null;
    }

    /// <summary>Releases the root's file handle; every archive in the chain becomes unreadable.</summary>
    public void Close()
    {
        ArchiveNode root = Root;
        root.session?.Dispose();
        root.MarkClosed();
    }

    private void MarkClosed()
    {
        IsClosed = true;
        foreach (ArchiveNode child in children)
            child.MarkClosed();
    }
}

/// <summary>An entry together with the opened archive that owns it.</summary>
public sealed record ArchiveItem(ArchiveNode Node, RpfEntry Entry)
{
    public string FullPath => Node.FullPath(Entry);

    public string LocalPath => Entry.Path;
}
