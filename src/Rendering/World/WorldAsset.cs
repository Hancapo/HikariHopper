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
    private WorldAsset(WorldAssetKey key, DrawableScene scene, float[] lodDistances, IReadOnlyList<RedTexture> textures)
    {
        Key = key;
        Scene = scene;
        LodReach2 = [.. lodDistances.Select(d => d >= float.MaxValue ? float.MaxValue : d * d)];
        foreach (SurfacePart part in scene.Parts)
            if (part.Lod is >= 0 and < Lods)
                LodMask |= (byte)(1 << part.Lod);
        if (LodMask == 0)
            LodMask = 1;
        MemoryCost = Estimate(scene, textures);
        Bytes = WorldMemoryCost.Total([MemoryCost]);
    }

    /// <summary>The drawable levels of detail an asset has at most.</summary>
    internal const int Lods = 4;

    public WorldAssetKey Key { get; }

    /// <summary>Its vertices relative to <see cref="DrawableScene.Origin"/>, its parts and their RDR2 materials.</summary>
    public DrawableScene Scene { get; }

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
    public static WorldAsset Load(WorldAssetKey key, RpfTextureCatalog catalog, Rdr2Crypto crypto, RpfReadSession reads)
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
        return new WorldAsset(key, scene, distances, textures);
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
    private static WorldMemoryCost Estimate(DrawableScene scene, IReadOnlyList<RedTexture> textures)
    {
        long owned = 2 * ((long)scene.Vertices.Length * SurfaceVertex.Size + (long)scene.Indices.Length * 4)
            + (long)scene.Materials.Count * Rdr2Material.UniformBytes + 1024;
        var shared = textures.Distinct().Select(texture => new WorldSharedMemory(Identities.GetOrCreateValue(texture).Value, (long)texture.Data.Length * 3));
        return new WorldMemoryCost(owned, shared);
    }
}
