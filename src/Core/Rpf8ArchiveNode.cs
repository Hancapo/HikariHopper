using RedFury.Resources;
using RedFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// A Red Dead Redemption 2 RPF8 archive, read through RedFury. RPF8 stores no names, only their hashes: its folders are
/// rebuilt from RedFury's list of the game's names (<see cref="ArchiveDirectory"/>), and an entry whose name isn't on it
/// shows as its hash (<c>0x1A2B3C4D.ydr</c>). Read only: HikariHopper doesn't write RPF8 yet.
/// </summary>
public sealed class Rpf8ArchiveNode : ArchiveNode
{
    /// <summary>The first four bytes of an RPF8 archive, read as a little-endian number.</summary>
    public const uint Magic = RpfHeader.Magic;

    private readonly RpfArchive archive;
    private readonly Rpf8Entry root;
    private readonly List<Rpf8Entry> all = [];
    private readonly Dictionary<string, Rpf8Entry> byPath = new(StringComparer.OrdinalIgnoreCase);

    private Rpf8ArchiveNode(RpfArchive archive, string name, Rpf8ArchiveNode? parent, Rpf8Entry? entry, string? sourcePath, Rdr2Keys? keys)
        : base(parent, entry, sourcePath, keys)
    {
        this.archive = archive;
        Name = name;
        root = Directory(ArchiveDirectory.Build(archive.Entries), "");
    }

    public override string Name { get; }

    public override string Format => "RPF8";

    // 'y', RAGE's 64-bit PC.
    public override string Platform => archive.Header.PlatformId == 0x79 ? "PC" : $"0x{archive.Header.PlatformId:X2}";

    public override string Encryption => archive.Header.TableKeyId == 255 ? "None" : "AES";

    public override string ResourceFormat => "RSC8";

    public override ArchiveEntry RootDirectory => root;

    /// <summary>
    /// Opens a root archive. Its keys are RDR2's: the game's when given, else the ones HikariHopper ships, so an RPF8
    /// opened on its own, or from a GTA V tab, opens too.
    /// </summary>
    public static new Rpf8ArchiveNode OpenRoot(string path, ArchiveKeys? keys)
    {
        Rdr2Keys rdr2 = keys as Rdr2Keys ?? Rdr2Keys.Bundled;
        string full = Path.GetFullPath(path);
        RpfArchive opened = RpfArchive.Open(full, rdr2.Crypto);
        try
        {
            return new Rpf8ArchiveNode(opened, Path.GetFileName(full), null, null, full, rdr2);
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    public override IEnumerable<ArchiveEntry> EnumerateEntries() => all;

    public override ArchiveEntry? EntryAt(string path) => byPath.GetValueOrDefault(path.Replace('\\', '/').Trim('/'));

    protected override ArchiveNode ReadNested(ArchiveEntry entry)
    {
        var file = (Rpf8Entry)entry;
        RpfArchive nested;
        try
        {
            nested = file.File!.Entry.OpenArchive();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException
            or InvalidOperationException or EndOfStreamException or MissingRpfKeyException)
        {
            throw new InvalidDataException($"RedFury could not decode {file.Name} as a nested RPF archive", exception);
        }
        try
        {
            return new Rpf8ArchiveNode(nested, file.Name, this, file, null, Keys as Rdr2Keys);
        }
        catch
        {
            nested.Dispose();
            throw;
        }
    }

    // Every archive in the chain holds a session of its own (a nested one is read from its owner's, or kept in memory).
    protected override void ReleaseFile()
    {
        foreach (ArchiveNode child in Children)
            if (child is Rpf8ArchiveNode nested)
                nested.ReleaseFile();
        archive.Dispose();
    }

    private Rpf8Entry Directory(ArchiveDirectory directory, string path)
    {
        var entry = new Rpf8Entry(this, directory.Name, path, null);
        if (path.Length > 0)
            Add(entry);
        var children = new List<ArchiveEntry>();
        foreach (ArchiveDirectory folder in directory.Directories)
            children.Add(Directory(folder, Join(path, folder.Name)));
        foreach (ArchiveFile file in directory.Files)
        {
            var child = new Rpf8Entry(this, file.Name, Join(path, file.Name), file);
            Add(child);
            children.Add(child);
        }
        entry.SetChildren(children);
        return entry;
    }

    // Listed in the archive's order; a second entry of the same name (RPF8 keeps collisions) is still listed, and the
    // first is the one found by path.
    private void Add(Rpf8Entry entry)
    {
        all.Add(entry);
        byPath.TryAdd(entry.Path, entry);
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private sealed class Rpf8Entry(Rpf8ArchiveNode node, string name, string path, ArchiveFile? file) : ArchiveEntry(node)
    {
        private IReadOnlyList<ArchiveEntry> children = [];

        public ArchiveFile? File { get; } = file;

        private RpfRecord Record => File!.Entry.Record;

        public override string Name { get; } = name;

        public override string Path { get; } = path;

        public override bool IsDirectory => File is null;

        public override IReadOnlyList<ArchiveEntry> Children => children;

        public void SetChildren(IReadOnlyList<ArchiveEntry> list) => children = list;

        public override long LogicalSize => File is null ? 0 : (long)Record.LogicalSize;

        public override long StoredSize => File is null ? 0 : Record.StoredSize;

        public override bool Compressed => File is not null && Record.CompressorId != 0;

        public override bool IsResource => File is not null && Record.IsResource;

        public override int? ResourceVersion => IsResource ? Record.ResourceVersion : null;

        // A resource as a standalone RSC8 file: its own compressed stream when it can be kept (not signed), else its
        // data compressed again; any other file decrypted and decompressed.
        public override byte[] Read()
        {
            if (File is null)
                throw new ArgumentException($"Cannot read a directory as a file: {Name}");
            RpfEntry entry = File.Entry;
            if (!Record.IsResource)
                return entry.Read();
            if (Record.CompressorId == 0 || Record.HasAuthentication)
                return entry.ReadResource().Write();
            // Its header, then its stream as stored, decrypted: copied, not decompressed and compressed again.
            byte[] stored = entry.ReadDecrypted();
            byte[] standalone = new byte[checked(stored.Length + 16)];
            Rsc8Header.FromRecord(Record).Write(standalone, includeEncryption: false);
            stored.CopyTo(standalone, 16);
            return standalone;
        }
    }
}
