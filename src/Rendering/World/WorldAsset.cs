using System.Numerics;
using System.Runtime.CompilerServices;
using RedFury.Rpf;
using RedFury.Textures;
using RedFury.World;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering.World;

/// <summary>
/// One of the world's assets made ready for the GPU on the streamer's worker (after RedFury's WorldAsset): an
/// archetype's drawable, dictionary member or fragment read from the game's archives, as the viewer's RDR2 scene, its
/// textures bound as the game binds them, and what it costs to keep.
/// </summary>
internal sealed class WorldAsset
{
    private WorldAsset(WorldAssetKey key, DrawableScene scene, float[] lodDistances, WorldLightBones lightBones,
        Func<GameTexture, WorldTextureId?>? identify = null)
    {
        Key = key;
        Scene = scene;
        LightBones = lightBones;
        LodReach2 = [.. lodDistances.Select(d => d >= float.MaxValue ? float.MaxValue : d * d)];
        foreach (SurfacePart part in scene.Parts)
            if (part.Lod is >= 0 and < Lods)
            {
                LodMask |= (byte)(1 << part.Lod);
                if (part.Casts)
                    CasterLods |= (byte)(1 << part.Lod);
            }
        if (LodMask == 0)
            LodMask = 1;
        Layout = WorldLayout.Of(scene);
        // The textures that go up with it: those its drawn surfaces bind, each by what it is.
        var ids = new Dictionary<GameTexture, object>(ReferenceEqualityComparer.Instance);
        foreach (var (_, material, rdr2) in Layout.Kept)
            foreach (GameTexture? texture in rdr2 is not null ? rdr2.Textures : material.BoundTextures())
                if (texture is not null && !ids.ContainsKey(texture))
                    ids.Add(texture, identify?.Invoke(texture) is { } named ? named : Contents.GetValue(texture, ContentOf));
        textureIds = ids;
        WorldMemoryCost cost = Estimate(Layout, ids);
        MemoryCost = new WorldMemoryCost(cost.OwnedBytes + lightBones.Bytes, cost.Shared);
        Bytes = WorldMemoryCost.Total([MemoryCost]);
    }

    /// <summary>The drawable levels of detail an asset has at most.</summary>
    internal const int Lods = 4;

    public WorldAssetKey Key { get; }

    /// <summary>
    /// Its parts and their materials; its vertices are <see cref="Layout"/>'s, moved there into the asset's own space
    /// (no longer from <see cref="DrawableScene.Origin"/>).
    /// </summary>
    public DrawableScene Scene { get; }

    /// <summary>Its surfaces laid out for the GPU, on the worker that read it.</summary>
    public WorldLayout Layout { get; }

    /// <summary>Its bones, where its lights are attached.</summary>
    public WorldLightBones LightBones { get; }

    /// <summary>Its levels of detail that have parts, a bit each.</summary>
    public byte LodMask { get; }

    /// <summary>Those of them with a part its model's render mask casts.</summary>
    public byte CasterLods { get; }

    /// <summary>Each level's switch distance, squared; unlimited where it has none.</summary>
    public float[] LodReach2 { get; }

    /// <summary>What it keeps once it's up: its geometry, and its textures, which other assets may share.</summary>
    public WorldMemoryCost MemoryCost { get; }

    /// <summary><see cref="MemoryCost"/> in bytes, shared textures counted once.</summary>
    public long Bytes { get; }

    public IReadOnlyList<string> Diagnostics => Scene.Diagnostics;

    private readonly IReadOnlyDictionary<GameTexture, object> textureIds;

    /// <summary>
    /// What a texture is, for keeping it on the GPU once whichever asset read it: a dictionary's texture by the
    /// dictionary and its name (a <see cref="WorldTextureId"/>), so it's the one already up when its dictionary is read
    /// again for another asset; any other by its pixels (a <see cref="WorldTextureContent"/>). Never the object read:
    /// that kept every such texture's pixels in memory for as long as it was on the GPU, and put it up again each time
    /// it was read again (all of RDR2's are read so, and GTA V's models' own).
    /// </summary>
    public object TextureId(GameTexture texture) => textureIds.TryGetValue(texture, out object? id) ? id : texture;

    // Each texture read is hashed once, however many assets bind it while it's held.
    private static readonly ConditionalWeakTable<GameTexture, object> Contents = new();

    private static object ContentOf(GameTexture texture) => new WorldTextureContent(System.IO.Hashing.XxHash3.HashToUInt64(texture.Data),
        texture.Name, texture.Width, texture.Height, (int)texture.Format, texture.MipCount);

    /// <summary>
    /// Reads an asset from the catalog's archives: a drawable (extension 2), a fragment's intact drawables (3) or a
    /// drawable dictionary's member, or all of it (4). Holds no archive open after.
    /// </summary>
    /// <exception cref="FileNotFoundException">The catalog has no such asset.</exception>
    /// <exception cref="NotSupportedException">An asset of another kind.</exception>
    public static WorldAsset LoadRdr2(WorldAssetKey key, RpfTextureCatalog catalog, Rdr2Crypto crypto, RpfReadSession reads)
    {
        TextureAssetLocation location = catalog.Find(key.Hash, key.Extension).LastOrDefault()
            ?? throw new FileNotFoundException($"Asset {key} not found.");
        string name = $"{key.Hash:X8}";
        IReadOnlyList<Rdr2Scene.Placed> models = location.Load(crypto, entry => key.Extension switch
        {
            2 => (IReadOnlyList<Rdr2Scene.Placed>)[new Rdr2Scene.Placed(name, RedFury.Ydr.Ydr.FromEntry(entry).Drawable, Matrix4x4.Identity)],
            3 => [.. Rdr2Scene.Fragment(RedFury.Yft.Yft.FromEntry(entry), name).Where(placed => placed.Variant != ModelVariant.Damaged)],
            4 => [.. RedFury.Ydd.Ydd.FromEntry(entry).Drawables.Where(member => key.Member == 0 || member.NameHash == key.Member)
                .Select(member => new Rdr2Scene.Placed(member.Label, member.Drawable, Matrix4x4.Identity))],
            _ => throw new NotSupportedException($"Unsupported world asset {key}."),
        }, reads);
        TextureBindingPlan binding = catalog.Bind(key.Member == 0 ? key.Hash : key.Member, key.Extension, location.RootPath, [key.TextureDictionary]);
        GameTexture? Find(RedFury.Drawables.TextureReference reference) =>
            binding.Resolve(reference.NameHash) is { } found ? Rdr2Textures.Of(found) : null;
        DrawableScene scene = Rdr2Scene.BuildWorld(models, Find);
        // Each level's switch distance, the furthest of its drawables'.
        float[] distances = [0, 0, 0, 0];
        foreach (Rdr2Scene.Placed model in models)
            for (int lod = 0; lod < Lods; lod++)
                if (model.Drawable.Lods.TryGetValue((CommonFury.Assets.DrawableLod)lod, out var level) && level.Count != 0
                    && lod < model.Drawable.LodDistances.Count && model.Drawable.LodDistances[lod] is > 0 and var distance)
                    distances[lod] = Math.Max(distances[lod], distance);
        for (int lod = 0; lod < Lods; lod++)
            if (distances[lod] <= 0)
                distances[lod] = float.MaxValue;
        return new WorldAsset(key, scene, distances, WorldLightBones.From(models));
    }

    /// <summary>
    /// A GTA V asset made ready from its scene (GtaWorldIndex reads it): each level's switch distance its drawables'
    /// furthest (unlimited where none says), and what its textures hold, the dictionaries' shared with other assets.
    /// </summary>
    public static WorldAsset FromGta(WorldAssetKey key, DrawableScene scene, IEnumerable<FiveFury.Drawables.Drawable> drawables,
        Func<GameTexture, WorldTextureId?>? identify = null)
    {
        float[] distances = [0, 0, 0, 0];
        foreach (FiveFury.Drawables.Drawable drawable in drawables)
            for (int lod = 0; lod < Lods; lod++)
                if (drawable.Lods[(CommonFury.Assets.DrawableLod)lod] is { Models.Count: > 0, Distance: > 0 and var distance })
                    distances[lod] = Math.Max(distances[lod], distance);
        for (int lod = 0; lod < Lods; lod++)
            if (distances[lod] <= 0)
                distances[lod] = float.MaxValue;
        return new WorldAsset(key, scene, distances, WorldLightBones.Carrying(scene.Lights, scene.LightCoronas), identify);
    }

    // Shared allocations are told apart by identity, so a texture several assets bind counts once: the same texture is
    // the same allocation however many times it's read. The identity is worked out from what the texture is, not kept:
    // a table of every texture seen grew for as long as the app ran, past the assets that bound them. Two textures
    // taken for one by a 64-bit hash would only be counted once.
    private static long IdentityOf(object texture)
    {
        Span<ulong> fields = stackalloc ulong[4];
        switch (texture)
        {
            case WorldTextureId id:
                fields[0] = 1;
                fields[1] = (ulong)id.Dictionary << 32 | id.Name;
                break;
            case WorldTextureContent content:
                fields[0] = 2;
                fields[1] = content.Pixels;
                fields[2] = (ulong)(uint)content.Width << 32 | (uint)content.Height;
                fields[3] = ((ulong)(uint)content.Format << 32 | (uint)content.Mips)
                    ^ System.IO.Hashing.XxHash3.HashToUInt64(System.Runtime.InteropServices.MemoryMarshal.AsBytes((content.Name ?? "").AsSpan()));
                break;
            default:
                throw new ArgumentException($"Not a texture's identity: {texture.GetType().Name}", nameof(texture));
        }
        // Identities are above nought.
        return (long)(System.IO.Hashing.XxHash3.HashToUInt64(System.Runtime.InteropServices.MemoryMarshal.AsBytes(fields)) >> 1) + 1;
    }

    // What it keeps once it's up, and no more: the vertices and indexes laid out for the GPU, each material's values,
    // and each texture's pixels on the GPU. What reading it took besides (its scene, the pixels read) goes once it's
    // up, and only the few assets read ahead of the uploads hold that meanwhile. Geometry used to count twice and
    // textures three times, so the world was charged two and a half times what it held: GTA V's reached the limit and
    // evicted while holding 1.7 GB, and RDR2's stood at it in Valentine holding 1.5 GB, its plan turning detail away.
    private static WorldMemoryCost Estimate(WorldLayout layout, Dictionary<GameTexture, object> textures)
    {
        long owned = (long)layout.Vertices.Length * SurfaceVertex.Size + (long)layout.Indices.Length * sizeof(uint)
            + (long)layout.Kept.Select(kept => kept.Part.Material).Distinct().Count() * Math.Max(Rdr2Material.UniformBytes, SurfaceMaterial.UniformBytes) + 1024;
        // Two of the objects it read can be one texture: the larger says what it takes.
        var shared = textures.GroupBy(texture => IdentityOf(texture.Value))
            .Select(group => new WorldSharedMemory(group.Key, group.Max(texture => (long)texture.Key.Data.Length)));
        return new WorldMemoryCost(owned, shared);
    }
}

/// <summary>A texture of a texture dictionary the game streams: the dictionary's name hash and the texture's.</summary>
internal readonly record struct WorldTextureId(uint Dictionary, uint Name);

/// <summary>
/// A texture no dictionary names (a model's own, or one of RDR2's, read through its binding): its pixels' hash, with
/// its name and its shape.
/// </summary>
internal readonly record struct WorldTextureContent(ulong Pixels, string? Name, int Width, int Height, int Format, int Mips);

/// <summary>
/// An asset's surfaces as the world draws them, laid out for the GPU (RedFury's BufferLayout): RDR2's materials, water
/// included, and GTA V's that Gta5/surface.wgsl, Gta5/terrain.wgsl and Gta5/water.wgsl draw (not yet its water foams, fur grass
/// and cables, which the model viewer draws with shaders of their own), by level of detail, each level's solid surfaces first so they
/// cast as one, the indexes made the vertices' own, and the vertices in the asset's own space (the scene has them from
/// its middle), as the placements' matrices take them.
/// </summary>
internal sealed class WorldLayout
{
    /// <summary>Its surfaces, by part, with their materials.</summary>
    public List<(SurfacePart Part, SurfaceMaterial Material, Rdr2Material? Rdr2)> Kept { get; } = [];

    /// <summary>The surfaces in the order their indexes are laid out.</summary>
    public int[] Order { get; private set; } = [];

    /// <summary>Each surface's part with its indexes where they're laid out, and its middle in the asset's space.</summary>
    public SurfacePart[] Placed { get; private set; } = [];

    public SurfaceVertex[] Vertices { get; private set; } = [];

    public uint[] Indices { get; private set; } = [];

    // Whether the world draws a material yet.
    private static bool Drawn(SurfaceMaterial material) => material.Rdr2 is not null
        || material.Water is not null
        || !material.Fur && !material.Cable && !material.Glass && !material.Shader.StartsWith("water", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lays out a scene made for one asset, whose vertices it takes and moves into the asset's space.</summary>
    public static WorldLayout Of(DrawableScene scene)
    {
        var layout = new WorldLayout();
        foreach (SurfacePart part in scene.Parts)
            if (part.IndexCount > 0 && scene.Materials[part.Material] is var material && Drawn(material))
                layout.Kept.Add((part, material, material.Rdr2));
        if (layout.Kept.Count == 0 || scene.Vertices.Length == 0)
            return layout;
        var kept = layout.Kept;
        static int Key(SurfaceMaterial material) => material.AlphaMode == AlphaMode.Opaque && !material.Decal ? 0 : 1;
        layout.Order = [.. kept.Select((item, i) => i).OrderBy(i => kept[i].Part.Lod).ThenBy(i => Key(kept[i].Material))];
        layout.Indices = new uint[kept.Sum(item => (long)item.Part.IndexCount)];
        layout.Placed = new SurfacePart[kept.Count];
        uint next = 0;
        foreach (int i in layout.Order)
        {
            SurfacePart part = kept[i].Part;
            for (uint c = 0; c < part.IndexCount; c++)
                layout.Indices[next + c] = scene.Indices[part.FirstIndex + c] + (uint)part.BaseVertex;
            layout.Placed[i] = part with { FirstIndex = next, BaseVertex = 0, Center = part.Center + scene.Origin };
            next += part.IndexCount;
        }
        // Moved into the asset's space where they are: the scene was built for this asset alone, and nothing reads its
        // vertices from its middle after (a copy was most of what laying an asset out allocated).
        SurfaceVertex[] vertices = scene.Vertices;
        for (int v = 0; v < vertices.Length; v++)
            vertices[v] = vertices[v] with { Position = vertices[v].Position + scene.Origin };
        layout.Vertices = vertices;
        return layout;
    }
}
