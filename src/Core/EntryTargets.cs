using FiveFury;
using FiveFury.Cryptography;

namespace HikariHopper.Core;

/// <summary>Summary of the open workspace for the title and status bar.</summary>
public sealed record WorkspaceInfo(string Name, string SourcePath, string Version, string Platform);

/// <summary>A game installation whose keys have been loaded.</summary>
public sealed record LoadedGame(string Root, GameTarget Target, GameCrypto Crypto);

/// <summary>An archived file that can be saved back into its root RPF.</summary>
public sealed record ArchiveEntryTarget(string RootPath, string EntryPath, GameCrypto? Crypto, ArchiveNode SourceArchive)
{
    /// <summary>Releases source handles before Windows atomically replaces the RPF.</summary>
    public void Prepare() => SourceArchive.Close();

    /// <summary>Replaces one entry and atomically saves its root RPF archive.</summary>
    public void Save(byte[] data)
    {
        Prepare();
        ArchiveWriter.ReplaceEntry(RootPath, Crypto, EntryPath, data);
    }
}

/// <summary>Where a new entry is created: a loose folder or a directory inside an (optionally nested) RPF.</summary>
public sealed record EntryCreationTarget(
    string? Directory = null,
    string? ArchivePath = null,
    string ArchivePrefix = "",
    string InternalDirectory = ".",
    GameTarget? Game = null,
    GameCrypto? Crypto = null)
{
    public bool InArchive => ArchivePath is not null;
}

/// <summary>Items selected for deletion at one location.</summary>
public sealed record EntryDeletionTarget(
    EntryCreationTarget Location,
    IReadOnlyList<string> LoosePaths,
    IReadOnlyList<string> ArchivePaths)
{
    public bool InArchive => Location.InArchive;
}
