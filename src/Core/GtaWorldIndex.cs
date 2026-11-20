using System.Numerics;
using CommonFury.Numerics;
using FiveFury;
using FiveFury.Assets;
using FiveFury.Authoring;
using CommonFury.Hashing;
using FiveFury.Numerics;
using FiveFury.Ymap;
using FiveFury.Textures;
using FiveFury.Ydd;
using FiveFury.Ydr;
using FiveFury.Yft;
using FiveFury.Ytyp;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Core;

/// <summary>
/// GTA V's world, as HikariHopper shows it: the base game's maps (no DLC packs: FiveFury's base content, the update
/// included), every entity placed in them, its archetype's model and its levels of detail, as RedFury.World's LOD graph
/// takes them, built after RedFury's WorldCatalog. GTA V is RAGE too: its entity flags (the LOD parent in the parent
/// map, the passes an entity is drawn in), its archetypes' shadow flags and its time archetypes' hours are RDR2's.
/// Interiors, grass and lights come in later; the installation stays open for reading the world's assets.
/// </summary>
internal sealed class GtaWorldIndex : WorldIndex
{
    private GtaWorldIndex(string root, int mapCount, WorldLodGraph lod, GameInstallation game, Dictionary<WorldEntityId, InteriorAmbient> ambients,
        Dictionary<WorldEntityId, (int Set, int Entity)> interiorEntities, GtaWorldWater water, GameVisualSettings? visuals)
        : base(root, mapCount, lod) => (Game, this.ambients, this.interiorEntities, this.water, this.visuals) = (game, ambients, interiorEntities, water, visuals);

    // The game's visualsettings.dat, read with the index.
    private readonly GameVisualSettings? visuals;

    // Where each interior's placement is in its interior: an entity of it, or of one of its entity sets (Set from 0).
    private readonly Dictionary<WorldEntityId, (int Set, int Entity)> interiorEntities;

    public override string? PlacementDetail(WorldLodNode node)
    {
        if (node.Id.Map == GtaWorldWater.Map)
            return $"water.xml quad {node.Id.Index}";
        if (!interiorEntities.TryGetValue(node.Id, out var inside))
            return null;
        return inside.Set < 0 ? $"interior entity {inside.Entity}" : $"interior entity set {inside.Set} · entity {inside.Entity}";
    }

    // The open water (water.xml's quads) and its tuning, and the water textures the game binds itself (graphics.ytd's).
    private readonly GtaWorldWater water;
    private GtaWater? waterTextures;

    // The game's own water textures, with watertune.xml's values: the ocean's material, whose textures a water
    // shader's drawables share.
    private GtaWater WaterTextures()
    {
        lock (cacheGate)
            if (waterTextures is not null)
                return waterTextures;
        TextureDictionary? graphics = Dictionary(new Hash32("graphics").Value);
        FiveFury.Textures.Texture? Named(string name) => graphics?.Textures.FirstOrDefault(texture => string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase));
        GtaWater made = water.Tuning with
        {
            Fog = Named("waterfog"),
            WorldFog = true,
            Bump = Named("waterbump"),
            WaveBump = Named("waterbump2"),
            Noise = Named("waternoise"),
            Foam = Named("waterfoam"),
        };
        lock (cacheGate)
            return waterTextures ??= made;
    }

    // The interior rooms' ambients of the placements inside them.
    private readonly Dictionary<WorldEntityId, InteriorAmbient> ambients;

    public override InteriorAmbient? AmbientOf(WorldLodNode node) => ambients.TryGetValue(node.Id, out InteriorAmbient ambient) ? ambient : null;

    /// <summary>The base game, mounted.</summary>
    public GameInstallation Game { get; }

    // The base game's dictionaries' parents (gtxd.meta and the maps' manifests), made the first time an asset is read,
    // and each dictionary's chain as found; and the dictionaries read lately, by name hash, shared by the assets that
    // read them, so a parent's textures go up once.
    private GameFileCache? graph;
    private readonly Dictionary<uint, uint[]> chains = [];
    private readonly Dictionary<uint, LinkedListNode<(uint Hash, TextureDictionary? Dictionary, long Bytes)>> dictionaries = [];

    // Held around the caches here (not the archives, which Gate covers), as assets are read on several threads; and
    // the dictionaries being read, so a dictionary two assets want at once is read once and shared.
    private readonly Lock cacheGate = new();
    private readonly Dictionary<uint, Lazy<(TextureDictionary? Dictionary, long Bytes)>> reading = [];
    private readonly LinkedList<(uint Hash, TextureDictionary? Dictionary, long Bytes)> recent = new();
    private long dictionaryBytes;

    /// <summary>
    /// How much of the dictionaries read lately is kept for the next asset that reads them. Their textures on the GPU
    /// are the viewport's, by what they are (<see cref="WorldTextureId"/>), so a dictionary read again isn't uploaded
    /// again: this only saves reading it.
    /// </summary>
    internal const long DictionaryBudget = 192L * 1024 * 1024;

    // Each dictionary's textures, named as they're read.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FiveFury.Textures.Texture, object> textureIds = new();

    private WorldTextureId? TextureId(FiveFury.Textures.Texture texture) => textureIds.TryGetValue(texture, out object? id) ? (WorldTextureId)id : null;

    /// <summary>
    /// Reads an archetype's drawable, fragment (intact) or drawable dictionary member, its textures from its own
    /// dictionary, then the one its archetype names and that one's gtxd parents, then mapdetail.ytd, as the game
    /// binds them. On any thread: the archives are read under <see cref="WorldIndex.Gate"/>, the rest outside it.
    /// </summary>
    public override bool LoadsConcurrently => true;

    public override IReadOnlyList<Rendering.WorldFamily> Families { get; } = [Rendering.WorldFamily.Gta, Rendering.WorldFamily.GtaTerrain, Rendering.WorldFamily.GtaWater];

    public override WorldAsset LoadAsset(WorldAssetKey key)
    {
        // A quad of the open water.
        if (key.Extension == GtaWorldWater.Extension)
        {
            if (key.Member >= water.Quads.Count)
                throw new FileNotFoundException($"Asset {key} not found.");
            return WorldAsset.FromGta(key, water.Surface((int)key.Member, WaterTextures()), [], TextureId);
        }
        GameFileType type = TypeOf(key.Extension) is { } known and not GameFileType.Ymap ? known
            : throw new NotSupportedException($"Unsupported world asset {key}.");
        byte[] data;
        string name;
        lock (Gate)
        {
            GameFile file = Game.FileSystem.Resolve(new Hash32(key.Hash), type).Value ?? throw new FileNotFoundException($"Asset {key} not found.");
            data = file.ReadStandalone();
            name = Path.GetFileNameWithoutExtension(file.VirtualPath);
        }
        // A fragment's lights are its own (a lamp post's), not its drawables'.
        Yft? fragment = type == GameFileType.Yft ? Yft.Read(data) : null;
        IReadOnlyList<SceneModel> models = type switch
        {
            GameFileType.Ydr => [new SceneModel(name, Ydr.Read(data).Drawable, Matrix4x4.Identity)],
            GameFileType.Yft => [.. FragmentModels.Of(fragment!, name).Where(model => model.Variant != ModelVariant.Damaged)],
            _ => [.. Ydd.Read(data).Drawables.Where(entry => key.Member == 0 || entry.Name.Value == key.Member)
                .Select(entry => new SceneModel(ModelContent.EntryName(entry.Name.Value, entry.Drawable.Name), entry.Drawable, Matrix4x4.Identity))],
        };
        if (models.Count == 0)
            throw new FileNotFoundException($"Asset {key} has no such drawable.");
        DrawableScene scene = DrawableScene.Build(models, new TextureLookup([.. Chain(key.TextureDictionary).Select(Dictionary).OfType<TextureDictionary>()], []),
            fragmentLights: fragment?.Lights);
        // A water shader's drawables: the game's own water textures, and its fog map where the material has no fog texture.
        foreach (SurfaceMaterial material in scene.Materials)
            if (material.Water is { } own)
            {
                GtaWater global = WaterTextures();
                material.Water = own with
                {
                    Fog = own.Fog ?? global.Fog,
                    WorldFog = own.Fog is null,
                    Bump = global.Bump,
                    WaveBump = global.WaveBump,
                    Noise = global.Noise,
                    Foam = global.Foam,
                    FogMin = global.FogMin,
                    FogMax = global.FogMax,
                    FoamScale = global.FoamScale,
                };
            }
        return WorldAsset.FromGta(key, scene, models.Select(model => model.Drawable), TextureId);
    }

    // Where the game looks for the textures of a model whose archetype names a dictionary (as FiveFury's ResolveTexture):
    // that dictionary, its parents nearest first, then mapdetail.ytd, which the game keeps loaded for every drawable's
    // detail maps.
    private uint[] Chain(uint dictionary)
    {
        lock (cacheGate)
            if (chains.TryGetValue(dictionary, out uint[]? known))
                return known;
        uint[] chain;
        lock (Gate)
            chain = ReadChain(dictionary);
        lock (cacheGate)
            return chains[dictionary] = chain;
    }

    // Under Gate: the texture graph reads the game's gtxd files the first time.
    private uint[] ReadChain(uint dictionary)
    {
        graph ??= new GameFileCache(Game.FileSystem);
        var order = new List<uint>();
        if (dictionary != 0)
            order.Add(dictionary);
        for (uint current = dictionary; current != 0;)
        {
            var parent = graph.TextureGraph.GetParent(new Hash32(current));
            // Two packs naming different parents: the first, as nothing ranks them.
            uint next = parent is { Status: ResolutionStatus.Resolved, Value: { } edge } ? edge.ParentHash.Value
                : parent.Status == ResolutionStatus.Ambiguous && graph.TextureGraph.EdgesFrom(new Hash32(current)) is [var first, ..] ? first.ParentHash.Value : 0;
            if (next == 0 || order.Contains(next))
                break;
            order.Add(next);
            current = next;
        }
        if (!order.Contains(MapDetail))
            order.Add(MapDetail);
        return [.. order];
    }

    private static readonly uint MapDetail = new Hash32("mapdetail").Value;

    // A dictionary by name hash, as the game streams it; null when the game has none or it can't be read. One being read
    // for another asset is waited for, so each dictionary's textures are one set, uploaded once.
    private TextureDictionary? Dictionary(uint hash)
    {
        Lazy<(TextureDictionary? Dictionary, long Bytes)>? read;
        bool mine = false;
        lock (cacheGate)
        {
            if (dictionaries.TryGetValue(hash, out var known))
            {
                recent.Remove(known);
                recent.AddFirst(known);
                return known.Value.Dictionary;
            }
            if (!reading.TryGetValue(hash, out read))
            {
                read = new(() => ReadDictionary(hash), LazyThreadSafetyMode.ExecutionAndPublication);
                reading.Add(hash, read);
                mine = true;
            }
        }
        if (!mine)
            return read.Value.Dictionary;
        (TextureDictionary? dictionary, long bytes) = (null, 0);
        try
        {
            (dictionary, bytes) = read.Value;
        }
        catch
        {
            lock (cacheGate)
                reading.Remove(hash);
            throw;
        }
        // Kept as it stops being read, in one step, so no other asset reads it again in between.
        lock (cacheGate)
        {
            reading.Remove(hash);
            dictionaries[hash] = recent.AddFirst((hash, dictionary, bytes));
            dictionaryBytes += bytes;
            while (dictionaryBytes > DictionaryBudget && recent.Last is { } oldest && oldest.Value.Hash != hash)
            {
                recent.RemoveLast();
                dictionaries.Remove(oldest.Value.Hash);
                dictionaryBytes -= oldest.Value.Bytes;
            }
        }
        return dictionary;
    }

    // Read under Gate, parsed outside it.
    private (TextureDictionary? Dictionary, long Bytes) ReadDictionary(uint hash)
    {
        try
        {
            byte[]? data = null;
            lock (Gate)
                if (Game.FileSystem.Resolve(new Hash32(hash), GameFileType.Ytd).Value is { } file)
                    data = file.ReadStandalone();
            if (data is null)
                return (null, 0);
            TextureDictionary dictionary = FiveFury.Ytd.Ytd.Read(data).Dictionary;
            foreach (FiveFury.Textures.Texture texture in dictionary.Textures)
                textureIds.AddOrUpdate(texture, new WorldTextureId(hash, new Hash32((texture.Name ?? "").ToLowerInvariant()).Value));
            return (dictionary, dictionary.Textures.Sum(texture => (long)texture.Data.Length));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException
            or OverflowException)
        {
            return (null, 0);
        }
    }

    /// <summary>
    /// The sky as the game loads it: its dome, the "skydome" drawable of models/skydome.ydd, and the textures
    /// of textures/skydome.ytd.
    /// </summary>
    public override GtaSkyDome? LoadSky()
    {
        var name = new Hash32("skydome");
        try
        {
            byte[]? model = null, textures = null;
            lock (Gate)
            {
                model = Game.FileSystem.Resolve(name, GameFileType.Ydd).Value?.ReadStandalone();
                textures = Game.FileSystem.Resolve(name, GameFileType.Ytd).Value?.ReadStandalone();
            }
            if (model is null)
                return null;
            var drawables = Ydd.Read(model).Drawables;
            FiveFury.Drawables.Drawable? dome = (drawables.FirstOrDefault(entry => entry.Name.Value == name.Value) ?? drawables.FirstOrDefault())?.Drawable;
            if (dome is null)
                return null;
            return GtaSkyDome.From(dome, textures is null ? null : FiveFury.Ytd.Ytd.Read(textures).Dictionary, GtaSkyVisuals.From(visuals));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException
            or OverflowException)
        {
            return null;
        }
    }

    public override Func<WorldLodNode, CancellationToken, WorldLodNode>? CreateGrass() => ExpandGrass;

    // The maps grass was read from lately, by name hash: neighbouring batches are in the same map.
    private readonly LinkedList<(uint Map, Ymap Ymap)> grassMaps = new();

    /// <summary>
    /// A grass batch's plants, read from its map and placed as GTA V's grass_batch vertex shader places them: at their
    /// quantised place in the batch's bounds, scaled across the batch's scale range by their own, turned by one of
    /// eight headings by their number (the game's eight instance matrices, here 45 degrees apart), and tilted towards
    /// the ground's normal by the batch's orientToTerrain; with their colour and occlusion, fading out from 85% of the
    /// batch's reach. Under <see cref="WorldIndex.Gate"/>.
    /// </summary>
    private WorldLodNode ExpandGrass(WorldLodNode node, CancellationToken cancellation)
    {
        WorldGrassBatch descriptor = node.Grass ?? throw new ArgumentException("Node is not a grass batch.", nameof(node));
        Ymap ymap = GrassMap(node.Id.Map);
        if ((uint)descriptor.BatchIndex >= ymap.InstancedData.Grass.Count)
            throw new InvalidDataException("Grass batch index is invalid.");
        GrassInstanceBatch batch = ymap.InstancedData.Grass[descriptor.BatchIndex];
        if (batch.Instances.Count != descriptor.InstanceCount)
            throw new InvalidDataException("Grass batch changed since indexing.");
        float fadeStart = float.IsFinite(batch.LodFadeStartDistance) && batch.LodFadeStartDistance > 0
            ? Math.Min(batch.LodFadeStartDistance, node.Distance * .85f) : node.Distance * .7f;
        var instances = new List<WorldInstance>(batch.Instances.Count);
        for (int i = 0; i < batch.Instances.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            GrassInstance plant = batch.Instances[i];
            float scale = batch.ScaleRange.X + (batch.ScaleRange.Y - batch.ScaleRange.X) * (plant.Scale / 255f);
            if (!(scale > .00001f) || !float.IsFinite(scale))
                continue;
            Vector3 normal = plant.Normal;
            var tilt = Quaternion.Normalize(new Quaternion(-normal.Y, normal.X, 0, 1 + normal.Z));
            tilt = Quaternion.Slerp(Quaternion.Identity, tilt, Math.Clamp(batch.OrientToTerrain, 0, 1));
            Matrix4x4 t = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ((i & 7) * MathF.PI / 4)
                * Matrix4x4.CreateFromQuaternion(tilt) * Matrix4x4.CreateTranslation(plant.Position(batch.Bounds));
            if (!VectorMath.IsFinite(t))
                continue;
            var rows = new Matrix3x4(new(t.M11, t.M21, t.M31, t.M41), new(t.M12, t.M22, t.M32, t.M42), new(t.M13, t.M23, t.M33, t.M43));
            var colour = new Vector3(plant.Color.R, plant.Color.G, plant.Color.B) / 255f;
            instances.Add(new WorldInstance(rows, Vector3.Transform(descriptor.ModelCenter, t), descriptor.ModelRadius * scale)
            {
                Grass = new WorldGrassAttributes(colour, plant.AmbientOcclusion / 255f, fadeStart, node.Distance),
            });
        }
        return node with { Instances = instances.AsReadOnly() };
    }

    private Ymap GrassMap(uint map)
    {
        for (LinkedListNode<(uint Map, Ymap Ymap)>? at = grassMaps.First; at is not null; at = at.Next)
            if (at.Value.Map == map)
            {
                grassMaps.Remove(at);
                grassMaps.AddFirst(at);
                return at.Value.Ymap;
            }
        GameFile file = Game.FileSystem.Resolve(new Hash32(map), GameFileType.Ymap).Value ?? throw new FileNotFoundException("Grass map is unavailable.");
        Ymap ymap = Ymap.Read(file.ReadStandalone());
        grassMaps.AddFirst((map, ymap));
        if (grassMaps.Count > 8)
            grassMaps.RemoveLast();
        return ymap;
    }

    public override WorldFile? Locate(uint hash, byte extension)
    {
        GameFileType? type = TypeOf(extension);
        if (type is null)
            return null;
        lock (Gate)
            return Game.FileSystem.Resolve(new Hash32(hash), type.Value).Value is { } file && GameTextures.GamePath(Root, file.SourcePath) is { } path
                ? new WorldFile(path, file.Size) : null;
    }

    public override string NameOf(uint hash)
    {
        lock (Gate)
            foreach (GameFileType type in new[] { GameFileType.Ydr, GameFileType.Yft, GameFileType.Ydd, GameFileType.Ymap, GameFileType.Ytyp })
                if (Game.FileSystem.Resolve(new Hash32(hash), type).Value is { } file)
                    return Path.GetFileNameWithoutExtension(file.VirtualPath);
        return $"0x{hash:X8}";
    }

    // An asset's file type, by the extension the LOD graph gives it (RDR2's numbering: 2 drawable, 3 fragment, 4
    // drawable dictionary, 73 map).
    private static GameFileType? TypeOf(byte extension) => extension switch
    {
        2 => GameFileType.Ydr,
        3 => GameFileType.Yft,
        4 => GameFileType.Ydd,
        73 => GameFileType.Ymap,
        _ => null,
    };

    /// <summary>HikariHopper's GTA V world cache format: change it whenever what's cached changes.</summary>
    private const string CacheVersion = "hh-gta-world-6";

    /// <summary>The index of the GTA V game at <paramref name="root"/>: read from the cache, or made and cached.</summary>
    public static GtaWorldIndex Build(string root, Rpf7Keys keys, Action<WorldProgress> report, CancellationToken cancellation)
    {
        report(new WorldProgress("Reading the game's content", null));
        GameTarget target = GameSettings.GameEditionForPath(root) == GameSettings.EnhancedEdition ? GameTarget.Enhanced : GameTarget.Legacy;
        GameInstallation game = GameInstallation.Open(root, target, keys.Crypto, new GameInstallationOptions
        {
            DlcLevel = DlcLevel.Base,
            IndexCache = new GameIndexCache(GameTextures.IndexFolder),
        });
        try
        {
            string fingerprint = GameWorld.Fingerprint(root, $"{CacheVersion}|{target}"), cache = GameWorld.CachePath(root, "gta-world");
            (int Maps, WorldLodNode[] Nodes, Dictionary<WorldEntityId, InteriorAmbient> Ambients, Dictionary<WorldEntityId, (int Set, int Entity)> Interiors)? catalog = null;
            if (File.Exists(cache))
            {
                report(new WorldProgress("Reading the world from the cache", null));
                catalog = GtaWorldCache.Read(cache, fingerprint);
            }
            WorldLodGraph? lod = null;
            if (catalog is not null)
            {
                try
                {
                    lod = new WorldLodGraph(catalog.Value.Nodes);
                }
                catch (ArgumentException)
                {
                    catalog = null;
                }
            }
            if (catalog is null)
            {
                GameTimecycle? timecycle = GameTimecycle.ForGame(new LoadedGame(root, target, keys.Crypto));
                catalog = GtaWorldCatalog.Load(game.FileSystem, timecycle, report, cancellation);
                // Made into a graph before it's cached, so only a good index is.
                lod = new WorldLodGraph(catalog.Value.Nodes);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                    GtaWorldCache.Write(cache, fingerprint, catalog.Value.Maps, catalog.Value.Nodes, catalog.Value.Ambients, catalog.Value.Interiors);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
            // The open water, read from the game each time (its placements are none of the cache's).
            GtaWorldWater water = GtaWorldWater.Read(new LoadedGame(root, target, keys.Crypto));
            GameVisualSettings? visuals = GameVisualSettings.Read(new LoadedGame(root, target, keys.Crypto));
            lod = new WorldLodGraph([.. catalog.Value.Nodes, .. water.Nodes()]);
            report(new WorldProgress("Ready", 1));
            return new GtaWorldIndex(root, catalog.Value.Maps, lod, game, catalog.Value.Ambients, catalog.Value.Interiors, water, visuals);
        }
        catch
        {
            game.Dispose();
            throw;
        }
    }
}

/// <summary>
/// GTA V's maps read into RedFury.World's nodes (after RedFury's WorldCatalog): each archetype's model, bounds, LOD
/// distance, flags and hours, from every archetype file; then each map's entities placed as the game places them, their
/// LOD parents in this map or, flagged, in its parent map. Interiors (MLO instances) and grass wait for their own steps.
/// </summary>
internal static class GtaWorldCatalog
{
    private sealed record Archetype(WorldAssetKey Asset, Vector3 Center, float Radius, float Distance, ArchetypeFlags Flags, ArchetypeAssetType Type,
        bool Animated, uint TimeFlags);

    public static (int Maps, WorldLodNode[] Nodes, Dictionary<WorldEntityId, InteriorAmbient> Ambients, Dictionary<WorldEntityId, (int Set, int Entity)> Interiors)
        Load(GameFileSystem files, GameTimecycle? timecycle, Action<WorldProgress> report, CancellationToken cancellation)
    {
        var ambients = new Dictionary<WorldEntityId, InteriorAmbient>();
        var interiorEntities = new Dictionary<WorldEntityId, (int Set, int Entity)>();
        // How many more children each LOD model an interior is placed under has than it expects.
        var adopted = new Dictionary<WorldEntityId, int>();
        // Each name's winning file, as the game streams it.
        List<GameFile> Winners(GameFileType type)
        {
            var winners = new List<GameFile>();
            foreach (Hash32 name in files.EnumerateFiles(type).Select(file => file.NameHash).Distinct())
                if (files.Resolve(name, type).Value is { } winner)
                    winners.Add(winner);
            return winners;
        }
        List<GameFile> types = Winners(GameFileType.Ytyp), maps = Winners(GameFileType.Ymap);
        var archetypes = new Dictionary<uint, Archetype>();
        var interiors = new Dictionary<uint, MloArchetypeDef>();
        int done = 0, total = types.Count + maps.Count;
        void Progress(string stage)
        {
            if (++done % 64 == 0 || done == total)
                report(new WorldProgress(stage, Math.Clamp(done / (double)total, 0, 1)));
        }
        foreach (GameFile file in types)
        {
            cancellation.ThrowIfCancellationRequested();
            Progress("Reading the archetypes");
            Ytyp ytyp;
            try
            {
                ytyp = Ytyp.Read(file.ReadStandalone());
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException)
            {
                continue;
            }
            foreach (YtypArchetype value in ytyp.Archetypes)
            {
                // Interiors' archetypes (MLOs) are their entities, placed where an instance of one is; those without a
                // model have nothing to draw.
                if (value is MloArchetypeDef interior)
                {
                    interiors[interior.Name.Value] = interior;
                    continue;
                }
                if (value is not BaseArchetypeDef archetype || archetype.AssetType is ArchetypeAssetType.Assetless or ArchetypeAssetType.Uninitialized)
                    continue;
                uint name = archetype.Name.Value, asset = archetype.AssetName.Value != 0 ? archetype.AssetName.Value : name;
                WorldAssetKey key = archetype.DrawableDictionary.Value != 0 && archetype.AssetType != ArchetypeAssetType.Fragment
                    ? new WorldAssetKey(archetype.DrawableDictionary.Value, 4, asset, archetype.TextureDictionary.Value)
                    : archetype.AssetType == ArchetypeAssetType.Fragment
                        ? new WorldAssetKey(asset, 3, 0, archetype.TextureDictionary.Value)
                        : new WorldAssetKey(asset, 2, 0, archetype.TextureDictionary.Value);
                uint hours = archetype is TimeArchetypeDef timed ? (uint)timed.TimeFlags : RedFury.Ytyp.TimeArchetype.AllHours;
                archetypes[name] = new Archetype(key, archetype.BoundingSphere.Center, archetype.BoundingSphere.Radius, archetype.LodDistance,
                    archetype.Flags, archetype.AssetType, archetype.ClipDictionary.Value != 0 && (archetype.Flags & ArchetypeFlags.HasAnim) != 0, hours);
            }
        }
        var nodes = new List<WorldLodNode>();
        int read = 0;
        foreach (GameFile file in maps)
        {
            cancellation.ThrowIfCancellationRequested();
            Progress("Reading the maps");
            Ymap ymap;
            try
            {
                ymap = Ymap.Read(file.ReadStandalone());
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException)
            {
                continue;
            }
            // By its file's name, which finds it again: two files can carry the same name inside.
            uint map = file.NameHash.Value, parentMap = ymap.Parent.Value;
            read++;
            var placedInteriors = new List<(MloInstanceDef Instance, WorldEntityId? Parent)>();
            // Each entity's place in its map, which a local LOD parent is found by.
            var indexes = new Dictionary<YmapEntity, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < ymap.Entities.Count; i++)
                indexes[ymap.Entities[i]] = i;
            for (int i = 0; i < ymap.Entities.Count; i++)
            {
                if (ymap.Entities[i] is MloInstanceDef instance)
                {
                    placedInteriors.Add((instance, LodParent(instance, map, parentMap, indexes)));
                    continue;
                }
                if (ymap.Entities[i] is not EntityDef entity || !archetypes.TryGetValue(entity.ArchetypeName.Value, out Archetype? type))
                    continue;
                if (Place(entity, type, Matrix4x4.Identity) is not var (transform, center, radius, distance))
                    continue;
                // The LOD parent: an entity of this map, or (flagged) of its parent map, by its place there.
                uint flags = (uint)entity.Flags;
                WorldEntityId? parent = LodParent(entity, map, parentMap, indexes);
                float childDistance = float.IsFinite(entity.ChildLodDistance) ? entity.ChildLodDistance : 0;
                nodes.Add(new WorldLodNode(new WorldEntityId(map, i), parent, type.Asset, transform, center, radius, distance,
                    childDistance, checked((int)entity.NumberOfChildren))
                {
                    EntityFlags = flags,
                    TintValue = entity.TintValue,
                    ArchetypeFlags = (ulong)type.Flags,
                    TimeFlags = type.TimeFlags,
                });
            }
            int next = AddInstances(ymap, map, archetypes, nodes);
            foreach (var (instance, parent) in placedInteriors)
                next = AddInterior(instance, parent, map, next, archetypes, interiors, nodes, timecycle, ambients, adopted, interiorEntities);
        }
        // The LOD models interiors were placed under expect the interior placement as one child: they have its
        // contents in its place.
        if (adopted.Count != 0)
            for (int n = 0; n < nodes.Count; n++)
                if (adopted.TryGetValue(nodes[n].Id, out int extra))
                    nodes[n] = nodes[n] with { ExpectedChildren = Math.Max(0, nodes[n].ExpectedChildren + extra) };
        if (nodes.Count == 0)
            throw new InvalidDataException("No readable world placements were found in the game's maps.");
        return (read, [.. nodes], ambients, interiorEntities);
    }

    /// <summary>The longest a grass batch is drawn from, whatever it says (RedFury's near-field tier).</summary>
    internal const float GrassReach = 180;

    // A map's instanced data, after its entities, numbered on from them (RedFury's WorldCatalog): each grass batch a node
    // whose plants are read when it's wanted, each batch of instanced props a node with its placements.
    private static int AddInstances(Ymap ymap, uint map, Dictionary<uint, Archetype> archetypes, List<WorldLodNode> nodes)
    {
        int index = ymap.Entities.Count;
        for (int g = 0; g < ymap.InstancedData.Grass.Count; g++, index++)
        {
            GrassInstanceBatch batch = ymap.InstancedData.Grass[g];
            if (batch.Instances.Count == 0 || !archetypes.TryGetValue(batch.ArchetypeName.Value, out Archetype? type))
                continue;
            float distance = Math.Min(batch.LodDistance > 0 ? batch.LodDistance : type.Distance, GrassReach);
            Vector3 range = batch.ScaleRange;
            float maxScale = Math.Max(Math.Abs(range.X), Math.Abs(range.Y)) + Math.Abs(range.Y * range.Z);
            Vector3 min = batch.Bounds.Minimum, max = batch.Bounds.Maximum;
            if (!float.IsFinite(maxScale) || !float.IsFinite(distance) || distance <= 0 || !(type.Radius > 0) || !VectorMath.IsFinite(min) || !VectorMath.IsFinite(max))
                continue;
            float padding = (type.Center.Length() + type.Radius) * maxScale;
            nodes.Add(new WorldLodNode(new WorldEntityId(map, index), null, type.Asset, Matrix4x4.Identity, (min + max) * .5f,
                (max - min).Length() * .5f + padding, distance, 0, 0)
            {
                Grass = new WorldGrassBatch(g, batch.Instances.Count, type.Center, type.Radius),
                ArchetypeFlags = (ulong)type.Flags,
                TimeFlags = type.TimeFlags,
            });
        }
        foreach (PropInstanceBatch batch in ymap.InstancedData.Props)
        {
            int at = index++;
            if (batch.Instances.Count == 0 || !archetypes.TryGetValue(batch.ArchetypeName.Value, out Archetype? type))
                continue;
            var placements = new List<WorldInstance>(batch.Instances.Count);
            foreach (PropInstance instance in batch.Instances)
            {
                Matrix4x4 t = instance.Transform;
                if (!VectorMath.IsFinite(t))
                    continue;
                placements.Add(new WorldInstance(new Matrix3x4(new(t.M11, t.M21, t.M31, t.M41), new(t.M12, t.M22, t.M32, t.M42), new(t.M13, t.M23, t.M33, t.M43)),
                    Vector3.Transform(type.Center, t), type.Radius * TransformMath.RadiusScale(t)));
            }
            if (placements.Count == 0)
                continue;
            Vector3 min = batch.Bounds.Minimum, max = batch.Bounds.Maximum;
            float distance = Math.Min(batch.LodDistance > 0 ? batch.LodDistance : type.Distance, 30000);
            nodes.Add(new WorldLodNode(new WorldEntityId(map, at), null, type.Asset, Matrix4x4.Identity, (min + max) * .5f,
                (max - min).Length() * .5f + type.Radius, distance, 0, 0)
            {
                Instances = placements.AsReadOnly(),
                ArchetypeFlags = (ulong)type.Flags,
                TimeFlags = type.TimeFlags,
            });
        }
        return index;
    }

    /// <summary>
    /// An interior placed by a map (an MLO instance): the entities of its archetype, and those of the entity sets the
    /// instance turns on by default, each placed in the interior, then where the instance puts the interior (its rotation
    /// as stored, unscaled). Numbered on from <paramref name="next"/>, in the instance's map. They take the instance's
    /// place in the LOD tree: children of its LOD parent (<paramref name="parent"/>, the interior's LOD model), which
    /// gives way to all of them together as it would to the instance (<paramref name="adopted"/> counts the extra
    /// children); none reaches further than the instance does, as the game shows an interior's contents only within
    /// that. Each one in a room (not the limbo, room nought, which is outside) takes that room's ambient
    /// (<paramref name="ambients"/>), by its timecycle modifiers. Interiors only cutscenes show are left out. Returns the
    /// next number.
    /// </summary>
    private static int AddInterior(MloInstanceDef instance, WorldEntityId? parent, uint map, int next, Dictionary<uint, Archetype> archetypes,
        Dictionary<uint, MloArchetypeDef> interiors, List<WorldLodNode> nodes, GameTimecycle? timecycle, Dictionary<WorldEntityId, InteriorAmbient> ambients,
        Dictionary<WorldEntityId, int> adopted, Dictionary<WorldEntityId, (int Set, int Entity)> interiorEntities)
    {
        if (!interiors.TryGetValue(instance.ArchetypeName.Value, out MloArchetypeDef? interior) || (interior.MloFlags & MloInteriorFlags.CutsceneOnly) != 0)
            return next;
        Matrix4x4 placement;
        try
        {
            placement = TransformMath.CreateTrs(instance.Position, QuaternionMath.NormalizeChecked(instance.Rotation), Vector3.One);
        }
        catch (ArgumentException)
        {
            return next;
        }
        // Which room each entity is in: the rooms name theirs, the portals theirs, and an entity set's say where each of
        // theirs is.
        var rooms = new Dictionary<YmapEntity, MloRoomDef>(ReferenceEqualityComparer.Instance);
        foreach (MloRoomDef room in interior.Rooms)
            foreach (LocalReference<YmapEntity> attached in room.AttachedObjects)
                if (attached.Target is { } held)
                    rooms[held] = room;
        // A portal's objects (its doors) are in the room on its inner side.
        MloRoomDef? limbo = interior.Rooms.Count > 0 ? interior.Rooms[0] : null;
        foreach (MloPortalDef portal in interior.Portals)
            if ((portal.RoomTo.Target is { } to && to != limbo ? to : portal.RoomFrom.Target) is { } side)
                foreach (LocalReference<YmapEntity> attached in portal.AttachedObjects)
                    if (attached.Target is { } held)
                        rooms.TryAdd(held, side);
        var sets = new HashSet<uint>(instance.DefaultEntitySets.Select(name => name.Value));
        // Each with where it is in the interior: its place in the interior's entities, or in an entity set's.
        var contents = interior.Entities.Select((entity, at) => (Entity: entity, Room: rooms.GetValueOrDefault(entity), Set: -1, At: at))
            .Concat(interior.EntitySets.Select((set, number) => (set, number)).Where(pair => sets.Contains(pair.set.Name.Value)).SelectMany(pair => pair.set.Entities
                .Select((item, at) => (item.Entity, Room: item.Location.Room ?? rooms.GetValueOrDefault(item.Entity), Set: pair.number, At: at))));
        var roomAmbients = new Dictionary<MloRoomDef, InteriorAmbient>(ReferenceEqualityComparer.Instance);
        float reach = instance.LodDistance > 0 ? instance.LodDistance : interior.LodDistance;
        int placedCount = 0;
        foreach (var (value, room, set, at) in contents)
        {
            int index = next++;
            interiorEntities[new WorldEntityId(map, index)] = (set, at);
            if (value is not EntityDef entity || value is MloInstanceDef || !archetypes.TryGetValue(entity.ArchetypeName.Value, out Archetype? type)
                || Place(entity, type, placement) is not { } placed)
                continue;
            if (timecycle is not null && room is not null && interior.Rooms.IndexOf(room) > 0)
            {
                if (!roomAmbients.TryGetValue(room, out InteriorAmbient ambient))
                    roomAmbients[room] = ambient = InteriorAmbient.For(room.TimecycleName.Value, room.SecondaryTimecycleName.Value, timecycle);
                ambients[new WorldEntityId(map, index)] = ambient;
            }
            placedCount++;
            nodes.Add(new WorldLodNode(new WorldEntityId(map, index), parent, type.Asset, placed.Transform, placed.Center, placed.Radius,
                reach > 0 ? Math.Min(placed.Distance, reach) : placed.Distance, 0, 0)
            {
                EntityFlags = (uint)entity.Flags,
                TintValue = entity.TintValue,
                ArchetypeFlags = (ulong)type.Flags,
                TimeFlags = type.TimeFlags,
            });
        }
        if (parent is { } lod)
            adopted[lod] = adopted.GetValueOrDefault(lod) + placedCount - 1;
        return next;
    }

    // An entity's LOD parent: one of this map's, or (flagged) of its parent map, by its place there.
    private static WorldEntityId? LodParent(EntityDef entity, uint map, uint parentMap, Dictionary<YmapEntity, int> indexes)
    {
        int parentIndex = entity.Parent.Kind switch
        {
            EntityParentKind.Local when entity.Parent.Entity is { } local && indexes.TryGetValue(local, out int at) => at,
            EntityParentKind.ParentMap or EntityParentKind.Unresolved => entity.Parent.Index,
            _ => -1,
        };
        return WorldLodGraph.ParentReference(map, parentMap, parentIndex, (uint)entity.Flags);
    }

    /// <summary>
    /// Where an entity is, as the game places it: its rotation decoded, scaled unless it's a fragment, then moved by
    /// <paramref name="parent"/> (an interior's placement; the identity for a map's own); its bounds and its reach (its
    /// own LOD distance, or its archetype's). Null where it can't be placed.
    /// </summary>
    private static (Matrix4x4 Transform, Vector3 Center, float Radius, float Distance)? Place(EntityDef entity, Archetype type, Matrix4x4 parent)
    {
        Matrix4x4 transform;
        try
        {
            Quaternion rotation = GtaMath.DecodeEntityRotation(entity.Rotation, (entity.Flags & YmapEntityFlags.FullMatrix) != 0, type.Animated);
            Vector3 scale = type.Type == ArchetypeAssetType.Fragment ? Vector3.One : new Vector3(entity.ScaleXY, entity.ScaleXY, entity.ScaleZ);
            transform = TransformMath.CreateTrs(entity.Position, rotation, scale) * parent;
        }
        catch (ArgumentException)
        {
            return null;
        }
        float scaleOf = type.Type == ArchetypeAssetType.Fragment ? 1 : Math.Max(Math.Abs(entity.ScaleXY), Math.Abs(entity.ScaleZ));
        Vector3 center = Vector3.Transform(type.Center, transform);
        float radius = type.Radius * scaleOf;
        float distance = entity.LodDistance > 0 ? entity.LodDistance : type.Distance;
        if (!float.IsFinite(distance) || distance <= 0 || !float.IsFinite(center.LengthSquared()) || !float.IsFinite(radius) || radius < 0
            || !VectorMath.IsFinite(transform))
            return null;
        return (transform, center, radius, Math.Min(distance, 30000));
    }
}

/// <summary>GTA V's world index on disk: the nodes as made, read back when the game's archives are as they were.</summary>
internal static class GtaWorldCache
{
    private const uint Magic = 0x57474848; // "HHGW"

    public static void Write(string path, string fingerprint, int maps, IReadOnlyList<WorldLodNode> nodes, IReadOnlyDictionary<WorldEntityId, InteriorAmbient> ambients,
        IReadOnlyDictionary<WorldEntityId, (int Set, int Entity)> interiors)
    {
        string temporary = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary)))
        {
            writer.Write(Magic);
            writer.Write(fingerprint);
            writer.Write(maps);
            writer.Write(nodes.Count);
            foreach (WorldLodNode node in nodes)
            {
                writer.Write(node.Id.Map);
                writer.Write(node.Id.Index);
                writer.Write(node.Parent is not null);
                if (node.Parent is { } parent)
                {
                    writer.Write(parent.Map);
                    writer.Write(parent.Index);
                }
                writer.Write(node.Asset.Hash);
                writer.Write(node.Asset.Extension);
                writer.Write(node.Asset.Member);
                writer.Write(node.Asset.TextureDictionary);
                Matrix4x4 t = node.Transform;
                foreach (float value in new[] { t.M11, t.M12, t.M13, t.M14, t.M21, t.M22, t.M23, t.M24, t.M31, t.M32, t.M33, t.M34, t.M41, t.M42, t.M43, t.M44 })
                    writer.Write(value);
                writer.Write(node.Center.X);
                writer.Write(node.Center.Y);
                writer.Write(node.Center.Z);
                writer.Write(node.Radius);
                writer.Write(node.Distance);
                writer.Write(node.ChildDistance);
                writer.Write(node.ExpectedChildren);
                writer.Write(node.EntityFlags);
                writer.Write(node.TintValue);
                writer.Write(node.ArchetypeFlags);
                writer.Write(node.TimeFlags);
                writer.Write(node.Grass is not null);
                if (node.Grass is { } grass)
                {
                    writer.Write(grass.BatchIndex);
                    writer.Write(grass.InstanceCount);
                    Write(writer, grass.ModelCenter);
                    writer.Write(grass.ModelRadius);
                }
                writer.Write(node.Instances.Count);
                foreach (WorldInstance instance in node.Instances)
                {
                    Matrix3x4 rows = instance.TransformRows;
                    foreach (Vector4 row in (ReadOnlySpan<Vector4>)[rows.Row0, rows.Row1, rows.Row2])
                    {
                        writer.Write(row.X);
                        writer.Write(row.Y);
                        writer.Write(row.Z);
                        writer.Write(row.W);
                    }
                    Write(writer, instance.Center);
                    writer.Write(instance.Radius);
                }
            }
            writer.Write(ambients.Count);
            foreach (var (id, ambient) in ambients)
            {
                writer.Write(id.Map);
                writer.Write(id.Index);
                Write(writer, ambient.Down);
                Write(writer, ambient.Up);
                writer.Write(ambient.Natural);
            }
            writer.Write(interiors.Count);
            foreach (var (id, inside) in interiors)
            {
                writer.Write(id.Map);
                writer.Write(id.Index);
                writer.Write(inside.Set);
                writer.Write(inside.Entity);
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vector3 Read(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    /// <summary>The cached index; null when there's none for the game as it is, or it can't be read.</summary>
    public static (int Maps, WorldLodNode[] Nodes, Dictionary<WorldEntityId, InteriorAmbient> Ambients, Dictionary<WorldEntityId, (int Set, int Entity)> Interiors)? Read(
        string path, string fingerprint)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != Magic || reader.ReadString() != fingerprint)
                return null;
            int maps = reader.ReadInt32(), count = reader.ReadInt32();
            if (count is < 0 or > 50_000_000)
                return null;
            var nodes = new WorldLodNode[count];
            for (int i = 0; i < count; i++)
            {
                var id = new WorldEntityId(reader.ReadUInt32(), reader.ReadInt32());
                WorldEntityId? parent = reader.ReadBoolean() ? new WorldEntityId(reader.ReadUInt32(), reader.ReadInt32()) : null;
                var asset = new WorldAssetKey(reader.ReadUInt32(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32());
                var m = new float[16];
                for (int k = 0; k < 16; k++)
                    m[k] = reader.ReadSingle();
                var transform = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                var center = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                float radius = reader.ReadSingle(), distance = reader.ReadSingle(), childDistance = reader.ReadSingle();
                int children = reader.ReadInt32();
                var node = new WorldLodNode(id, parent, asset, transform, center, radius, distance, childDistance, children)
                {
                    EntityFlags = reader.ReadUInt32(),
                    TintValue = reader.ReadUInt32(),
                    ArchetypeFlags = reader.ReadUInt64(),
                    TimeFlags = reader.ReadUInt32(),
                };
                if (reader.ReadBoolean())
                    node = node with { Grass = new WorldGrassBatch(reader.ReadInt32(), reader.ReadInt32(), Read(reader), reader.ReadSingle()) };
                int instances = reader.ReadInt32();
                if (instances is < 0 or > 1_000_000)
                    return null;
                if (instances > 0)
                {
                    var placements = new WorldInstance[instances];
                    for (int k = 0; k < instances; k++)
                    {
                        Vector4 Row() => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var rows = new Matrix3x4(Row(), Row(), Row());
                        placements[k] = new WorldInstance(rows, Read(reader), reader.ReadSingle());
                    }
                    node = node with { Instances = placements };
                }
                nodes[i] = node;
            }
            int rooms = reader.ReadInt32();
            if (rooms is < 0 or > 10_000_000)
                return null;
            var ambients = new Dictionary<WorldEntityId, InteriorAmbient>(rooms);
            for (int i = 0; i < rooms; i++)
                ambients[new WorldEntityId(reader.ReadUInt32(), reader.ReadInt32())] = new InteriorAmbient(Read(reader), Read(reader), reader.ReadSingle());
            int placed = reader.ReadInt32();
            if (placed is < 0 or > 10_000_000)
                return null;
            var interiors = new Dictionary<WorldEntityId, (int Set, int Entity)>(placed);
            for (int i = 0; i < placed; i++)
                interiors[new WorldEntityId(reader.ReadUInt32(), reader.ReadInt32())] = (reader.ReadInt32(), reader.ReadInt32());
            return (maps, nodes, ambients, interiors);
        }
        catch (Exception error) when (error is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
