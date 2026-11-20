namespace HikariHopper.Core;

/// <summary>How a file is kept: loose on disk, or in an archive as it is or compressed.</summary>
public enum EntryStorage
{
    Loose,
    Uncompressed,
    Compressed,
}

/// <summary>One explorer row, independent of whether it lives on disk or inside an RPF.</summary>
/// <param name="Size">The file's whole size, as read: what the open-size limits compare.</param>
/// <param name="NativeEntry">A <see cref="FileSystemInfo"/> for loose items or an <see cref="ArchiveItem"/> for archived ones.</param>
/// <param name="StoredSize">The bytes an archived file takes in its archive; for a compressed one, less than <paramref name="Size"/>.</param>
public sealed record EntryRecord(
    string Name,
    string Path,
    string Kind,
    long Size,
    int Children = 0,
    object? NativeEntry = null,
    int NavigableChildren = 0,
    EntryStorage Storage = EntryStorage.Loose,
    long StoredSize = 0)
{
    public const string FolderKind = "Folder";

    public bool IsDirectory => Kind == FolderKind;

    /// <summary>The size the explorer shows and sorts by: what a compressed file takes, otherwise its size.</summary>
    public long ShownSize => Storage == EntryStorage.Compressed ? StoredSize : Size;

    public ArchiveItem? ArchiveItem => NativeEntry as ArchiveItem;
}
