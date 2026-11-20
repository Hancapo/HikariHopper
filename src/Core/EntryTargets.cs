using FiveFury;
using FiveFury.Cryptography;

namespace HikariHopper.Core;

/// <summary>Summary of the open workspace for the title and status bar.</summary>
public sealed record WorkspaceInfo(string Name, string SourcePath, string Version, string Platform);

/// <summary>A game installation whose keys have been loaded: its folder, its edition (<see cref="GameCatalog"/>) and its keys.</summary>
public sealed record LoadedGame(string Root, string Edition, ArchiveKeys Keys)
{
    /// <summary>A GTA V installation, its edition from <paramref name="target"/>.</summary>
    public LoadedGame(string root, GameTarget target, GameCrypto crypto)
        : this(root, target == GameTarget.Enhanced ? GameSettings.EnhancedEdition : GameSettings.LegacyEdition, new Rpf7Keys(crypto))
    {
    }

    /// <summary>Whether it's GTA V, whose texture tables, models and texture editor are GTA V's only so far.</summary>
    public bool IsGtaV => Keys is Rpf7Keys;

    /// <summary>GTA V's edition as FiveFury takes it; GTA V's only.</summary>
    public GameTarget Target => IsGtaV
        ? Edition == GameSettings.EnhancedEdition ? GameTarget.Enhanced : GameTarget.Legacy
        : throw new InvalidOperationException($"{Edition} isn't GTA V");

    /// <summary>GTA V's keys; GTA V's only.</summary>
    public GameCrypto Crypto => Keys is Rpf7Keys gta ? gta.Crypto : throw new InvalidOperationException($"{Edition} isn't GTA V");
}

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
