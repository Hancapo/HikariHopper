namespace HikariHopper.Core;

/// <summary>One explorer row, independent of whether it lives on disk or inside an RPF.</summary>
/// <param name="NativeEntry">A <see cref="FileSystemInfo"/> for loose items or an <see cref="ArchiveItem"/> for archived ones.</param>
public sealed record EntryRecord(
    string Name,
    string Path,
    string Kind,
    long Size,
    int Children = 0,
    object? NativeEntry = null,
    int NavigableChildren = 0)
{
    public const string FolderKind = "Folder";

    public bool IsDirectory => Kind == FolderKind;

    public ArchiveItem? ArchiveItem => NativeEntry as ArchiveItem;
}
