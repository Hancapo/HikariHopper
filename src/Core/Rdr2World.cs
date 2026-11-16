using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RedFury.Rpf;
using RedFury.Textures;
using RedFury.World;

namespace HikariHopper.Core;

/// <summary>
/// Red Dead Redemption 2's world, as RedFury's world viewer reads it: the base map's index (its maps, every placement in
/// them and their levels of detail, from the game's own content files, without DLC or optional groups), and the
/// catalog of the archives its assets and textures are read from. The index is built in the background the first time
/// (a minute or two, saying how far it has got) and cached on disk until the game's archives change; then it's read
/// back in a few seconds. One per game folder, shared by its tabs.
/// </summary>
internal sealed partial class Rdr2World
{
    private static readonly ConcurrentDictionary<string, Rdr2World> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly Rdr2Crypto crypto;
    private readonly object gate = new();
    private Rdr2WorldProgress progress = new("", null);

    internal Rdr2World(string root, Rdr2Crypto crypto) => (this.root, this.crypto) = (root, crypto);

    /// <summary>The world of the RDR2 game at <paramref name="root"/>, shared by every caller.</summary>
    public static Rdr2World ForGame(string root, Rdr2Crypto crypto) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), path => new Rdr2World(path, crypto));

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>
    /// Builds the index from the game's archives; tests replace it. Given the game folder, its keys, a report of how far
    /// it has got, and a cancellation.
    /// </summary>
    internal static Func<string, Rdr2Crypto, Action<Rdr2WorldProgress>, CancellationToken, Rdr2WorldIndex> Builder { get; set; } = Build;

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>Where the index has got, while it's being made; raised on the thread making it.</summary>
    public event Action<Rdr2WorldProgress>? ProgressChanged;

    /// <summary>Where the index has got.</summary>
    public Rdr2WorldProgress Progress
    {
        get
        {
            lock (gate)
                return progress;
        }
    }

    private Task<Rdr2WorldIndex>? index;

    /// <summary>The index, once it's ready: null before, or when it couldn't be made.</summary>
    public Rdr2WorldIndex? Index => index is { IsCompletedSuccessfully: true } done ? done.Result : null;

    /// <summary>Why the index couldn't be made; null while it's being made, or once it is.</summary>
    public string? Failure => index is { IsFaulted: true } failed ? failed.Exception!.GetBaseException().Message : null;

    /// <summary>Starts making (or reading) the index in the background; does nothing while that runs or once it's done.</summary>
    public Task<Rdr2WorldIndex> Load()
    {
        lock (gate)
            return index ??= Task.Run(() => Builder(root, crypto, Report, CancellationToken.None));
    }

    private void Report(Rdr2WorldProgress value)
    {
        lock (gate)
            progress = value;
        ProgressChanged?.Invoke(value);
    }

    /// <summary>What a cache must be made with to be read: HikariHopper's own, the game's keys, and every RPF as it is.</summary>
    internal static string Fingerprint(string root, Rdr2Crypto crypto)
    {
        var input = new StringBuilder(CacheVersion).Append('|').Append(crypto.KeyCount);
        foreach (string path in Directory.EnumerateFiles(root, "*.rpf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = new FileInfo(path);
            input.Append('\n').Append(Path.GetRelativePath(root, path).ToUpperInvariant()).Append('|').Append(file.Length).Append('|')
                .Append(file.LastWriteTimeUtc.Ticks);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    /// <summary>HikariHopper's world cache format; RedFury checks its own inside the file.</summary>
    private const string CacheVersion = "hh-rdr2-world-1";

    /// <summary>Where a game's index is cached: beside the texture tables, which the settings' cache size and deletion cover.</summary>
    internal static string CachePath(string root) =>
        Path.Combine(GameTextures.CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + "-world.bin");

    private static Rdr2WorldIndex Build(string root, Rdr2Crypto crypto, Action<Rdr2WorldProgress> report, CancellationToken cancellation)
    {
        report(new Rdr2WorldProgress("Reading the game's content", null));
        WorldGameSource sources = WorldGameSource.ForGame(root, crypto);
        var reads = new RpfReadSession(crypto);
        RpfTextureCatalog assets = sources.CreateBaseCatalog(readSession: reads);
        // The archives' scan is the index's and the streaming's alike: it overlaps reading the cache.
        Task scanning = Task.Run(() => assets.Scan(cancellation), cancellation);
        string fingerprint = Fingerprint(root, crypto), cache = CachePath(root);
        WorldCatalog? catalog = null;
        if (File.Exists(cache))
        {
            report(new Rdr2WorldProgress("Reading the world from the cache", null));
            try
            {
                catalog = WorldCatalog.ReadCache(cache, fingerprint);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OverflowException
                or UnauthorizedAccessException)
            {
            }
        }
        if (catalog is null)
        {
            report(new Rdr2WorldProgress("Scanning the game's archives", null));
            scanning.GetAwaiter().GetResult();
            catalog = WorldCatalog.LoadMetadata(assets.Assets(74), assets.Assets(73), [.. sources.Diagnostics, .. assets.Diagnostics], crypto,
                message => report(Rdr2WorldProgress.From(message)), cancellation);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                catalog.SaveCache(cache, fingerprint);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        scanning.GetAwaiter().GetResult();
        report(new Rdr2WorldProgress("Ready", 1));
        return new Rdr2WorldIndex(catalog.MapCount, catalog.Lod, assets, reads);
    }
}

/// <summary>A game's world, made: its index, and the archives its assets are read from.</summary>
/// <param name="MapCount">The base map's maps.</param>
/// <param name="Lod">Their placements and their levels of detail.</param>
/// <param name="Assets">The archives the world's assets and textures are found in, scanned.</param>
/// <param name="Reads">The archives held open for reading them.</param>
internal sealed record Rdr2WorldIndex(int MapCount, WorldLodGraph Lod, RpfTextureCatalog Assets, RpfReadSession Reads)
{
    /// <summary>Every placement in it: an entity, or one of a batch's instances.</summary>
    public long Placements { get; } = Lod.Nodes.Sum(node => (long)Math.Max(1, node.Instances.Count));
}

/// <summary>Where a world's index has got: its stage, as the loading screen says it, and how far, 0 to 1, when that's known.</summary>
internal sealed partial record Rdr2WorldProgress(string Stage, double? Fraction)
{
    [GeneratedRegex(@"^Discovering maps (\d+)/(\d+)")]
    private static partial Regex Discovering();

    /// <summary>RedFury's report while it reads the maps ("Discovering maps 640/7303 · …"), as a stage and how far.</summary>
    public static Rdr2WorldProgress From(string message)
    {
        if (Discovering().Match(message) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int done)
            && int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int total) && total > 0)
            return new Rdr2WorldProgress("Reading the maps", Math.Clamp(done / (double)total, 0, 1));
        return new Rdr2WorldProgress("Reading the maps", null);
    }
}
