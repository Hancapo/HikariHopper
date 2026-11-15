using System.Numerics;
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

        internal Buffer? Vertices, Indices;

        // Its surfaces, laid out by level of detail with each level's solid ones together, and those runs of solid
        // surfaces, each drawn as one in the shadow.
        internal readonly List<WorldMesh> Meshes = [];
        internal readonly List<WorldDepthRun> DepthRuns = [];
        internal readonly List<GameTexture> Textures = [];
        internal bool CastsShadows;

        // The covered placements' matrices (a storage buffer, and the group reading it), and the indexes of those the
        // camera sees and of those casting into each cascade.
        internal Buffer? SourceInstances, Instances, ShadowInstances;
        internal BindGroup? SourceGroup;
        internal readonly WorldInstanceBuffer Sources = new();

        // Which placement each of Sources is: its node, and which of its instances (-1 for a node without any).
        internal readonly List<(WorldLodNode Node, int Instance)> Placements = [];

        // The nodes its storage holds, in order, and this coverage's (by their index in the cull set).
        internal readonly List<WorldLodNode> SourceNodes = [];
        internal readonly List<int> NextNodes = [];

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

    // A surface of an asset: its part (with indexes into the asset's vertices as they are), and how it's drawn.
    private readonly record struct WorldMesh(SurfacePart Part, BindGroup Material, Buffer Uniforms, AlphaMode Mode, bool DoubleSided, bool Decal)
    {
        internal int Level => Part.Lod is >= 0 and < WorldAsset.Lods ? Part.Lod : 0;

        internal bool Opaque => Mode != AlphaMode.Blend;

        internal bool Blended => Mode == AlphaMode.Blend && !Decal;

        // Solid without a cut-out: casts without its material, beside the others of its level.
        internal bool Caster => Mode == AlphaMode.Opaque && !Decal;
    }

    // Adjacent solid surfaces of a level, cast into the shadow as one draw.
    private readonly record struct WorldDepthRun(int Level, uint FirstIndex, uint IndexCount);

    // A blended surface of one placement in view, and its distance from the camera, squared: drawn far to near.
    private readonly record struct WorldBlendedDraw(WorldGpuAsset Asset, WorldMesh Mesh, uint Instance, float Distance);

    private readonly Dictionary<WorldAssetKey, WorldGpuAsset> worldAssets = [];
    private readonly Dictionary<GameTexture, int> worldTextureUsers = new(ReferenceEqualityComparer.Instance);
    private readonly WorldCullSet worldCull = new();
    private WorldSelection worldSelection = new([], []);
    private WorldGpuAsset?[] worldNodeAsset = [];
    private bool[] worldNodeCasts = [];
    private uint[] worldSourceIndex = [];

    // What each volume sees: the camera, then each cascade.
    private readonly List<int>[] worldVisible = [.. Enumerable.Range(0, 1 + WorldShadowCascades.Count).Select(_ => new List<int>())];
    private readonly List<WorldGpuAsset> listedAssets = [], gatheredAssets = [];
    private bool worldSourcesDirty = true, worldNodeAssetsDirty = true, worldVisibilityDirty = true;
    private long worldSourceBuilds, worldClock;
    private (Vector3 Eye, Vector3 Forward, int Width, int Height, Vector3 Sun, bool Shadows) worldView;
    private BindGroupLayout? worldInstanceLayout, worldEmptyLayout;
    private BindGroup? worldEmptyGroup;
    private RenderPipeline? worldSolid, worldUnculled, worldBlended, worldDecal, worldShadow, worldMaskedShadow;
    private readonly List<WorldBlendedDraw> worldBlendedDraws = [];
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
                ClearWorld();
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
        if (!Rdr2Supported)
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
            worldVisibilityDirty = worldSourcesDirty = worldNodeAssetsDirty = true;
            worldCull.Update(selection.Coverage);
        }
        worldSelection = selection;
        foreach (WorldLodNode node in selection.Coverage)
            if (worldAssets.TryGetValue(node.Asset, out WorldGpuAsset? asset))
                asset.Used = worldClock;
    }

    /// <summary>Lets go of every asset: another world, or none.</summary>
    public void ClearWorld()
    {
        CancelWorldUpload();
        foreach (WorldAssetKey key in worldAssets.Keys.ToArray())
            RemoveWorldAsset(key);
        worldSelection = new WorldSelection([], []);
        worldCull.Update([]);
        listedAssets.Clear();
        worldBlendedDraws.Clear();
        worldPicked = null;
        WorldDrawnCount = 0;
        worldNodeAssetsDirty = worldSourcesDirty = worldVisibilityDirty = true;
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
        // The GPU memory goes now, not whenever the handles are collected.
        asset.Vertices?.Destroy();
        asset.Indices?.Destroy();
        asset.SourceInstances?.Destroy();
        asset.Instances?.Destroy();
        asset.ShadowInstances?.Destroy();
        foreach (Buffer uniforms in asset.Meshes.Select(mesh => mesh.Uniforms).Distinct())
            uniforms.Destroy();
        foreach (GameTexture texture in asset.Textures)
        {
            int users = worldTextureUsers[texture] - 1;
            if (users > 0)
            {
                worldTextureUsers[texture] = users;
                continue;
            }
            worldTextureUsers.Remove(texture);
            ForgetTexture(texture);
        }
    }

    // A texture no asset binds any more, off the GPU.
    private void ForgetTexture(GameTexture texture)
    {
        textures.Remove(texture);
        srgbViews.Remove(texture);
        if (uploadedOf.Remove(texture, out WebGpuSharp.Texture? gpuTexture))
        {
            uploaded.Remove(gpuTexture);
            gpuTexture.Destroy();
        }
    }

    // The covered placements' matrices into each asset's storage, when the coverage or the assets changed. An asset
    // whose nodes came back as they were, in the same order, keeps its storage and only maps the entries to it again:
    // a coverage change touches only the assets whose placements changed.
    private void UpdateWorldSources()
    {
        WorldCullSet set = worldCull;
        if (worldSourceIndex.Length < set.X.Length)
            worldSourceIndex = new uint[set.X.Length];
        foreach (WorldGpuAsset asset in worldAssets.Values)
            asset.NextNodes.Clear();
        for (int n = 0; n < set.Nodes.Length; n++)
            if (worldAssets.TryGetValue(set.Nodes[n].Asset, out WorldGpuAsset? asset))
                asset.NextNodes.Add(n);
        foreach (WorldGpuAsset asset in worldAssets.Values)
        {
            if (asset.SourceRevision >= 0 && SameNodes(asset))
            {
                uint next = 0;
                foreach (int n in asset.NextNodes)
                    for (int entry = set.First[n], end = entry + set.Count[n]; entry < end; entry++)
                        worldSourceIndex[entry] = next++;
                continue;
            }
            asset.Sources.Begin();
            asset.Placements.Clear();
            asset.SourceNodes.Clear();
            foreach (int n in asset.NextNodes)
            {
                WorldLodNode node = set.Nodes[n];
                WorldCullSet.PreparedNode block = set.Blocks[n];
                asset.SourceNodes.Add(node);
                for (int i = 0, entry = set.First[n]; i < block.Count; i++, entry++)
                {
                    worldSourceIndex[entry] = (uint)asset.Sources.Count;
                    asset.Sources.Add(block.World[i], block.Normal[i]);
                    asset.Placements.Add((node, block.Index[i]));
                }
            }
            int count = asset.Sources.Count;
            bool replaced = count > asset.Capacity;
            if (replaced)
            {
                int capacity = Math.Max(16, count);
                ReleaseAssetBundles(asset);
                asset.SourceInstances?.Destroy();
                asset.Instances?.Destroy();
                SetInstanceCharge(asset, capacity, asset.ShadowCapacity);
                asset.Data.Reserve(capacity);
                asset.SourceInstances = device.CreateBuffer(new BufferDescriptor { Size = (ulong)capacity * 128, Usage = BufferUsage.Storage | BufferUsage.CopyDst });
                asset.SourceGroup = device.CreateBindGroup(new BindGroupDescriptor
                {
                    Layout = worldInstanceLayout!,
                    Entries =
                    [
                        new BindGroupEntry { Binding = 0, Buffer = asset.SourceInstances, Size = (ulong)capacity * 128 },
                        new BindGroupEntry { Binding = 1, Buffer = localLightBuffer!, Size = LocalLightFrame.LightBytes },
                        new BindGroupEntry { Binding = 2, Buffer = localLightTiles!, Size = LocalLightFrame.TileBytes },
                    ],
                });
                asset.Instances = device.CreateBuffer(new BufferDescriptor { Size = (ulong)capacity * 4, Usage = BufferUsage.Vertex | BufferUsage.CopyDst });
                asset.Rewrite = true;
                worldVisibilityDirty = true;
            }
            if (count > 0 && (replaced || asset.Sources.Changed))
                queue.WriteBuffer(asset.SourceInstances!, 0, (ReadOnlySpan<Matrix4x4>)asset.Sources.Matrices.AsSpan(0, count * 2));
            bool reflected = false;
            for (int i = 0; i < count && !reflected; i++)
                reflected = asset.Sources.Matrices[i * 2].GetDeterminant() < 0;
            if (asset.Reflected != reflected)
            {
                asset.Reflected = reflected;
                ReleaseAssetBundles(asset);
            }
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
                    if (worldNodeCasts[n] && cascade.Intersects(node.Center - eye, node.Radius))
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
        foreach (WorldGpuAsset asset in listedAssets)
        {
            TrackBundleLods(asset);
            if ((asset.Data.Changed || asset.Rewrite) && asset.Data.Count > 0)
            {
                queue.WriteBuffer(asset.Instances!, 0, (ReadOnlySpan<uint>)asset.Data.Values.AsSpan(0, asset.Data.Count));
                asset.Rewrite = false;
            }
            if (asset.ShadowData.Count > asset.ShadowCapacity)
            {
                // Each cascade lists a covered placement once at most: room for the whole coverage in every cascade
                // keeps turning the camera from growing (and recording again) many assets at once.
                asset.ShadowInstances?.Destroy();
                SetInstanceCharge(asset, asset.Capacity, Math.Max(16, Math.Max(asset.ShadowData.Count, asset.Capacity * WorldShadowCascades.Count)));
                asset.ShadowInstances = device.CreateBuffer(new BufferDescriptor { Size = (ulong)asset.ShadowCapacity * 4, Usage = BufferUsage.Vertex | BufferUsage.CopyDst });
                ReleaseAssetBundles(asset);
                asset.RewriteShadow = true;
            }
            if ((asset.ShadowData.Changed || asset.RewriteShadow) && asset.ShadowData.Count > 0)
            {
                queue.WriteBuffer(asset.ShadowInstances!, 0, (ReadOnlySpan<uint>)asset.ShadowData.Values.AsSpan(0, asset.ShadowData.Count));
                asset.RewriteShadow = false;
            }
            drawn += asset.Data.Count;
            // Each blended surface once per placement in view, to be sorted by its distance.
            foreach (WorldMesh mesh in asset.Meshes)
                if (mesh.Blended)
                    for (int i = asset.LodStart[mesh.Level], end = i + asset.LodCount[mesh.Level]; i < end; i++)
                    {
                        Vector3 center = Vector3.Transform(mesh.Part.Center, asset.Sources.Matrices[asset.Data.Values[i] * 2]);
                        worldBlendedDraws.Add(new WorldBlendedDraw(asset, mesh, (uint)i, Vector3.DistanceSquared(center, eye)));
                    }
        }
        worldBlendedDraws.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        WorldDrawnCount = drawn;
        UpdateWorldArguments();

        // Buckets a volume's entries by level of detail and puts them after each touched asset's camera list (cascade
        // -1) or shadow list.
        void Gather(List<int> entries, int cascade)
        {
            gatheredAssets.Clear();
            foreach (int entry in entries)
            {
                WorldGpuAsset asset = worldNodeAsset[set.Node[entry]]!;
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
                asset.Buckets[asset.Level(x * x + y * y + z * z)].Add(worldSourceIndex[entry]);
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
