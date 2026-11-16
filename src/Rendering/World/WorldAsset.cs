using System.Numerics;
using System.Runtime.CompilerServices;
using RedFury.Rpf;
using RedFury.Textures;
using RedFury.World;
using GameTexture = FiveFury.Textures.Texture;
using RedTexture = RedFury.Textures.Texture;

namespace HikariHopper.Rendering.World;

/// <summary>
/// One of the world's assets made ready for the GPU on the streamer's worker (after RedFury's WorldAsset): an
/// archetype's drawable, dictionary member or fragment read from the game's archives, as the viewer's RDR2 scene, its
/// textures bound as the game binds them, and what it costs to keep.
/// </summary>
internal sealed class WorldAsset
{
    private WorldAsset(WorldAssetKey key, DrawableScene scene, float[] lodDistances, IEnumerable<(object Texture, long Bytes)> textures,
        WorldLightBones lightBones)
    {
        Key = key;
        Scene = scene;
        LightBones = lightBones;
        LodReach2 = [.. lodDistances.Select(d => d >= float.MaxValue ? float.MaxValue : d * d)];
        foreach (SurfacePart part in scene.Parts)
            if (part.Lod is >= 0 and < Lods)
                LodMask |= (byte)(1 << part.Lod);
        if (LodMask == 0)
            LodMask = 1;
        Layout = WorldLayout.Of(scene);
        WorldMemoryCost cost = Estimate(scene, textures);
        MemoryCost = new WorldMemoryCost(cost.OwnedBytes + lightBones.Bytes, cost.Shared);
        Bytes = WorldMemoryCost.Total([MemoryCost]);
    }

    /// <summary>The drawable levels of detail an asset has at most.</summary>
    internal const int Lods = 4;

    public WorldAssetKey Key { get; }

    /// <summary>Its vertices relative to <see cref="DrawableScene.Origin"/>, its parts and their RDR2 materials.</summary>
    public DrawableScene Scene { get; }

    /// <summary>Its surfaces laid out for the GPU, on the worker that read it.</summary>
    public WorldLayout Layout { get; }

    /// <summary>Its bones, where its lights are attached.</summary>
    public WorldLightBones LightBones { get; }

    /// <summary>Its levels of detail that have parts, a bit each.</summary>
    public byte LodMask { get; }

    /// <summary>Each level's switch distance, squared; unlimited where it has none.</summary>
    public float[] LodReach2 { get; }

    /// <summary>What it costs to keep: its geometry, and its textures, which other assets may share.</summary>
    public WorldMemoryCost MemoryCost { get; }

    /// <summary><see cref="MemoryCost"/> in bytes, shared textures counted once.</summary>
    public long Bytes { get; }

    public IReadOnlyList<string> Diagnostics => Scene.Diagnostics;

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
        var textures = new List<RedTexture>();
        GameTexture? Find(RedFury.Drawables.TextureReference reference)
        {
            if (binding.Resolve(reference.NameHash) is not { } found)
                return null;
            textures.Add(found);
            return Rdr2Textures.Of(found);
        }
        DrawableScene scene = Rdr2Scene.BuildWorld(models, Find);
        // The drawables' own textures count too.
        foreach (RedFury.Ytd.Ytd own in models.Select(model => model.Drawable.EmbeddedTextures).OfType<RedFury.Ytd.Ytd>().Distinct())
            textures.AddRange(own.Textures);
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
        return new WorldAsset(key, scene, distances, textures.Select(texture => ((object)texture, (long)texture.Data.Length)), WorldLightBones.From(models));
    }

    /// <summary>
    /// A GTA V asset made ready from its scene (GtaWorldIndex reads it): each level's switch distance its drawables'
    /// furthest (unlimited where none says), and what its textures hold, the dictionaries' shared with other assets.
    /// </summary>
    public static WorldAsset FromGta(WorldAssetKey key, DrawableScene scene, IEnumerable<FiveFury.Drawables.Drawable> drawables)
    {
        float[] distances = [0, 0, 0, 0];
        foreach (FiveFury.Drawables.Drawable drawable in drawables)
            for (int lod = 0; lod < Lods; lod++)
                if (drawable.Lods[(CommonFury.Assets.DrawableLod)lod] is { Models.Count: > 0, Distance: > 0 and var distance })
                    distances[lod] = Math.Max(distances[lod], distance);
        for (int lod = 0; lod < Lods; lod++)
            if (distances[lod] <= 0)
                distances[lod] = float.MaxValue;
        var textures = new HashSet<GameTexture>(ReferenceEqualityComparer.Instance);
        foreach (SurfaceMaterial material in scene.Materials)
            foreach (GameTexture? texture in material.BoundTextures())
                if (texture is not null)
                    textures.Add(texture);
        return new WorldAsset(key, scene, distances, textures.Select(texture => ((object)texture, (long)texture.Data.Length)), WorldLightBones.Carrying(scene.Lights));
    }

    // Shared allocations are told apart by identity, so a texture several assets bind counts once.
    private sealed class MemoryIdentity
    {
        internal readonly long Value = Interlocked.Increment(ref nextIdentity);
    }

    private static long nextIdentity;
    private static readonly ConditionalWeakTable<object, MemoryIdentity> Identities = new();

    // Geometry on the GPU and while it's made (twice), each material's uniforms; each texture's pixels as read and on
    // the GPU.
    private static WorldMemoryCost Estimate(DrawableScene scene, IEnumerable<(object Texture, long Bytes)> textures)
    {
        long owned = 2 * ((long)scene.Vertices.Length * SurfaceVertex.Size + (long)scene.Indices.Length * 4)
            + (long)scene.Materials.Count * Math.Max(Rdr2Material.UniformBytes, SurfaceMaterial.UniformBytes) + 1024;
        var shared = textures.DistinctBy(texture => texture.Texture, ReferenceEqualityComparer.Instance)
            .Select(texture => new WorldSharedMemory(Identities.GetOrCreateValue(texture.Texture).Value, texture.Bytes * 3));
        return new WorldMemoryCost(owned, shared);
    }
}

/// <summary>
/// An asset's surfaces as the world draws them, laid out for the GPU (RedFury's BufferLayout): RDR2's materials, water
/// included, and GTA V's that surface.wgsl, terrain.wgsl and gta_water.wgsl draw (not yet its water foams, fur grass
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
        layout.Vertices = new SurfaceVertex[scene.Vertices.Length];
        for (int v = 0; v < layout.Vertices.Length; v++)
            layout.Vertices[v] = scene.Vertices[v] with { Position = scene.Vertices[v].Position + scene.Origin };
        return layout;
    }
}
