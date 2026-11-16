using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using RedFury.World;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// A game's world (after RedFury's WebGpuRenderer.World): the streamed assets, each uploaded once and drawn once per
// placement in view with rdr2_world.wgsl. The covered placements' matrices stay on the GPU per asset; each frame the
// camera's cull only writes the indexes of those in view, grouped by the drawable level of detail each is at. The
// streamer (WorldStreamer) says what's covered and hands over the assets it read; this keeps them within the budget.
internal sealed unsafe partial class ViewportRenderer
{
    private sealed class WorldGpuAsset(WorldAsset source)
    {
        internal readonly WorldAssetKey Key = source.Key;
        internal readonly WorldMemoryCost MemoryCost = source.MemoryCost;
        internal readonly long Bytes = source.Bytes;
        internal readonly byte LodMask = source.LodMask;
        internal readonly float[] LodReach2 = source.LodReach2;

        // Where the asset's vertices are from: each placement's matrix is moved by it.
        internal readonly Matrix4x4 Local = Matrix4x4.CreateTranslation(source.Scene.Origin);
        internal Buffer? Vertices, Indices;
        internal readonly List<WorldMesh> Meshes = [];
        internal readonly List<GameTexture> Textures = [];

        // The covered placements' matrices (a storage buffer, and the group reading it) and the indexes of those in view.
        internal Buffer? SourceInstances, Instances;
        internal BindGroup? SourceGroup;
        internal readonly WorldInstanceBuffer Sources = new();
        internal long SourceRevision = -1;
        internal int Capacity;
        internal bool Reflected;
        internal readonly WorldInstanceIndices Data = new();

        // Whether the indexes go to the GPU whether or not they changed: a new buffer holds none.
        internal bool Rewrite;

        // The range of each level of detail's placements in Data, and each level's scratch while they're gathered.
        internal readonly int[] LodStart = new int[WorldAsset.Lods], LodCount = new int[WorldAsset.Lods];
        internal readonly WorldInstanceIndices[] Buckets = [new(), new(), new(), new()];
        internal bool Listed;

        // When the coverage last held it: the oldest go first when room is needed.
        internal long Used;

        internal int Level(float distance2) => WorldLodSelection.Level(LodMask, LodReach2, distance2);
    }

    private readonly record struct WorldMesh(SurfacePart Part, BindGroup Material, Buffer Uniforms, AlphaMode Mode, bool DoubleSided, bool Decal)
    {
        internal int Level => Part.Lod is >= 0 and < WorldAsset.Lods ? Part.Lod : 0;

        internal bool Opaque => Mode != AlphaMode.Blend;

        internal bool Blended => Mode == AlphaMode.Blend && !Decal;
    }

    // A blended surface of one placement in view, and its distance from the camera, squared: drawn far to near.
    private readonly record struct WorldBlendedDraw(WorldGpuAsset Asset, WorldMesh Mesh, uint Instance, float Distance);

    private readonly Dictionary<WorldAssetKey, WorldGpuAsset> worldAssets = [];
    private readonly Dictionary<GameTexture, int> worldTextureUsers = new(ReferenceEqualityComparer.Instance);
    private readonly WorldCullSet worldCull = new();
    private WorldSelection worldSelection = new([], []);
    private WorldGpuAsset?[] worldNodeAsset = [];
    private uint[] worldSourceIndex = [];
    private readonly List<int> worldVisible = [];
    private readonly List<WorldGpuAsset> listedAssets = [];
    private bool worldSourcesDirty = true, worldNodeAssetsDirty = true, worldVisibilityDirty = true;
    private long worldSourceRevision, worldClock;
    private (Vector3 Eye, Vector3 Forward, int Width, int Height) worldView;
    private BindGroupLayout? worldInstanceLayout;
    private RenderPipeline? worldSolid, worldUnculled, worldBlended, worldDecal;
    private readonly List<WorldBlendedDraw> worldBlendedDraws = [];
    private bool worldShown;

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

    /// <summary>The assets uploaded.</summary>
    public IReadOnlySet<WorldAssetKey> WorldResident => worldAssets.Keys.ToHashSet();

    /// <summary>Whether an asset is uploaded and can be drawn.</summary>
    public bool IsWorldReady(WorldAssetKey key) => worldAssets.ContainsKey(key);

    /// <summary>What an uploaded asset costs to keep; null when it isn't.</summary>
    public WorldMemoryCost? WorldAssetMemory(WorldAssetKey key) => worldAssets.TryGetValue(key, out WorldGpuAsset? asset) ? asset.MemoryCost : null;

    // Running totals (RedFury's WorldMemory, copied), so the budget's checks never add up every asset: shared textures
    // are counted once, at the largest size any holder said, for as long as one holds them.
    private long worldOwnedBytes, worldSharedBytes, worldInstanceBytes;
    private readonly Dictionary<long, (long Bytes, int Holders)> worldShared = [];

    /// <summary>What the uploaded assets cost, shared textures counted once, with their placements' storage.</summary>
    public long WorldBytes => worldOwnedBytes + worldSharedBytes + worldInstanceBytes;

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

    /// <summary>The assets uploaded.</summary>
    public int WorldAssetCount => worldAssets.Count;

    /// <summary>The placements drawn in the last frame.</summary>
    public int WorldDrawnCount { get; private set; }

    /// <summary>
    /// Uploads an asset the streamer read, within <paramref name="budget"/>: the least recently covered assets not in
    /// <paramref name="protectedAssets"/> go to make room. False when there isn't room even so.
    /// </summary>
    /// <exception cref="NotSupportedException">The GPU can't bind as many textures as RDR2's material reads.</exception>
    public bool AddWorldAsset(WorldAsset asset, IReadOnlySet<WorldAssetKey> protectedAssets, long budget)
    {
        if (worldAssets.ContainsKey(asset.Key))
            return true;
        if (asset.Bytes > budget)
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
        var uploaded = new WorldGpuAsset(asset);
        DrawableScene scene = asset.Scene;
        if (scene.Vertices.Length > 0 && scene.Indices.Length > 0)
        {
            uploaded.Vertices = Upload((ReadOnlySpan<SurfaceVertex>)scene.Vertices, SurfaceVertex.Size);
            uploaded.Indices = Upload((ReadOnlySpan<uint>)scene.Indices, sizeof(uint), BufferUsage.Index);
        }
        var groups = new Dictionary<int, (BindGroup Group, Buffer Uniforms)>();
        foreach (SurfacePart part in scene.Parts)
        {
            SurfaceMaterial material = scene.Materials[part.Material];
            // Water waits for the scene behind it to be copied; GTA V's materials aren't the world's.
            if (material.Rdr2 is not { Water: false } rdr2 || uploaded.Vertices is null)
                continue;
            if (!groups.TryGetValue(part.Material, out var group))
            {
                Buffer uniforms = device.CreateBuffer(new BufferDescriptor { Size = Rdr2Material.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
                queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<byte>)rdr2.Uniforms);
                group = (Rdr2Group(rdr2, uniforms), uniforms);
                groups.Add(part.Material, group);
                foreach (GameTexture? texture in rdr2.Textures)
                    if (texture is not null && !uploaded.Textures.Contains(texture))
                        uploaded.Textures.Add(texture);
            }
            uploaded.Meshes.Add(new WorldMesh(part, group.Group, group.Uniforms, material.Decal ? AlphaMode.Blend : material.AlphaMode, rdr2.DoubleSided, material.Decal));
        }
        foreach (GameTexture texture in uploaded.Textures)
            worldTextureUsers[texture] = worldTextureUsers.GetValueOrDefault(texture) + 1;
        uploaded.Used = worldClock;
        ChargeWorldMemory(uploaded.MemoryCost);
        worldAssets.Add(asset.Key, uploaded);
        worldNodeAssetsDirty = worldSourcesDirty = worldVisibilityDirty = true;
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
            worldSourceRevision++;
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
        foreach (WorldAssetKey key in worldAssets.Keys.ToArray())
            RemoveWorldAsset(key);
        worldSelection = new WorldSelection([], []);
        worldCull.Update([]);
        listedAssets.Clear();
        worldBlendedDraws.Clear();
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
        worldInstanceBytes -= asset.Capacity * 132L;
        // The GPU memory goes now, not whenever the handles are collected.
        asset.Vertices?.Destroy();
        asset.Indices?.Destroy();
        asset.SourceInstances?.Destroy();
        asset.Instances?.Destroy();
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

    private void CreateWorldPipelines()
    {
        if (worldInstanceLayout is not null)
            return;
        CreateRdr2Pipelines();
        worldInstanceLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 128 },
                },
            ],
        });
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = [sceneLayout, rdr2Layout!, lightLayout!, worldInstanceLayout],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = LitShader("rdr2_light.wgsl") + "\n" + Read("rdr2_terrain_blending.wgsl") + "\n" + Read("rdr2_material.wgsl") + "\n"
                + Read("rdr2_vertex.wgsl") + "\n" + Read("rdr2_world.wgsl"),
        }));
        // Decals are pulled towards the camera a little, as RedFury's are, over the surface they lie on.
        RenderPipeline Pipeline(CullMode cull, bool blend, bool decal = false) =>
            gpu.Pipeline($"rdr2-world/{cull}/{blend}/{decal}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = layout,
                Vertex = new VertexState
                {
                    Module = shader.Value,
                    EntryPoint = "vs_rdr2_world",
                    Buffers =
                    [
                        SurfaceVertexLayout(),
                        // Which of the asset's placements each instance is.
                        new VertexBufferLayout
                        {
                            ArrayStride = sizeof(uint),
                            StepMode = VertexStepMode.Instance,
                            Attributes = [new VertexAttribute { ShaderLocation = 8, Offset = 0, Format = VertexFormat.Uint32 }],
                        },
                    ],
                },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.CCW, CullMode = cull },
                Fragment = new FragmentState
                {
                    Module = shader.Value,
                    EntryPoint = "fs_rdr2_world",
                    Targets = new[]
                    {
                        new ColorTargetState
                        {
                            Format = HdrFormat,
                            Blend = blend ? new BlendState
                            {
                                Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            } : null,
                            WriteMask = ColorWriteMask.All,
                        },
                    },
                },
                DepthStencil = new DepthStencilState
                {
                    Format = DepthFormat,
                    DepthWriteEnabled = blend ? OptionalBool.False : OptionalBool.True,
                    DepthCompare = CompareFunction.GreaterEqual,
                    DepthBias = decal ? 2 : 0,
                    DepthBiasSlopeScale = decal ? 1 : 0,
                },
            }));
        worldSolid = Pipeline(CullMode.Back, blend: false);
        worldDecal = Pipeline(CullMode.None, blend: true, decal: true);
        worldUnculled = Pipeline(CullMode.None, blend: false);
        worldBlended = Pipeline(CullMode.None, blend: true);
    }

    // The covered placements' matrices into each asset's storage, when the coverage or the assets changed: an asset
    // whose placements are as they were writes nothing.
    private void UpdateWorldSources()
    {
        WorldCullSet set = worldCull;
        if (worldSourceIndex.Length < set.Pairs.Length / 2)
            worldSourceIndex = new uint[set.Pairs.Length / 2];
        foreach (WorldGpuAsset asset in worldAssets.Values)
            if (asset.SourceRevision != worldSourceRevision)
                asset.Sources.Begin();
        for (int n = 0; n < set.Nodes.Length; n++)
        {
            if (!worldAssets.TryGetValue(set.Nodes[n].Asset, out WorldGpuAsset? asset) || asset.SourceRevision == worldSourceRevision)
                continue;
            for (int entry = set.First[n], end = entry + set.Count[n]; entry < end; entry++)
            {
                worldSourceIndex[entry] = (uint)asset.Sources.Count;
                asset.Sources.Add(set.Pairs, entry, asset.Local);
            }
        }
        foreach (WorldGpuAsset asset in worldAssets.Values)
        {
            if (asset.SourceRevision == worldSourceRevision)
                continue;
            int count = asset.Sources.Count;
            bool replaced = count > asset.Capacity;
            if (replaced)
            {
                int capacity = Math.Max(16, count);
                asset.SourceInstances?.Destroy();
                asset.Instances?.Destroy();
                worldInstanceBytes += (capacity - asset.Capacity) * 132L;
                asset.Capacity = capacity;
                asset.Data.Reserve(capacity);
                asset.SourceInstances = device.CreateBuffer(new BufferDescriptor { Size = (ulong)capacity * 128, Usage = BufferUsage.Storage | BufferUsage.CopyDst });
                asset.SourceGroup = device.CreateBindGroup(new BindGroupDescriptor
                {
                    Layout = worldInstanceLayout!,
                    Entries = [new BindGroupEntry { Binding = 0, Buffer = asset.SourceInstances, Size = (ulong)capacity * 128 }],
                });
                asset.Instances = device.CreateBuffer(new BufferDescriptor { Size = (ulong)capacity * 4, Usage = BufferUsage.Vertex | BufferUsage.CopyDst });
            }
            if (count > 0 && (replaced || asset.Sources.Changed))
                queue.WriteBuffer(asset.SourceInstances!, 0, (ReadOnlySpan<Matrix4x4>)asset.Sources.Matrices.AsSpan(0, count * 2));
            bool reflected = false;
            for (int i = 0; i < count && !reflected; i++)
                reflected = asset.Sources.Matrices[i * 2].GetDeterminant() < 0;
            asset.Reflected = reflected;
            asset.SourceRevision = worldSourceRevision;
            if (replaced)
            {
                asset.Rewrite = true;
                worldVisibilityDirty = true;
            }
        }
        worldSourcesDirty = false;
    }

    // Which covered placements the camera sees, by asset and level of detail: only when the view, the coverage or the
    // assets changed.
    private void UpdateWorldVisibility()
    {
        if (worldSourcesDirty)
            UpdateWorldSources();
        Vector3 eye = Camera.Eye, forward = Camera.Forward;
        var view = (eye, forward, width, height);
        if (!worldVisibilityDirty && worldView == view)
            return;
        worldView = view;
        worldVisibilityDirty = false;
        WorldCullSet set = worldCull;
        // Which uploaded asset each covered node draws: that changes with the coverage and the uploads, not the view.
        if (worldNodeAssetsDirty)
        {
            if (worldNodeAsset.Length < set.Nodes.Length)
                worldNodeAsset = new WorldGpuAsset?[set.Nodes.Length];
            for (int n = 0; n < set.Nodes.Length; n++)
                worldNodeAsset[n] = worldAssets.GetValueOrDefault(set.Nodes[n].Asset);
            worldNodeAssetsDirty = false;
        }
        var frustum = new CameraFrustum(eye, forward, (float)width / Math.Max(height, 1));
        worldVisible.Clear();
        for (int n = 0; n < set.Nodes.Length; n++)
        {
            WorldLodNode node = set.Nodes[n];
            WorldGpuAsset? asset = worldNodeAsset[n];
            if (asset is null || asset.Capacity == 0 || set.Count[n] == 0 || !frustum.IsVisible(node.Center, node.Radius))
                continue;
            WorldCulling.Camera(set, set.First[n], set.Count[n], eye, frustum, worldVisible);
        }
        // Only the assets listed last time hold ranges.
        foreach (WorldGpuAsset asset in listedAssets)
        {
            asset.Data.Begin();
            asset.Listed = false;
            Array.Clear(asset.LodStart);
            Array.Clear(asset.LodCount);
        }
        listedAssets.Clear();
        foreach (int entry in worldVisible)
        {
            WorldGpuAsset asset = worldNodeAsset[set.Node[entry]]!;
            if (!asset.Listed)
            {
                asset.Listed = true;
                listedAssets.Add(asset);
            }
            float x = set.X[entry] - eye.X, y = set.Y[entry] - eye.Y, z = set.Z[entry] - eye.Z;
            asset.Buckets[asset.Level(x * x + y * y + z * z)].Add(worldSourceIndex[entry]);
        }
        int drawn = 0;
        worldBlendedDraws.Clear();
        foreach (WorldGpuAsset asset in listedAssets)
        {
            // Each level's indexes after the one before, and where they are.
            for (int level = 0; level < WorldAsset.Lods; level++)
            {
                WorldInstanceIndices bucket = asset.Buckets[level];
                asset.LodStart[level] = asset.Data.Count;
                asset.LodCount[level] = bucket.Count;
                for (int i = 0; i < bucket.Count; i++)
                    asset.Data.Add(bucket.Values[i]);
                bucket.Begin();
            }
            if ((asset.Data.Changed || asset.Rewrite) && asset.Data.Count > 0)
            {
                queue.WriteBuffer(asset.Instances!, 0, (ReadOnlySpan<uint>)asset.Data.Values.AsSpan(0, asset.Data.Count));
                asset.Rewrite = false;
            }
            drawn += asset.Data.Count;
            // Each blended surface once per placement, to be sorted by its distance.
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
    }

    /// <summary>
    /// The placements in view, as RedFury draws them: the solid surfaces, each instanced over its level's placements;
    /// the decals over them, the same way, as each lies on its own placement; then the blended surfaces one placement at
    /// a time, far to near.
    /// </summary>
    private void DrawWorld(RenderPassEncoder pass)
    {
        if (listedAssets.Count == 0 || worldInstanceLayout is null)
            return;
        RenderPipeline? current = null;
        WorldGpuAsset? boundAsset = null;
        void Use(RenderPipeline pipeline)
        {
            if (pipeline != current)
            {
                pass.SetPipeline(pipeline);
                current = pipeline;
            }
        }
        void Bind(WorldGpuAsset asset)
        {
            if (asset == boundAsset)
                return;
            pass.SetBindGroup(3, asset.SourceGroup!);
            pass.SetVertexBuffer(0, asset.Vertices!);
            pass.SetVertexBuffer(1, asset.Instances!);
            pass.SetIndexBuffer(asset.Indices!, IndexFormat.Uint32);
            boundAsset = asset;
        }
        for (int stage = 0; stage < 2; stage++)
            foreach (WorldGpuAsset asset in listedAssets)
            {
                if (asset.Vertices is null || asset.SourceGroup is null || asset.Data.Count == 0)
                    continue;
                foreach (WorldMesh mesh in asset.Meshes)
                {
                    if (stage == 0 ? !mesh.Opaque : !mesh.Decal)
                        continue;
                    uint count = (uint)asset.LodCount[mesh.Level];
                    if (count == 0)
                        continue;
                    // Cut-outs, two-sided surfaces and mirrored placements aren't culled, as RedFury has them.
                    Use(stage == 1 ? worldDecal! : mesh.Mode == AlphaMode.Mask || mesh.DoubleSided || asset.Reflected ? worldUnculled! : worldSolid!);
                    Bind(asset);
                    pass.SetBindGroup(1, mesh.Material);
                    pass.DrawIndexed(mesh.Part.IndexCount, count, mesh.Part.FirstIndex, mesh.Part.BaseVertex, (uint)asset.LodStart[mesh.Level]);
                }
            }
        BindGroup? boundMaterial = null;
        foreach (WorldBlendedDraw draw in worldBlendedDraws)
        {
            Use(worldBlended!);
            Bind(draw.Asset);
            if (draw.Mesh.Material != boundMaterial)
            {
                pass.SetBindGroup(1, draw.Mesh.Material);
                boundMaterial = draw.Mesh.Material;
            }
            pass.DrawIndexed(draw.Mesh.Part.IndexCount, 1, draw.Mesh.Part.FirstIndex, draw.Mesh.Part.BaseVertex, draw.Instance);
        }
    }
}
