using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using RedFury.World;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// A game's world (after RedFury's WebGpuRenderer.World): the streamed assets, each uploaded once and drawn once per
// placement in view with rdr2_world.wgsl. The covered placements' matrices stay on the GPU per asset; when the view
// changes, the camera's and the due shadow cascades' culls only write the indexes of the placements each sees, grouped
// by the drawable level of detail each is at. The streamer (WorldStreamer) says what's covered and hands over the assets
// it read; this keeps them within the budget. Drawing is in ViewportRenderer.WorldBundles.cs, the sun's shadow in
// ViewportRenderer.WorldShadows.cs.
internal sealed unsafe partial class ViewportRenderer
{
    private sealed class WorldGpuAsset(WorldAsset source)
    {
        internal readonly WorldAssetKey Key = source.Key;
        internal WorldMemoryCost MemoryCost = source.MemoryCost;
        internal readonly byte LodMask = source.LodMask;
        internal readonly WorldLightBones LightBones = source.LightBones;
        internal readonly float[] LodReach2 = source.LodReach2;

        internal WorldSlice? Vertices, Indices;

        // Its surfaces, laid out by level of detail with each level's solid ones together, and those runs of solid
        // surfaces, each drawn as one in the shadow.
        internal readonly List<WorldMesh> Meshes = [];
        internal readonly List<WorldDepthRun> DepthRuns = [];
        internal readonly List<object> Textures = [];
        internal bool CastsShadows;

        // Whether it has an RDR2 cut-out drawn with sub-sampled alpha, which the frame then puts together
        // (ViewportRenderer.Rdr2SubSampleAlpha.cs).
        internal bool SubSampled;

        // The covered placements' matrices (a storage buffer, and the group reading it), and the indexes of those the
        // camera sees and of those casting into each cascade.
        internal WorldSlice? SourceInstances, Instances, ShadowInstances;
        internal BindGroup? SourceGroup;
        internal readonly WorldInstanceBuffer Sources = new();

        // Which placement each of Sources is: its node, and which of its instances (-1 for a node without any).
        internal readonly List<(WorldLodNode Node, int Instance)> Placements = [];

        // The nodes its storage holds, in the cull set's order, with each one's first row, and this coverage's (by their
        // index in the cull set).
        internal readonly List<WorldLodNode> SourceNodes = [];
        internal readonly List<int> SourceFirsts = [];
        internal readonly List<int> NextNodes = [];

        // Where each covered node's placements are in its storage, which stay put while it's covered; the gaps nodes no
        // longer covered left, refilled first; the rows in use, gaps included; the rows live, and how many are mirrored;
        // and whether a node's fade began since its rows were written.
        internal readonly Dictionary<WorldLodNode, WorldSourceSlot> Slots = new(ReferenceEqualityComparer.Instance);
        internal readonly List<(int First, int Count)> Gaps = [];
        internal int SourceRows, LiveSources, ReflectedSources;
        internal bool FadeChanged;

        // Its number in the picking pass, from 1.
        internal int Id;
        internal long SourceRevision = -1;
        internal int Capacity, ShadowCapacity;
        internal bool Reflected;
        internal readonly WorldInstanceIndices Data = new(), ShadowData = new();

        // Whether the indexes go to the GPU whether or not they changed: a new buffer holds none.
        internal bool Rewrite, RewriteShadow;

        // The range of each level of detail's placements in Data, and in ShadowData per cascade (cascade * Lods + level).
        internal readonly int[] LodStart = new int[WorldAsset.Lods], LodCount = new int[WorldAsset.Lods];
        internal readonly int[] ShadowStart = new int[WorldShadowCascades.Count * WorldAsset.Lods];
        internal readonly int[] ShadowCount = new int[WorldShadowCascades.Count * WorldAsset.Lods];

        // Each level's scratch while a view's placements are gathered.
        internal readonly WorldInstanceIndices[] Buckets = [new(), new(), new(), new()];
        internal bool Listed, Gathered;

        // The levels any placement in view or casting is at, this update.
        internal byte NeededLods;

        // Its render bundles (the solid surfaces, the decals, then each cascade's casters), the levels each holds, and how
        // many updates in a row they've held levels no longer used.
        internal readonly RenderBundle?[] Bundles = new RenderBundle?[WorldBundleCount];
        internal readonly int[] BundleDraws = new int[WorldBundleCount];
        internal readonly byte[] BundleLods = new byte[WorldBundleCount];
        internal int SurplusUpdates;

        // Its first slot in the shared indirect arguments, once it has them.
        internal int IndirectFirst = -1;

        // When the coverage last held it: the oldest go first when room is needed.
        internal long Used;

        internal int Level(float distance2) => WorldLodSelection.Level(LodMask, LodReach2, distance2);
    }

    // A covered node's rows in its asset's storage, the fade they were written with, and the update that last saw it.
    private struct WorldSourceSlot
    {
        internal int First, Count, Reflected;
        internal (float Start, float Direction) Fade;
        internal long Seen;
    }

    // A surface of an asset: its part (with indexes into the asset's vertices as they are), and how it's drawn.
    // Night-only: a _geomnightonly shader's, gone by day (SurfaceMaterial.NightOnly), which the shadow's bundles leave
    // out, day and night.
    private readonly record struct WorldMesh(SurfacePart Part, BindGroup Material, WorldSlice Uniforms, AlphaMode Mode, bool DoubleSided, bool Decal,
        WorldFamily Family, bool NightOnly = false, bool CullsBack = false)
    {
        internal int Level => Part.Lod is >= 0 and < WorldAsset.Lods ? Part.Lod : 0;

        // Drawn into the main view, as its model's render mask has it (a tree's shadow proxy isn't).
        internal bool Opaque => Mode != AlphaMode.Blend && Part.Shown;

        internal bool Blended => Mode == AlphaMode.Blend && !Decal && Part.Shown;

        // Water: drawn after the copy of the scene behind it.
        internal bool Water => Family is WorldFamily.Rdr2Water or WorldFamily.GtaWater;

        // Solid without a cut-out: casts without its material, beside the others of its level.
        // Each only where its model's render mask casts it (a tree's shadow proxy does, the tree it stands in for doesn't).
        internal bool Caster => Mode == AlphaMode.Opaque && !Decal && !NightOnly && Part.Casts;

        // A cut-out that casts through its material.
        internal bool MaskedCaster => Mode == AlphaMode.Mask && !NightOnly && Part.Casts;
    }

    // Adjacent solid surfaces of a level, cast into the shadow as one draw.
    private readonly record struct WorldDepthRun(int Level, uint FirstIndex, uint IndexCount);

    // A blended surface of one placement in view, and its distance from the camera, squared: drawn far to near.
    private readonly record struct WorldBlendedDraw(WorldGpuAsset Asset, WorldMesh Mesh, uint Instance, float Distance);

    private readonly Dictionary<WorldAssetKey, WorldGpuAsset> worldAssets = [];

    // The assets' buffers, in shared pages by usage: vertices and placements' indexes, indexes, placements' matrices,
    // and materials' values.
    private WorldBufferPages? worldVertexPages, worldIndexPages, worldStoragePages, worldUniformPages;

    private WorldBufferPages VertexPages => worldVertexPages ??= new(device, BufferUsage.Vertex | BufferUsage.CopyDst);

    private WorldBufferPages IndexPages => worldIndexPages ??= new(device, BufferUsage.Index | BufferUsage.CopyDst);

    private WorldBufferPages StoragePages => worldStoragePages ??= new(device, BufferUsage.Storage | BufferUsage.CopyDst);

    private WorldBufferPages UniformPages => worldUniformPages ??= new(device, BufferUsage.Uniform | BufferUsage.CopyDst, 1024 * 1024);

    /// <summary>The GPU memory the world's buffer pages take, what's used of it, and how many buffers they are.</summary>
    internal (ulong Bytes, ulong Used, int Buffers) WorldBufferMemory
    {
        get
        {
            WorldBufferPages?[] all = [worldVertexPages, worldIndexPages, worldStoragePages, worldUniformPages];
            ulong bytes = 0, used = 0;
            int buffers = 0;
            foreach (WorldBufferPages? pages in all)
                if (pages is not null)
                    (bytes, used, buffers) = (bytes + pages.Bytes, used + pages.UsedBytes, buffers + pages.Buffers);
            return (bytes, used, buffers);
        }
    }
    // The world's textures on the GPU by what they are (WorldAsset.TextureId), not by the object read: the pixels read
    // go once they're up, as nothing here holds them, and a texture read again is the one up. Each with how many
    // assets bind it; one that can't be drawn has no GPU texture and reads as missing.
    private sealed class WorldTexture(WebGpuSharp.Texture? gpu)
    {
        internal readonly WebGpuSharp.Texture? Gpu = gpu;
        internal readonly TextureView? View = gpu is null ? null : ShapedView(gpu);
        internal TextureView? Srgb;
        internal int Users;
    }

    private readonly Dictionary<object, WorldTexture> worldTextures = [];

    /// <summary>The world's textures on the GPU.</summary>
    internal int WorldTextureCount => worldTextures.Count;
    private readonly WorldCullSet worldCull = new();
    private WorldSelection worldSelection = new([], []);
    private WorldGpuAsset?[] worldNodeAsset = [];
    private bool[] worldNodeCasts = [];
    // Each covered node's first row in its asset's storage: its placements' rows follow, as its entries do.
    private int[] worldNodeRow = [];

    // What each volume sees: the camera, then each cascade.
    private readonly List<int>[] worldVisible = [.. Enumerable.Range(0, 1 + WorldShadowCascades.Count).Select(_ => new List<int>())];
    private readonly List<WorldGpuAsset> listedAssets = [], gatheredAssets = [];
    private bool worldSourcesDirty = true, worldNodeAssetsDirty = true, worldVisibilityDirty = true;
    private long worldSourceBuilds, worldClock;
    private (Vector3 Eye, Vector3 Forward, int Width, int Height, Vector3 Sun, bool Shadows) worldView;
    private BindGroupLayout? worldInstanceLayout, worldEmptyLayout;
    private BindGroup? worldEmptyGroup;
    private RenderPipeline? worldShadow;

    // Each family's pipelines (see WorldFamily).
    private const int WorldFamilies = 5;
    private readonly RenderPipeline?[] worldSolid = new RenderPipeline?[WorldFamilies], worldUnculled = new RenderPipeline?[WorldFamilies],
        worldBlended = new RenderPipeline?[WorldFamilies], worldDecal = new RenderPipeline?[WorldFamilies],
        worldMaskedShadow = new RenderPipeline?[WorldFamilies];
    private readonly List<WorldBlendedDraw> worldBlendedDraws = [], worldWaterDraws = [];
    private bool worldShown;
    private int worldAssetIds;

    /// <summary>
    /// Whether this view shows a world: the scene is then drawn relative to the camera, the backdrop's grid and floor
    /// go, and the world's assets are drawn.
    /// </summary>
    public bool WorldShown
    {
        get => worldShown;
        set
        {
            worldShown = value;
            if (!value)
            {
                ClearWorld();
                // Outside a world, GLOBAL's timecycle again.
                SetTimecyclePlace(null);
            }
        }
    }

    /// <summary>The assets uploaded, and the one on its way up.</summary>
    public IReadOnlySet<WorldAssetKey> WorldResident => pendingWorld is { } pending
        ? worldAssets.Keys.Append(pending.Source.Key).ToHashSet() : worldAssets.Keys.ToHashSet();

    /// <summary>Whether an asset is uploaded and can be drawn.</summary>
    public bool IsWorldReady(WorldAssetKey key) => worldAssets.ContainsKey(key);

    /// <summary>An uploaded asset's bones, where its lights are attached; null when it isn't uploaded.</summary>
    public WorldLightBones? WorldAssetLightBones(WorldAssetKey key) => worldAssets.TryGetValue(key, out WorldGpuAsset? asset) ? asset.LightBones : null;

    /// <summary>What an uploaded asset costs to keep, with its placements' storage; null when it isn't.</summary>
    public WorldMemoryCost? WorldAssetMemory(WorldAssetKey key) => worldAssets.TryGetValue(key, out WorldGpuAsset? asset) ? asset.MemoryCost
        : pendingWorld?.Source.Key == key ? pendingWorld.Source.MemoryCost : null;

    // Running totals (RedFury's WorldMemory, copied), so the budget's checks never add up every asset: shared textures
    // are counted once, at the largest size any holder said, for as long as one holds them.
    private long worldOwnedBytes, worldSharedBytes;
    private readonly Dictionary<long, (long Bytes, int Holders)> worldShared = [];

    /// <summary>What the uploaded assets cost, shared textures counted once, with their placements' storage.</summary>
    public long WorldBytes => worldOwnedBytes + worldSharedBytes;

    /// <summary>The assets uploaded.</summary>
    public int WorldAssetCount => worldAssets.Count;

    /// <summary>The placements the camera saw in the last frame.</summary>
    public int WorldDrawnCount { get; private set; }

    private void ChargeWorldMemory(WorldMemoryCost cost)
    {
        worldOwnedBytes += cost.OwnedBytes;
        foreach (WorldSharedMemory item in cost.Shared)
        {
            var (bytes, holders) = worldShared.GetValueOrDefault(item.Identity);
            if (item.Bytes > bytes)
            {
                worldSharedBytes += item.Bytes - bytes;
                bytes = item.Bytes;
            }
            worldShared[item.Identity] = (bytes, holders + 1);
        }
    }

    private void ReleaseWorldMemory(WorldMemoryCost cost)
    {
        worldOwnedBytes -= cost.OwnedBytes;
        foreach (WorldSharedMemory item in cost.Shared)
        {
            var (bytes, holders) = worldShared[item.Identity];
            if (holders > 1)
                worldShared[item.Identity] = (bytes, holders - 1);
            else
            {
                worldShared.Remove(item.Identity);
                worldSharedBytes -= bytes;
            }
        }
    }

    // What's charged once an asset not yet uploaded is.
    private long ProposedWorldBytes(WorldAsset asset)
    {
        long total = WorldBytes + asset.MemoryCost.OwnedBytes;
        foreach (WorldSharedMemory item in asset.MemoryCost.Shared)
            total += Math.Max(0, item.Bytes - worldShared.GetValueOrDefault(item.Identity).Bytes);
        return total;
    }

    // An asset's placements' storage, owned by it: its charge follows its capacity, not what's in view.
    private static long InstanceBytes(WorldGpuAsset asset) => asset.Capacity * 132L + asset.ShadowCapacity * 4L;

    /// <summary>
    /// Puts an asset the streamer read on its way to the GPU, within <paramref name="budget"/>: the least recently
    /// covered assets not in <paramref name="protectedAssets"/> go to make room. False when there isn't room even so,
    /// or another asset is on its way (<see cref="ProcessWorldUploads"/> takes it the rest of the way).
    /// </summary>
    /// <exception cref="NotSupportedException">The GPU can't bind as many textures as RDR2's material reads.</exception>
    public bool AddWorldAsset(WorldAsset asset, IReadOnlySet<WorldAssetKey> protectedAssets, long budget)
    {
        if (worldAssets.ContainsKey(asset.Key) || pendingWorld?.Source.Key == asset.Key)
            return true;
        if (pendingWorld is not null || asset.Bytes > budget)
            return false;
        if (ProposedWorldBytes(asset) > budget)
            foreach (var pair in worldAssets.Where(p => !protectedAssets.Contains(p.Key)).OrderBy(p => p.Value.Used).ToArray())
            {
                RemoveWorldAsset(pair.Key);
                if (ProposedWorldBytes(asset) <= budget)
                    break;
            }
        if (ProposedWorldBytes(asset) > budget)
            return false;
        if (asset.Layout.Kept.Exists(kept => kept.Rdr2 is not null) && !Rdr2Supported)
            throw new NotSupportedException("This GPU can't bind as many textures as Red Dead Redemption 2's material reads.");
        CreateWorldPipelines();
        ChargeWorldMemory(asset.MemoryCost);
        BeginWorldUpload(asset);
        return true;
    }

    /// <summary>
    /// What the streamer selected: the placements covered (drawn when in view and uploaded), the others kept for the
    /// time being. Placements change storage only when the coverage does.
    /// </summary>
    public void SetWorldSelection(WorldSelection selection)
    {
        worldClock++;
        // The same coverage is common while assets come in or the camera only turns.
        if (!worldSelection.Coverage.SequenceEqual(selection.Coverage, ReferenceEqualityComparer.Instance))
        {
            StartWorldFades(worldSelection.Coverage, selection.Coverage);
            worldSelection = selection;
            UpdateWorldCoverage();
        }
        worldSelection = selection;
        foreach (WorldLodNode node in selection.Coverage)
            if (worldAssets.TryGetValue(node.Asset, out WorldGpuAsset? asset))
                asset.Used = worldClock;
    }

    /// <summary>
    /// The room ambient of a placement inside an interior, as the world's index has it; null outside. Set before the
    /// first selection.
    /// </summary>
    public Func<WorldLodNode, InteriorAmbient?>? WorldAmbient
    {
        get => worldCull.Ambient;
        set => worldCull.Ambient = value;
    }

    // GTA's level of detail fades (light.wgsl's lod_fade_keeps): the placements that came into the coverage fade in, and
    // those that left it stay drawn for the fade, fading out, in the same moment, so a model and the ones replacing it
    // share their pixels as one gives way to the other. When each began on the light's clock, and which way; those
    // fading out, when they began; and how long a fade is, as the shaders have it.
    private readonly Dictionary<WorldLodNode, (float Start, float Direction)> worldFades = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldLodNode, double> worldFadingOut = new(ReferenceEqualityComparer.Instance);
    internal const double LodFadeSeconds = .4;

    // The fades' clock, in seconds since the renderer was made (light.wgsl's water.w): it doesn't wrap, so a fade done
    // stays done, and a float of it keeps to a hundredth of a second for days.
    private readonly System.Diagnostics.Stopwatch lodFadeClock = System.Diagnostics.Stopwatch.StartNew();

    private double lodFadeTime => lodFadeClock.Elapsed.TotalSeconds;

    /// <summary>The placements fading out of the coverage, still drawn.</summary>
    internal int WorldFadingOut => worldFadingOut.Count;

    // A coverage change's fades: none for the first coverage (nothing was shown to fade from), nor for grass, which
    // fades with distance of its own.
    private void StartWorldFades(IReadOnlyList<WorldLodNode> before, IReadOnlyList<WorldLodNode> after)
    {
        if (before.Count == 0)
            return;
        double now = lodFadeTime;
        var was = new HashSet<WorldLodNode>(before, ReferenceEqualityComparer.Instance);
        var kept = new HashSet<WorldLodNode>(after, ReferenceEqualityComparer.Instance);
        foreach (WorldLodNode node in after)
            if (!was.Contains(node) && !IsGrass(node))
            {
                worldFadingOut.Remove(node);
                worldFades[node] = ((float)now, 1);
                MarkFade(node);
            }
        foreach (WorldLodNode node in before)
            if (!kept.Contains(node) && !IsGrass(node) && worldAssets.ContainsKey(node.Asset))
            {
                worldFadingOut[node] = now;
                worldFades[node] = ((float)now, -1);
                MarkFade(node);
            }

        // Its asset's storage holds the node's rows with the fade they had.
        void MarkFade(WorldLodNode node)
        {
            if (worldAssets.TryGetValue(node.Asset, out WorldGpuAsset? asset))
                asset.FadeChanged = true;
        }

        static bool IsGrass(WorldLodNode node) => node.Grass is not null || node.Instances is [{ Grass: not null }, ..];
    }

    // The coverage drawn: the selection's, and those fading out of it while their fade lasts.
    private void UpdateWorldCoverage()
    {
        worldVisibilityDirty = worldSourcesDirty = worldNodeAssetsDirty = true;
        worldCull.Update(worldFadingOut.Count == 0 ? worldSelection.Coverage : [.. worldSelection.Coverage, .. worldFadingOut.Keys]);
    }

    // Those faded out go; those faded in need nothing more (their pixels are all drawn, and the clock doesn't wrap).
    private void EndWorldFades()
    {
        if (worldFades.Count == 0)
            return;
        double now = lodFadeTime;
        bool ended = false;
        foreach (var (node, (start, direction)) in worldFades.ToArray())
        {
            if (now - start < LodFadeSeconds)
                continue;
            worldFades.Remove(node);
            if (direction < 0 && worldFadingOut.Remove(node))
                ended = true;
        }
        if (ended)
            UpdateWorldCoverage();
    }

    // A placement's normal matrix with its level of detail fade in the lanes the normals don't read (WorldCullSet).
    private Matrix4x4 WithWorldFade(Matrix4x4 normal, WorldLodNode node)
    {
        if (normal.M43 > .5f || !worldFades.TryGetValue(node, out var fade))
            return normal;
        normal.M14 = fade.Start;
        normal.M24 = fade.Direction;
        return normal;
    }

    /// <summary>Lets go of every asset: another world, or none.</summary>
    public void ClearWorld()
    {
        CancelWorldUpload();
        worldFades.Clear();
        worldFadingOut.Clear();
        foreach (WorldAssetKey key in worldAssets.Keys.ToArray())
            RemoveWorldAsset(key);
        worldSelection = new WorldSelection([], []);
        worldCull.Update([]);
        localLights = [];
        listedAssets.Clear();
        worldBlendedDraws.Clear();
        worldWaterDraws.Clear();
        worldPicked = null;
        WorldDrawnCount = 0;
        worldNodeAssetsDirty = worldSourcesDirty = worldVisibilityDirty = true;
        foreach (WorldBufferPages? pages in new[] { worldVertexPages, worldIndexPages, worldStoragePages, worldUniformPages })
            pages?.Clear();
        ClearWorldSky();
        ClearRdr2Sky();
        ClearWorldLensFlare();
        ClearWorldCloudHats();
        ClearWorldCoronas();
    }

    private void RemoveWorldAsset(WorldAssetKey key)
    {
        if (!worldAssets.Remove(key, out WorldGpuAsset? asset))
            return;
        worldNodeAssetsDirty = worldSourcesDirty = worldVisibilityDirty = true;
        listedAssets.Remove(asset);
        ReleaseWorldMemory(asset.MemoryCost);
        ReleaseAssetBundles(asset);
        if (asset.IndirectFirst >= 0)
            worldArgumentAllocator.Free(asset.IndirectFirst, ArgumentSlots(asset));
        // Its ranges of the pages go back now.
        FreeSlice(VertexPages, asset.Vertices);
        FreeSlice(IndexPages, asset.Indices);
        FreeSlice(StoragePages, asset.SourceInstances);
        FreeSlice(VertexPages, asset.Instances);
        FreeSlice(VertexPages, asset.ShadowInstances);
        foreach (WorldSlice uniforms in asset.Meshes.Select(mesh => mesh.Uniforms).Distinct())
            UniformPages.Free(uniforms);
        foreach (object id in asset.Textures)
            if (worldTextures.TryGetValue(id, out WorldTexture? texture) && --texture.Users <= 0)
                ForgetWorldTexture(id);
    }
    private static void FreeSlice(WorldBufferPages pages, WorldSlice? slice)
    {
        if (slice is { } range)
            pages.Free(range);
    }

    private static void SetSlice(RenderPassEncoder pass, uint slot, WorldSlice slice) => pass.SetVertexBuffer(slot, slice.Buffer, slice.Offset, slice.Size);

    private static void SetIndexSlice(RenderPassEncoder pass, WorldSlice slice) => pass.SetIndexBuffer(slice.Buffer, IndexFormat.Uint32, slice.Offset, slice.Size);

    // A texture no asset binds any more, off the GPU.
    private void ForgetWorldTexture(object id)
    {
        if (worldTextures.Remove(id, out WorldTexture? texture))
            texture.Gpu?.Destroy();
    }

    // An asset's textures, as the material groups look them up, while its groups are made: the objects it read stand
    // for the textures up by what they are, and are let go after.
    private void LendWorldTextures(WorldAsset asset, IEnumerable<GameTexture> read)
    {
        foreach (GameTexture texture in read)
        {
            WorldTexture? up = worldTextures.GetValueOrDefault(asset.TextureId(texture));
            textures[texture] = up?.View;
            if (up?.Gpu is { } gpu)
                uploadedOf[texture] = gpu;
            if (up?.Srgb is { } srgb)
                srgbViews[texture] = srgb;
        }
    }

    private void ReturnWorldTextures(WorldAsset asset, IEnumerable<GameTexture> read)
    {
        foreach (GameTexture texture in read)
        {
            // An sRGB view made meanwhile (RDR2's colour slots) stays with the texture.
            if (srgbViews.Remove(texture, out TextureView? srgb) && worldTextures.GetValueOrDefault(asset.TextureId(texture)) is { Srgb: null } up)
                up.Srgb = srgb;
            textures.Remove(texture);
            uploadedOf.Remove(texture);
        }
    }

    // The covered placements' matrices into each asset's storage, when the coverage or the assets changed. Each covered
    // node keeps its rows while it stays covered: a node coming in takes a gap one going left, or rows at the end, and
    // only its rows, and those whose fade changed, are written and uploaded. An asset with a city's placements of one
    // tree or rock no longer rewrites all of them when one joins or leaves. Its storage is laid out again, without gaps,
    // once they are half of it.
    private void UpdateWorldSources()
    {
        WorldCullSet set = worldCull;
        if (worldNodeRow.Length < set.Nodes.Length)
            worldNodeRow = new int[set.Nodes.Length];
        foreach (WorldGpuAsset asset in worldAssets.Values)
            asset.NextNodes.Clear();
        for (int n = 0; n < set.Nodes.Length; n++)
            if (worldAssets.TryGetValue(set.Nodes[n].Asset, out WorldGpuAsset? asset))
                asset.NextNodes.Add(n);
        long seen = ++worldSourceUpdates;
        foreach (WorldGpuAsset asset in worldAssets.Values)
        {
            // The same nodes in the same order, written with the fades as they are: only the nodes are mapped again.
            if (asset.SourceRevision >= 0 && !asset.FadeChanged && SameNodes(asset))
            {
                for (int k = 0; k < asset.NextNodes.Count; k++)
                    worldNodeRow[asset.NextNodes[k]] = asset.SourceFirsts[k];
                continue;
            }
            worldSourceRanges.Clear();
            // Half of it gaps: laid out again from the start.
            if (asset.SourceRows > 64 && asset.SourceRows > 2 * asset.LiveSources)
            {
                asset.Slots.Clear();
                asset.Gaps.Clear();
                asset.Placements.Clear();
                asset.Sources.Truncate(0);
                asset.SourceRows = asset.LiveSources = asset.ReflectedSources = 0;
            }
            // Those still covered stay, their rows written again only for a fade that changed; the others come in.
            worldAddedNodes.Clear();
            foreach (int n in asset.NextNodes)
            {
                WorldLodNode node = set.Nodes[n];
                ref WorldSourceSlot slot = ref CollectionsMarshal.GetValueRefOrNullRef(asset.Slots, node);
                if (Unsafe.IsNullRef(ref slot))
                {
                    worldAddedNodes.Add(n);
                    continue;
                }
                slot.Seen = seen;
                var fade = worldFades.GetValueOrDefault(node);
                if (slot.Fade != fade)
                {
                    slot.Fade = fade;
                    WorldCullSet.PreparedNode block = set.Blocks[n];
                    for (int i = 0; i < slot.Count; i++)
                        asset.Sources.Set(slot.First + i, block.World[i], WithWorldFade(block.Normal[i], node));
                    worldSourceRanges.Add((slot.First, slot.Count));
                }
            }
            // Those no longer covered leave their rows as a gap.
            if (asset.Slots.Count > asset.NextNodes.Count - worldAddedNodes.Count)
            {
                worldRemovedNodes.Clear();
                foreach (var (node, slot) in asset.Slots)
                    if (slot.Seen != seen)
                        worldRemovedNodes.Add(node);
                foreach (WorldLodNode node in worldRemovedNodes)
                {
                    asset.Slots.Remove(node, out WorldSourceSlot slot);
                    asset.LiveSources -= slot.Count;
                    asset.ReflectedSources -= slot.Reflected;
                    for (int i = 0; i < slot.Count; i++)
                        asset.Placements[slot.First + i] = (null!, -1);
                    if (slot.Count > 0)
                        asset.Gaps.Add((slot.First, slot.Count));
                }
            }
            foreach (int n in worldAddedNodes)
            {
                WorldLodNode node = set.Nodes[n];
                WorldCullSet.PreparedNode block = set.Blocks[n];
                int first = TakeGap(asset, block.Count);
                var fade = worldFades.GetValueOrDefault(node);
                asset.Slots.Add(node, new WorldSourceSlot { First = first, Count = block.Count, Reflected = block.Reflected, Fade = fade, Seen = seen });
                while (asset.Placements.Count < first + block.Count)
                    asset.Placements.Add((null!, -1));
                for (int i = 0; i < block.Count; i++)
                {
                    asset.Sources.Set(first + i, block.World[i], WithWorldFade(block.Normal[i], node));
                    asset.Placements[first + i] = (node, block.Index[i]);
                }
                asset.LiveSources += block.Count;
                asset.ReflectedSources += block.Reflected;
                if (block.Count > 0)
                    worldSourceRanges.Add((first, block.Count));
            }
            // The nodes mapped to their rows, and the order kept for the next update.
            asset.SourceNodes.Clear();
            asset.SourceFirsts.Clear();
            foreach (int n in asset.NextNodes)
            {
                WorldLodNode node = set.Nodes[n];
                int first = asset.Slots[node].First;
                asset.SourceNodes.Add(node);
                asset.SourceFirsts.Add(first);
                worldNodeRow[n] = first;
            }
            int count = asset.SourceRows;
            bool replaced = count > asset.Capacity;
            if (replaced)
            {
                int capacity = Math.Max(16, count);
                ReleaseAssetBundles(asset);
                FreeSlice(StoragePages, asset.SourceInstances);
                FreeSlice(VertexPages, asset.Instances);
                SetInstanceCharge(asset, capacity, asset.ShadowCapacity);
                asset.Data.Reserve(capacity);
                WorldSlice sources = StoragePages.Allocate((ulong)capacity * 128);
                asset.SourceInstances = sources;
                asset.SourceGroup = device.CreateBindGroup(new BindGroupDescriptor
                {
                    Layout = worldInstanceLayout!,
                    Entries =
                    [
                        new BindGroupEntry { Binding = 0, Buffer = sources.Buffer, Offset = sources.Offset, Size = sources.Size },
                    ],
                });
                asset.Instances = VertexPages.Allocate((ulong)capacity * 4);
                asset.Rewrite = true;
                worldVisibilityDirty = true;
                // New storage holds nothing yet: all of it goes up.
                worldSourceRanges.Clear();
                if (count > 0)
                    worldSourceRanges.Add((0, count));
            }
            // Only the rows written, a run of adjacent ones at a time.
            worldSourceRanges.Sort();
            for (int r = 0; r < worldSourceRanges.Count;)
            {
                var (first, length) = worldSourceRanges[r++];
                while (r < worldSourceRanges.Count && worldSourceRanges[r].First <= first + length)
                {
                    length = Math.Max(length, worldSourceRanges[r].First + worldSourceRanges[r].Count - first);
                    r++;
                }
                queue.WriteBuffer(asset.SourceInstances!.Value.Buffer, asset.SourceInstances.Value.Offset + (ulong)first * 128,
                    (ReadOnlySpan<Matrix4x4>)asset.Sources.Matrices.AsSpan(first * 2, length * 2));
            }
            bool reflected = asset.ReflectedSources > 0;
            if (asset.Reflected != reflected)
            {
                asset.Reflected = reflected;
                ReleaseAssetBundles(asset);
            }
            asset.FadeChanged = false;
            asset.SourceRevision = ++worldSourceBuilds;
        }
        worldSourcesDirty = false;

        // Whether an asset's nodes this time are the ones its storage holds, in its order.
        bool SameNodes(WorldGpuAsset asset)
        {
            if (asset.NextNodes.Count != asset.SourceNodes.Count)
                return false;
            for (int i = 0; i < asset.NextNodes.Count; i++)
                if (!ReferenceEquals(set.Nodes[asset.NextNodes[i]], asset.SourceNodes[i]))
                    return false;
            return true;
        }
    }

    /// <summary>An uploaded asset's rows in use, gaps included, and those live; for tests.</summary>
    internal (int Rows, int Live) WorldSourceRows(WorldAssetKey key) =>
        worldAssets.TryGetValue(key, out WorldGpuAsset? asset) ? (asset.SourceRows, asset.LiveSources) : default;

    /// <summary>The row a covered node's first placement is in, and its normal matrix there; for tests.</summary>
    internal (int Row, Matrix4x4 Normal) WorldSourceRow(WorldLodNode node)
    {
        WorldGpuAsset asset = worldAssets[node.Asset];
        int row = asset.Slots[node].First;
        return (row, asset.Sources.Matrices[row * 2 + 1]);
    }

    /// <summary>
    /// Whether every covered placement's entry reads its own world matrix from its asset's storage, and its rows name
    /// it; for tests.
    /// </summary>
    internal bool WorldSourcesMatchCoverage()
    {
        WorldCullSet set = worldCull;
        for (int n = 0; n < set.Nodes.Length; n++)
        {
            if (!worldAssets.TryGetValue(set.Nodes[n].Asset, out WorldGpuAsset? asset))
                continue;
            WorldCullSet.PreparedNode block = set.Blocks[n];
            for (int i = 0; i < block.Count; i++)
            {
                uint row = (uint)(worldNodeRow[n] + i);
                if (asset.Sources.Matrices[row * 2] != block.World[i] || !ReferenceEquals(asset.Placements[(int)row].Node, set.Nodes[n]))
                    return false;
            }
        }
        return true;
    }

    // Rows for a node coming in: the first gap it fits, what it leaves of it kept, or rows at the end.
    private static int TakeGap(WorldGpuAsset asset, int count)
    {
        if (count == 0)
            return 0;
        for (int g = 0; g < asset.Gaps.Count; g++)
        {
            var (first, length) = asset.Gaps[g];
            if (length < count)
                continue;
            if (length == count)
                asset.Gaps.RemoveAt(g);
            else
                asset.Gaps[g] = (first + count, length - count);
            return first;
        }
        int end = asset.SourceRows;
        asset.SourceRows += count;
        return end;
    }

    // Scratch for the updates above.
    private readonly List<(int First, int Count)> worldSourceRanges = [];
    private readonly List<int> worldAddedNodes = [];
    private readonly List<WorldLodNode> worldRemovedNodes = [];
    private long worldSourceUpdates;

    // An asset's charge with its placements' storage at these capacities.
    private void SetInstanceCharge(WorldGpuAsset asset, int capacity, int shadowCapacity)
    {
        long before = InstanceBytes(asset);
        asset.Capacity = capacity;
        asset.ShadowCapacity = shadowCapacity;
        long change = InstanceBytes(asset) - before;
        if (change == 0)
            return;
        worldOwnedBytes += change;
        asset.MemoryCost = new WorldMemoryCost(asset.MemoryCost.OwnedBytes + change, asset.MemoryCost.Shared);
    }

    // Which covered placements the camera and each due cascade see, by asset and level of detail: only when the view,
    // the coverage, the assets or the cascades changed.

    private void UpdateWorldVisibility(ShadowCascade[] cascades, bool shadows)
    {
        EndWorldFades();
        if (worldSourcesDirty)
            UpdateWorldSources();
        Vector3 eye = Camera.Eye, forward = Camera.Forward;
        var view = (eye, forward, width, height, key.Direction, shadows);
        // A cascade due without its casters' lists forces an update that culls it.
        for (int c = 0; c < WorldShadowCascades.Count; c++)
            if (shadows && shadowRenderNow[c] && !cascadeListsValid[c])
                worldVisibilityDirty = true;
        if (!worldVisibilityDirty && worldView == view)
            return;
        worldView = view;
        worldVisibilityDirty = false;
        WorldCullSet set = worldCull;
        // Which uploaded asset each covered node draws: that changes with the coverage and the uploads, not the view.
        if (worldNodeAssetsDirty)
        {
            if (worldNodeAsset.Length < set.Nodes.Length)
            {
                worldNodeAsset = new WorldGpuAsset?[set.Nodes.Length];
                worldNodeCasts = new bool[set.Nodes.Length];
            }
            for (int n = 0; n < set.Nodes.Length; n++)
            {
                WorldGpuAsset? resident = worldAssets.GetValueOrDefault(set.Nodes[n].Asset);
                worldNodeAsset[n] = resident;
                worldNodeCasts[n] = resident is { CastsShadows: true };
            }
            worldNodeAssetsDirty = false;
        }
        var frustum = new CameraFrustum(eye, forward, Aspect);
        // The camera's frustum and each cascade are volumes of their own, culled at once. Casters are culled against
        // their cascade's light volume, not the camera: what's behind or beside the view still shadows what's in it.
        // Only the cascades rendered this frame are culled; the others hold no lists until they're due again.
        int volumes = shadows ? 1 + cascades.Length : 1;
        for (int c = 0; c < WorldShadowCascades.Count; c++)
            cascadeListsValid[c] = shadows && shadowRenderNow[c];
        Parallel.For(0, volumes, volume =>
        {
            List<int> output = worldVisible[volume];
            output.Clear();
            if (volume > 0 && !cascadeListsValid[volume - 1])
                return;
            for (int n = 0; n < set.Nodes.Length; n++)
            {
                WorldLodNode node = set.Nodes[n];
                WorldGpuAsset? asset = worldNodeAsset[n];
                if (asset is null || asset.Capacity == 0 || set.Count[n] == 0)
                    continue;
                if (volume == 0)
                {
                    if (frustum.IsVisible(node.Center, node.Radius))
                        WorldCulling.Camera(set, set.First[n], set.Count[n], eye, frustum, output);
                }
                else
                {
                    ShadowCascade cascade = cascades[volume - 1];
                    // What's fading out casts no longer: the models replacing it do.
                    if (worldNodeCasts[n] && cascade.Intersects(node.Center - eye, node.Radius) && !worldFadingOut.ContainsKey(node))
                        WorldCulling.Cascade(set, set.First[n], set.Count[n], eye, cascade, output);
                }
            }
        });
        // Only the assets listed last time hold ranges.
        foreach (WorldGpuAsset asset in listedAssets)
        {
            asset.Data.Begin();
            asset.ShadowData.Begin();
            asset.NeededLods = 0;
            asset.Listed = false;
            Array.Clear(asset.LodStart);
            Array.Clear(asset.LodCount);
            Array.Clear(asset.ShadowStart);
            Array.Clear(asset.ShadowCount);
        }
        listedAssets.Clear();
        Gather(worldVisible[0], -1);
        if (shadows)
            for (int c = 0; c < cascades.Length; c++)
                Gather(worldVisible[1 + c], c);
        int drawn = 0;
        worldBlendedDraws.Clear();
        worldWaterDraws.Clear();
        foreach (WorldGpuAsset asset in listedAssets)
        {
            TrackBundleLods(asset);
            if ((asset.Data.Changed || asset.Rewrite) && asset.Data.Count > 0)
            {
                queue.WriteBuffer(asset.Instances!.Value.Buffer, asset.Instances.Value.Offset, (ReadOnlySpan<uint>)asset.Data.Values.AsSpan(0, asset.Data.Count));
                asset.Rewrite = false;
            }
            if (asset.ShadowData.Count > asset.ShadowCapacity)
            {
                // Each cascade lists a covered placement once at most: room for the whole coverage in every cascade
                // keeps turning the camera from growing (and recording again) many assets at once.
                FreeSlice(VertexPages, asset.ShadowInstances);
                SetInstanceCharge(asset, asset.Capacity, Math.Max(16, Math.Max(asset.ShadowData.Count, asset.Capacity * WorldShadowCascades.Count)));
                asset.ShadowInstances = VertexPages.Allocate((ulong)asset.ShadowCapacity * 4);
                ReleaseAssetBundles(asset);
                asset.RewriteShadow = true;
            }
            if ((asset.ShadowData.Changed || asset.RewriteShadow) && asset.ShadowData.Count > 0)
            {
                queue.WriteBuffer(asset.ShadowInstances!.Value.Buffer, asset.ShadowInstances.Value.Offset, (ReadOnlySpan<uint>)asset.ShadowData.Values.AsSpan(0, asset.ShadowData.Count));
                asset.RewriteShadow = false;
            }
            drawn += asset.Data.Count;
            // Each blended surface once per placement in view, to be sorted by its distance.
            foreach (WorldMesh mesh in asset.Meshes)
                if (mesh.Blended)
                    for (int i = asset.LodStart[mesh.Level], end = i + asset.LodCount[mesh.Level]; i < end; i++)
                    {
                        Vector3 center = Vector3.Transform(mesh.Part.Center, asset.Sources.Matrices[asset.Data.Values[i] * 2]);
                        (mesh.Water ? worldWaterDraws : worldBlendedDraws).Add(new WorldBlendedDraw(asset, mesh, (uint)i, Vector3.DistanceSquared(center, eye)));
                    }
        }
        worldBlendedDraws.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        worldWaterDraws.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        WorldDrawnCount = drawn;
        UpdateWorldArguments();

        // Buckets a volume's entries by level of detail and puts them after each touched asset's camera list (cascade
        // -1) or shadow list.
        void Gather(List<int> entries, int cascade)
        {
            gatheredAssets.Clear();
            foreach (int entry in entries)
            {
                int node = set.Node[entry];
                WorldGpuAsset asset = worldNodeAsset[node]!;
                if (!asset.Gathered)
                {
                    asset.Gathered = true;
                    gatheredAssets.Add(asset);
                }
                if (!asset.Listed)
                {
                    asset.Listed = true;
                    listedAssets.Add(asset);
                }
                float x = set.X[entry] - eye.X, y = set.Y[entry] - eye.Y, z = set.Z[entry] - eye.Z;
                asset.Buckets[asset.Level(x * x + y * y + z * z)].Add((uint)(worldNodeRow[node] + entry - set.First[node]));
            }
            foreach (WorldGpuAsset asset in gatheredAssets)
            {
                asset.Gathered = false;
                if (cascade < 0)
                    Flush(asset, asset.Data, asset.LodStart, asset.LodCount, 0);
                else
                    Flush(asset, asset.ShadowData, asset.ShadowStart, asset.ShadowCount, cascade * WorldAsset.Lods);
            }
        }

        static void Flush(WorldGpuAsset asset, WorldInstanceIndices destination, int[] start, int[] count, int offset)
        {
            for (int level = 0; level < WorldAsset.Lods; level++)
            {
                WorldInstanceIndices bucket = asset.Buckets[level];
                start[offset + level] = destination.Count;
                count[offset + level] = bucket.Count;
                if (bucket.Count != 0)
                    asset.NeededLods |= (byte)(1 << level);
                for (int i = 0; i < bucket.Count; i++)
                    destination.Add(bucket.Values[i]);
                bucket.Begin();
            }
        }
    }
}

/// <summary>
/// Which world shaders draw a surface: RDR2's material, GTA V's surface.wgsl or terrain.wgsl, or either game's water
/// (rdr2_water.wgsl, gta_water.wgsl).
/// </summary>
internal enum WorldFamily
{
    Rdr2,
    Gta,
    GtaTerrain,
    Rdr2Water,
    GtaWater,
}
