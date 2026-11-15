using System.Numerics;
using CommonFury.Numerics;
using FiveFury;
using FiveFury.Assets;
using CommonFury.Hashing;
using FiveFury.Numerics;
using FiveFury.Ymap;
using FiveFury.Ytyp;
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
    private GtaWorldIndex(string root, int mapCount, WorldLodGraph lod, GameInstallation game) : base(root, mapCount, lod) => Game = game;

    /// <summary>The base game, mounted.</summary>
    public GameInstallation Game { get; }

    public override WorldAsset LoadAsset(WorldAssetKey key) =>
        throw new NotSupportedException("GTA V's world assets aren't drawn yet.");

    public override Func<WorldLodNode, CancellationToken, WorldLodNode>? CreateGrass() => null;

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
    private const string CacheVersion = "hh-gta-world-1";

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
            (int Maps, WorldLodNode[] Nodes)? catalog = null;
            if (File.Exists(cache))
            {
                report(new WorldProgress("Reading the world from the cache", null));
                catalog = GtaWorldCache.Read(cache, fingerprint);
            }
            if (catalog is null)
            {
                catalog = GtaWorldCatalog.Load(game.FileSystem, report, cancellation);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                    GtaWorldCache.Write(cache, fingerprint, catalog.Value.Maps, catalog.Value.Nodes);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
            report(new WorldProgress("Ready", 1));
            return new GtaWorldIndex(root, catalog.Value.Maps, new WorldLodGraph(catalog.Value.Nodes), game);
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

    public static (int Maps, WorldLodNode[] Nodes) Load(GameFileSystem files, Action<WorldProgress> report, CancellationToken cancellation)
    {
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
                // Interiors' archetypes (MLOs) and those without a model have nothing to draw of their own here.
                if (value is not BaseArchetypeDef archetype || archetype is MloArchetypeDef || archetype.AssetType is ArchetypeAssetType.Assetless
                    or ArchetypeAssetType.Uninitialized)
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
            uint map = ymap.Name.Value != 0 ? ymap.Name.Value : file.NameHash.Value, parentMap = ymap.Parent.Value;
            read++;
            // Each entity's place in its map, which a local LOD parent is found by.
            var indexes = new Dictionary<YmapEntity, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < ymap.Entities.Count; i++)
                indexes[ymap.Entities[i]] = i;
            for (int i = 0; i < ymap.Entities.Count; i++)
            {
                if (ymap.Entities[i] is not EntityDef entity || entity is MloInstanceDef || !archetypes.TryGetValue(entity.ArchetypeName.Value, out Archetype? type))
                    continue;
                Matrix4x4 transform;
                try
                {
                    Quaternion rotation = GtaMath.DecodeEntityRotation(entity.Rotation, (entity.Flags & YmapEntityFlags.FullMatrix) != 0, type.Animated);
                    Vector3 scale = type.Type == ArchetypeAssetType.Fragment ? Vector3.One : new Vector3(entity.ScaleXY, entity.ScaleXY, entity.ScaleZ);
                    transform = TransformMath.CreateTrs(entity.Position, rotation, scale);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                float scaleOf = type.Type == ArchetypeAssetType.Fragment ? 1 : Math.Max(Math.Abs(entity.ScaleXY), Math.Abs(entity.ScaleZ));
                Vector3 center = Vector3.Transform(type.Center, transform);
                float radius = type.Radius * scaleOf;
                float distance = entity.LodDistance > 0 ? entity.LodDistance : type.Distance;
                if (!float.IsFinite(distance) || distance <= 0 || !float.IsFinite(center.LengthSquared()) || !float.IsFinite(radius) || radius < 0
                    || !VectorMath.IsFinite(transform))
                    continue;
                // The LOD parent: an entity of this map, or (flagged) of its parent map, by its place there.
                int parentIndex = entity.Parent.Kind switch
                {
                    EntityParentKind.Local when entity.Parent.Entity is { } local && indexes.TryGetValue(local, out int at) => at,
                    EntityParentKind.ParentMap or EntityParentKind.Unresolved => entity.Parent.Index,
                    _ => -1,
                };
                uint flags = (uint)entity.Flags;
                WorldEntityId? parent = WorldLodGraph.ParentReference(map, parentMap, parentIndex, flags);
                float childDistance = float.IsFinite(entity.ChildLodDistance) ? entity.ChildLodDistance : 0;
                nodes.Add(new WorldLodNode(new WorldEntityId(map, i), parent, type.Asset, transform, center, radius, Math.Min(distance, 30000),
                    childDistance, checked((int)entity.NumberOfChildren))
                {
                    EntityFlags = flags,
                    TintValue = entity.TintValue,
                    ArchetypeFlags = (ulong)type.Flags,
                    TimeFlags = type.TimeFlags,
                });
            }
        }
        if (nodes.Count == 0)
            throw new InvalidDataException("No readable world placements were found in the game's maps.");
        return (read, [.. nodes]);
    }
}

/// <summary>GTA V's world index on disk: the nodes as made, read back when the game's archives are as they were.</summary>
internal static class GtaWorldCache
{
    private const uint Magic = 0x57474848; // "HHGW"

    public static void Write(string path, string fingerprint, int maps, IReadOnlyList<WorldLodNode> nodes)
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
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The cached index; null when there's none for the game as it is, or it can't be read.</summary>
    public static (int Maps, WorldLodNode[] Nodes)? Read(string path, string fingerprint)
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
                nodes[i] = new WorldLodNode(id, parent, asset, transform, center, radius, distance, childDistance, children)
                {
                    EntityFlags = reader.ReadUInt32(),
                    TintValue = reader.ReadUInt32(),
                    ArchetypeFlags = reader.ReadUInt64(),
                    TimeFlags = reader.ReadUInt32(),
                };
            }
            return (maps, nodes);
        }
        catch (Exception error) when (error is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
