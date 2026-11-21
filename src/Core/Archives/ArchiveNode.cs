namespace HikariHopper.Core;

/// <summary>
/// One opened game archive in a chain of nested archives, whatever its format (<see cref="Rpf7ArchiveNode"/> for
/// GTA V's RPF7). Nested archives open as independent trees, so this records where each one came from: its owner, the
/// entry it was read from and its <see cref="Prefix"/>, the entry's full path from the root archive (empty for the
/// root), matching FiveFury Python's <c>RpfArchive.prefix</c>. What the explorer lists, reads and searches goes through
/// this and <see cref="ArchiveEntry"/>; writing stays with the format's own type.
/// </summary>
public abstract class ArchiveNode
{
    private readonly List<ArchiveNode> children = [];

    protected ArchiveNode(ArchiveNode? parent, ArchiveEntry? entry, string? sourcePath, ArchiveKeys? keys)
    {
        Parent = parent;
        Entry = entry;
        SourcePath = sourcePath;
        Keys = keys;
        Prefix = parent is null || entry is null ? "" : parent.FullPath(entry);
    }

    public ArchiveNode? Parent { get; }

    /// <summary>The entry in <see cref="Parent"/> this archive was read from.</summary>
    public ArchiveEntry? Entry { get; }

    /// <summary>Full path of <see cref="Entry"/> from the root archive; empty for the root.</summary>
    public string Prefix { get; }

    /// <summary>The root archive's file; null for nested archives.</summary>
    public string? SourcePath { get; }

    /// <summary>The keys it was opened with, passed on to the archives nested in it.</summary>
    public ArchiveKeys? Keys { get; }

    public abstract string Name { get; }

    /// <summary>Its format as the properties name it: "RPF7".</summary>
    public abstract string Format { get; }

    /// <summary>The platform it was made for: "PC".</summary>
    public abstract string Platform { get; }

    /// <summary>How it's encrypted, as the properties name it: "None", "AES", "NG".</summary>
    public abstract string Encryption { get; }

    /// <summary>The format of the resources it holds, as the properties name it: "RSC7".</summary>
    public abstract string ResourceFormat { get; }

    /// <summary>Its root directory.</summary>
    public abstract ArchiveEntry RootDirectory { get; }

    /// <summary>Every entry in it, folders and files at any depth, without its root directory.</summary>
    public abstract IEnumerable<ArchiveEntry> EnumerateEntries();

    /// <summary>
    /// The entry at <paramref name="path"/>, normalized and relative to this archive, without looking inside nested
    /// archives; null when there's none.
    /// </summary>
    public abstract ArchiveEntry? EntryAt(string path);

    /// <summary>Reads the archive stored in <paramref name="entry"/>, one of this archive's.</summary>
    protected abstract ArchiveNode ReadNested(ArchiveEntry entry);

    /// <summary>Releases the file handle a root archive holds.</summary>
    protected abstract void ReleaseFile();

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

    /// <summary>
    /// Opens a root archive in whichever format it is, by its first four bytes: RDR2's RPF8 (<see cref="Rpf8ArchiveNode"/>)
    /// or GTA V's RPF7 (<see cref="Rpf7ArchiveNode"/>, which says what's wrong with anything else).
    /// </summary>
    public static ArchiveNode OpenRoot(string path, ArchiveKeys? keys)
    {
        Span<byte> start = stackalloc byte[4];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            if (stream.ReadAtLeast(start, 4, throwOnEndOfStream: false) == 4
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(start) == Rpf8ArchiveNode.Magic)
                return Rpf8ArchiveNode.OpenRoot(path, keys);
        return Rpf7ArchiveNode.OpenRoot(path, keys);
    }

    /// <summary>Full path of an entry of this archive from the root archive.</summary>
    public string FullPath(ArchiveEntry entry)
    {
        string local = entry.Path;
        if (Prefix.Length == 0)
            return local;
        return local.Length == 0 ? Prefix : $"{Prefix}/{local}";
    }

    /// <summary>The already loaded child archive read from <paramref name="entry"/>, if any.</summary>
    public ArchiveNode? LoadedChild(ArchiveEntry entry) =>
        children.FirstOrDefault(child => ReferenceEquals(child.Entry, entry));

    /// <summary>Loads (once) the nested archive stored in <paramref name="entry"/>.</summary>
    public ArchiveNode OpenNested(ArchiveEntry entry)
    {
        if (!ReferenceEquals(entry.Node, this))
            throw new ArgumentException("The entry does not belong to this archive.", nameof(entry));
        if (LoadedChild(entry) is { } existing)
            return existing;
        if (!entry.IsBinaryFile)
            throw new InvalidDataException($"{entry.Name} can't hold a nested archive");
        ArchiveNode node = ReadNested(entry);
        children.Add(node);
        return node;
    }

    /// <summary>Directory at a local path; <c>"."</c>, <c>""</c> or <c>"/"</c> select the root.</summary>
    public ArchiveEntry? DirectoryAt(string path)
    {
        string normalized = EntryNames.NormalizedDirectory(path);
        return normalized == "." ? RootDirectory : EntryAt(normalized) is { IsDirectory: true } directory ? directory : null;
    }

    /// <summary>Resolves a path relative to this archive, descending into nested archives on the way.</summary>
    public (ArchiveNode Node, ArchiveEntry Entry)? FindEntry(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');
        if (EntryAt(normalized) is { } direct)
            return (this, direct);
        foreach (ArchiveEntry nested in EnumerateEntries().Where(item => item.IsBinaryFile
            && item.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
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
        root.ReleaseFile();
        root.MarkClosed();
    }

    private void MarkClosed()
    {
        IsClosed = true;
        foreach (ArchiveNode child in children)
            child.MarkClosed();
    }
}

/// <summary>
/// An entry of an opened archive (<see cref="ArchiveNode"/>), whatever its format: a directory or a file, with what
/// the explorer lists and reads of it.
/// </summary>
public abstract class ArchiveEntry
{
    protected ArchiveEntry(ArchiveNode node) => Node = node;

    /// <summary>The archive it belongs to.</summary>
    public ArchiveNode Node { get; }

    public abstract string Name { get; }

    /// <summary>Its path within its archive; empty for the root directory.</summary>
    public abstract string Path { get; }

    public abstract bool IsDirectory { get; }

    /// <summary>A directory's entries, in the archive's order; none for a file.</summary>
    public abstract IReadOnlyList<ArchiveEntry> Children { get; }

    /// <summary>A file's size once read; 0 when the archive doesn't record it.</summary>
    public abstract long LogicalSize { get; }

    /// <summary>A file's size as stored in the archive.</summary>
    public abstract long StoredSize { get; }

    /// <summary>A file's size as the explorer lists it: its size once read, else as stored.</summary>
    public long Size => LogicalSize > 0 ? LogicalSize : StoredSize;

    /// <summary>Whether a file is kept compressed in the archive.</summary>
    public abstract bool Compressed { get; }

    /// <summary>Whether a file is a resource (a texture dictionary, a model and the rest).</summary>
    public abstract bool IsResource { get; }

    /// <summary>A file that isn't a resource: one that may hold a nested archive.</summary>
    public bool IsBinaryFile => !IsDirectory && !IsResource;

    /// <summary>A resource's version as its header records it; null for anything else.</summary>
    public abstract int? ResourceVersion { get; }

    /// <summary>Whether it no longer belongs to its archive (removed since it was listed).</summary>
    public virtual bool IsDetached => false;

    /// <summary>A file's bytes as a standalone file: decrypted and, for a binary file, decompressed.</summary>
    public abstract byte[] Read();
}

/// <summary>An entry together with the opened archive that owns it.</summary>
public sealed record ArchiveItem(ArchiveNode Node, ArchiveEntry Entry)
{
    public string FullPath => Node.FullPath(Entry);

    public string LocalPath => Entry.Path;
}
