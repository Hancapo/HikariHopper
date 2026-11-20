using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HikariHopper.Rendering.World;
using RedFury.Rpf;
using RedFury.Textures;
using RedFury.World;

namespace HikariHopper.Core;

/// <summary>
/// A game's world, as HikariHopper shows it: its index (its maps, every placement in them and their levels of detail),
/// built in the background the first time (a minute or two, saying how far it has got) and cached on disk until the
/// game's archives change, then read back in a few seconds. Red Dead Redemption 2's is RedFury's (<see cref="Rdr2WorldIndex"/>),
/// GTA V's HikariHopper's own (<see cref="GtaWorldIndex"/>); both feed RedFury.World's LOD graph, which the one streamer
/// and renderer draw. One per game folder, shared by its tabs.
/// </summary>
internal sealed class GameWorld
{
    private static readonly ConcurrentDictionary<string, GameWorld> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly ArchiveKeys keys;
    private readonly object gate = new();
    private WorldProgress progress = new("", null);

    internal GameWorld(string root, ArchiveKeys keys) => (this.root, this.keys) = (root, keys);

    /// <summary>The world of the game at <paramref name="root"/>, shared by every caller.</summary>
    public static GameWorld ForGame(string root, ArchiveKeys keys) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), path => new GameWorld(path, keys));

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>
    /// Builds the index from the game's archives, by the game its keys are for; tests replace it. Given the game folder,
    /// its keys, a report of how far it has got, and a cancellation.
    /// </summary>
    internal static Func<string, ArchiveKeys, Action<WorldProgress>, CancellationToken, WorldIndex> Builder { get; set; } = Build;

    private static WorldIndex Build(string root, ArchiveKeys keys, Action<WorldProgress> report, CancellationToken cancellation) => keys switch
    {
        Rdr2Keys rdr2 => Rdr2WorldIndex.Build(root, rdr2.Crypto, report, cancellation),
        Rpf7Keys gta => GtaWorldIndex.Build(root, gta, report, cancellation),
        _ => throw new NotSupportedException("This game has no world HikariHopper shows yet."),
    };

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>Where the index has got, while it's being made; raised on the thread making it.</summary>
    public event Action<WorldProgress>? ProgressChanged;

    /// <summary>Where the index has got.</summary>
    public WorldProgress Progress
    {
        get
        {
            lock (gate)
                return progress;
        }
    }

    private Task<WorldIndex>? index;

    /// <summary>The index, once it's ready: null before, or when it couldn't be made.</summary>
    public WorldIndex? Index => index is { IsCompletedSuccessfully: true } done ? done.Result : null;

    /// <summary>Why the index couldn't be made; null while it's being made, or once it is.</summary>
    public string? Failure => index is { IsFaulted: true } failed ? failed.Exception!.GetBaseException().Message : null;

    /// <summary>Starts making (or reading) the index in the background; does nothing while that runs or once it's done.</summary>
    public Task<WorldIndex> Load()
    {
        lock (gate)
            return index ??= Task.Run(() => Builder(root, keys, Report, CancellationToken.None));
    }

    private void Report(WorldProgress value)
    {
        lock (gate)
            progress = value;
        ProgressChanged?.Invoke(value);
    }

    /// <summary>
    /// What a cache must be made with to be read: HikariHopper's own format (<paramref name="version"/>), and every RPF
    /// of the game as it is.
    /// </summary>
    internal static string Fingerprint(string root, string version)
    {
        var input = new StringBuilder(version);
        foreach (string path in Directory.EnumerateFiles(root, "*.rpf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = new FileInfo(path);
            input.Append('\n').Append(Path.GetRelativePath(root, path).ToUpperInvariant()).Append('|').Append(file.Length).Append('|')
                .Append(file.LastWriteTimeUtc.Ticks);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    /// <summary>
    /// Where a game's index is cached: beside the texture tables, which the settings' cache size and deletion cover; by
    /// the game folder, and a name of the game's.
    /// </summary>
    internal static string CachePath(string root, string name) =>
        Path.Combine(GameTextures.CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + $"-{name}.bin");
}

/// <summary>
/// A game's world, made: its maps' placements and their levels of detail, and how its assets are read, its grass
/// expanded and its files found. The streamer's workers read through it one thing at a time, under <see cref="Gate"/>,
/// unless it reads assets on several threads at once (<see cref="LoadsConcurrently"/>).
/// </summary>
internal abstract class WorldIndex(string root, int mapCount, WorldLodGraph lod)
{
    /// <summary>The game folder.</summary>
    public string Root { get; } = root;

    /// <summary>The maps the index read.</summary>
    public int MapCount { get; } = mapCount;

    /// <summary>Their placements and their levels of detail.</summary>
    public WorldLodGraph Lod { get; } = lod;

    /// <summary>Every placement in it: an entity, or one of a batch's instances.</summary>
    public long Placements { get; } = lod.Nodes.Sum(node => (long)Math.Max(1, node.Instances.Count));

    /// <summary>
    /// The timecycle's boxes over the world, which the camera's place in it is worked out from; none for a world whose
    /// timecycle doesn't change with place.
    /// </summary>
    public virtual Rendering.TimecycleZones? TimecycleZones => null;

    /// <summary>Held while the game's archives are read: by whichever streamer reads them, and for finding files.</summary>
    public object Gate { get; } = new();

    /// <summary>
    /// Whether <see cref="LoadAsset"/> can be called from several threads at once, taking <see cref="Gate"/> itself only
    /// while it reads the archives; otherwise it's called under the gate.
    /// </summary>
    public virtual bool LoadsConcurrently => false;

    /// <summary>The kinds of surface its assets are drawn with, whose pipelines are compiled as its world is shown.</summary>
    public abstract IReadOnlyList<Rendering.WorldFamily> Families { get; }

    /// <summary>
    /// Reads an asset the LOD graph names and makes it ready for the GPU. Under <see cref="Gate"/>, unless
    /// <see cref="LoadsConcurrently"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The game has no such asset.</exception>
    /// <exception cref="NotSupportedException">An asset this can't read.</exception>
    public abstract WorldAsset LoadAsset(WorldAssetKey key);

    /// <summary>What expands grass batches, for a streamer's worker; null for a game whose grass isn't drawn yet.</summary>
    public abstract Func<WorldLodNode, CancellationToken, WorldLodNode>? CreateGrass();

    /// <summary>Where an asset or a map (extension 73) is among the game's files; null when it isn't found. Under <see cref="Gate"/>.</summary>
    public abstract WorldFile? Locate(uint hash, byte extension);

    /// <summary>A name the game is known to use for a hash, or the hash itself as the explorer writes it.</summary>
    public abstract string NameOf(uint hash);

    /// <summary>The ambient of the interior room a placement is in; null for one outside, or a game without rooms' ambients.</summary>
    public virtual InteriorAmbient? AmbientOf(WorldLodNode node) => null;

    /// <summary>
    /// Where in its map a placement is, as the right-click menu says it (after the map's name), for one that isn't simply
    /// the map's entity of its number (a GTA V interior's); null for those that are.
    /// </summary>
    public virtual string? PlacementDetail(WorldLodNode node) => null;

    /// <summary>
    /// GTA V's sky behind its world, read from its files; null for a world whose sky is drawn otherwise (RDR2's,
    /// <see cref="LoadRdr2Sky"/>) or whose files don't have it. On any thread.
    /// </summary>
    public virtual Rendering.GtaSkyDome? LoadSky() => null;

    /// <summary>
    /// Red Dead Redemption 2's sky behind its world, as its files set it; null for a world without it (GTA V's, whose
    /// sky is <see cref="LoadSky"/>) or whose files don't have it. On any thread.
    /// </summary>
    public virtual Rendering.Rdr2Sky? LoadRdr2Sky() => null;

    /// <summary>
    /// The lens flare the game draws over its world when the sun's in view, read from its files; null for a world
    /// without one (RDR2's, for now) or whose files don't have it. On any thread.
    /// </summary>
    public virtual Rendering.GtaLensFlare? LoadLensFlare() => null;

    /// <summary>
    /// The cloud hat the game draws over that sky for the viewer's weather, read from its files; null for a world with
    /// none, or whose files don't have it. On any thread.
    /// </summary>
    public virtual Rendering.GtaCloudHats? LoadCloudHats() => null;

    /// <summary>
    /// What the game draws its lights' coronas with, read from its files; null for a world whose coronas aren't drawn
    /// (RDR2's, for now). On any thread.
    /// </summary>
    public virtual Rendering.GtaCoronaData? LoadCoronas() => null;

    /// <summary>Whether a placement is inside an interior.</summary>
    public virtual bool IsInInterior(WorldLodNode node) => false;
}

/// <summary>Red Dead Redemption 2's world: RedFury.World's catalog of its base map, and the archives its assets are read from.</summary>
/// <param name="Assets">The archives the world's assets and textures are found in, scanned.</param>
/// <param name="Reads">The archives held open for reading them.</param>
/// <param name="Crypto">The game's keys, which they're read with.</param>
internal sealed class Rdr2WorldIndex(string root, int mapCount, WorldLodGraph lod, RpfTextureCatalog assets, RpfReadSession reads, Rdr2Crypto crypto)
    : WorldIndex(root, mapCount, lod)
{
    public override IReadOnlyList<Rendering.WorldFamily> Families { get; } = [Rendering.WorldFamily.Rdr2, Rendering.WorldFamily.Rdr2Water];

    public RpfTextureCatalog Assets { get; } = assets;

    public RpfReadSession Reads { get; } = reads;

    public Rdr2Crypto Crypto { get; } = crypto;

    /// <summary>The game's files by their virtual paths, its update's over its base's; none for an index made without them.</summary>
    public WorldGameSource? Sources { get; init; }

    public override WorldAsset LoadAsset(WorldAssetKey key) => WorldAsset.LoadRdr2(key, Assets, Crypto, Reads);

    public override Func<WorldLodNode, CancellationToken, WorldLodNode>? CreateGrass()
    {
        var loader = new WorldGrassLoader(Assets, Crypto, Reads);
        return loader.Load;
    }

    public override WorldFile? Locate(uint hash, byte extension) => Rdr2WorldFiles.Locate(this, hash, extension);

    public override string NameOf(uint hash) => Rdr2WorldFiles.NameOf(hash);

    public override Rendering.Rdr2Sky? LoadRdr2Sky() =>
        Sources is { } sources
            ? Rendering.Rdr2Sky.Read(sources.Read, () => Rendering.Rdr2SkyDome.Read(sources, Crypto), () => Rendering.Rdr2Grading.Read(sources, Crypto))
            : null;

    /// <summary>HikariHopper's RDR2 world cache format; RedFury checks its own inside the file.</summary>
    private const string CacheVersion = "hh-rdr2-world-1";

    /// <summary>The index of the RDR2 game at <paramref name="root"/>: read from the cache, or made and cached.</summary>
    public static Rdr2WorldIndex Build(string root, Rdr2Crypto crypto, Action<WorldProgress> report, CancellationToken cancellation)
    {
        report(new WorldProgress("Reading the game's content", null));
        WorldGameSource sources = WorldGameSource.ForGame(root, crypto);
        var reads = new RpfReadSession(crypto);
        RpfTextureCatalog assets = sources.CreateBaseCatalog(readSession: reads);
        // The archives' scan is the index's and the streaming's alike: it overlaps reading the cache.
        Task scanning = Task.Run(() => assets.Scan(cancellation), cancellation);
        string fingerprint = GameWorld.Fingerprint(root, $"{CacheVersion}|{crypto.KeyCount}"), cache = GameWorld.CachePath(root, "world");
        WorldCatalog? catalog = null;
        if (File.Exists(cache))
        {
            report(new WorldProgress("Reading the world from the cache", null));
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
            report(new WorldProgress("Scanning the game's archives", null));
            scanning.GetAwaiter().GetResult();
            catalog = WorldCatalog.LoadMetadata(assets.Assets(74), assets.Assets(73), [.. sources.Diagnostics, .. assets.Diagnostics], crypto,
                message => report(WorldProgress.From(message)), cancellation);
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
        report(new WorldProgress("Ready", 1));
        return new Rdr2WorldIndex(root, catalog.MapCount, catalog.Lod, assets, reads, crypto) { Sources = sources };
    }
}

/// <summary>Where a world's index has got: its stage, as the loading screen says it, and how far, 0 to 1, when that's known.</summary>
internal sealed partial record WorldProgress(string Stage, double? Fraction)
{
    [GeneratedRegex(@"^Discovering maps (\d+)/(\d+)")]
    private static partial Regex Discovering();

    /// <summary>RedFury's report while it reads the maps ("Discovering maps 640/7303 · …"), as a stage and how far.</summary>
    public static WorldProgress From(string message)
    {
        if (Discovering().Match(message) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int done)
            && int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int total) && total > 0)
            return new WorldProgress("Reading the maps", Math.Clamp(done / (double)total, 0, 1));
        return new WorldProgress("Reading the maps", null);
    }
}
