using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// How the world is drawn (RedFury's WebGpuRenderer.WorldBundles, its indirect path): each asset's solid surfaces, its
// decals and its casters into each cascade are recorded once into render bundles that keep their bindings and read
// their instance counts from one shared buffer of indirect arguments, rewritten when the view changes. A bundle is
// recorded again only when what it binds changes, or it lacks a level of detail now in view. Blended surfaces keep
// their exact far-to-near order and are drawn as they are each frame. A GPU that can't start indirect draws past the
// first instance draws the same directly.
internal sealed unsafe partial class ViewportRenderer
{
    private const int OpaqueBundle = 0, DecalBundle = 1, ShadowBundle = 2;
    private const int WorldBundleCount = ShadowBundle + WorldShadowCascades.Count;

    // Recorded bundles keep levels of detail no longer drawn (zero-count draws) for this many updates before shrinking.
    private const int LodSurplusUpdates = 120;

    private Buffer? worldArgumentsBuffer;
    private WorldDrawArguments[] worldArguments = [];
    private readonly WorldArgumentAllocator worldArgumentAllocator = new();
    private readonly List<RenderBundle> executableBundles = [];
    private readonly List<WorldGpuAsset> directAssets = [];

    // Bundles recorded a frame at most: a burst of new assets or levels of detail is recorded over a few frames, the
    // assets waiting their turn drawn directly meanwhile, from the same counts.
    private const int RecordsPerFrame = 12;
    private int recordsThisFrame;

    // The light group the colour bundles were recorded with: a new one (a new shadow map) records them again.
    private BindGroup? bundledLightGroup;

    private bool IndirectWorld => gpu.IndirectFirstInstance && !DirectWorldDraws;

    /// <summary>Draws the world directly even where bundles could: for tests, and to measure what the bundles save.</summary>
    internal static bool DirectWorldDraws { get; set; }

    /// <summary>The world's draws in the last frame: into the shadow's cascades, and in the view (blended ones included).</summary>
    internal (int Shadow, int View) WorldDraws => (worldShadowDraws, worldViewDraws);

    private int worldShadowDraws, worldViewDraws;

    // The indirect arguments of each pass (the camera, then each cascade): a slot per surface, then one per depth run.
    private static int ArgumentSlots(WorldGpuAsset asset) => (asset.Meshes.Count + asset.DepthRuns.Count) * (1 + WorldShadowCascades.Count);

    private static int ArgumentIndex(WorldGpuAsset asset, int slot, int pass) => asset.IndirectFirst + slot * (1 + WorldShadowCascades.Count) + pass;

    // The placements a level draws in a pass: its slice of the camera's list or a cascade's.
    private static (uint First, uint Count) LevelRange(WorldGpuAsset asset, int level, int pass)
    {
        if (pass == 0)
            return ((uint)asset.LodStart[level], (uint)asset.LodCount[level]);
        int slot = (pass - 1) * WorldAsset.Lods + level;
        return ((uint)asset.ShadowStart[slot], (uint)asset.ShadowCount[slot]);
    }

    private static bool HasPassInstances(WorldGpuAsset asset, int bundle)
    {
        if (bundle < ShadowBundle)
            return asset.Data.Count != 0;
        int c = (bundle - ShadowBundle) * WorldAsset.Lods;
        return (asset.ShadowCount[c] | asset.ShadowCount[c + 1] | asset.ShadowCount[c + 2] | asset.ShadowCount[c + 3]) != 0;
    }

    private static void ReleaseAssetBundles(WorldGpuAsset asset)
    {
        Array.Clear(asset.Bundles);
        Array.Clear(asset.BundleLods);
    }

    // Bundles hold only the levels they were recorded with: recorded again at once when another is needed, and after a
    // long enough surplus.
    private static void TrackBundleLods(WorldGpuAsset asset)
    {
        if ((asset.LodMask & (asset.LodMask - 1)) == 0)
            return;
        byte recorded = 0, missing = 0;
        // Recorded ones, even those that came out empty (an asset without decals, say).
        for (int i = 0; i < asset.Bundles.Length; i++)
            if (asset.BundleLods[i] != 0)
            {
                recorded |= asset.BundleLods[i];
                missing |= (byte)(asset.NeededLods & ~asset.BundleLods[i]);
            }
        asset.SurplusUpdates = (recorded & ~asset.NeededLods) != 0 ? asset.SurplusUpdates + 1 : 0;
        if (missing != 0 || asset.SurplusUpdates > LodSurplusUpdates)
        {
            ReleaseAssetBundles(asset);
            asset.SurplusUpdates = 0;
        }
    }

    // Every listed asset's counts into the arguments; the buffer grows (and every bundle is recorded again) as needed.
    private void UpdateWorldArguments()
    {
        foreach (WorldGpuAsset asset in listedAssets)
            if (asset.IndirectFirst < 0)
                asset.IndirectFirst = worldArgumentAllocator.Allocate(Math.Max(1, ArgumentSlots(asset)));
        int length = Math.Max(1, worldArgumentAllocator.Length);
        if (length > worldArguments.Length)
            Array.Resize(ref worldArguments, Math.Max(length, Math.Max(65536, worldArguments.Length * 2)));
        foreach (WorldGpuAsset asset in listedAssets)
        {
            for (int i = 0; i < asset.Meshes.Count; i++)
                for (int pass = 0; pass <= WorldShadowCascades.Count; pass++)
                {
                    WorldMesh mesh = asset.Meshes[i];
                    var (first, count) = LevelRange(asset, mesh.Level, pass);
                    worldArguments[ArgumentIndex(asset, i, pass)] = new WorldDrawArguments(mesh.Part.IndexCount, count, mesh.Part.FirstIndex, 0, count == 0 ? 0 : first);
                }
            for (int r = 0; r < asset.DepthRuns.Count; r++)
                for (int pass = 0; pass <= WorldShadowCascades.Count; pass++)
                {
                    WorldDepthRun run = asset.DepthRuns[r];
                    var (first, count) = LevelRange(asset, run.Level, pass);
                    worldArguments[ArgumentIndex(asset, asset.Meshes.Count + r, pass)] = new WorldDrawArguments(run.IndexCount, count, run.FirstIndex, 0, count == 0 ? 0 : first);
                }
        }
        if (!IndirectWorld)
            return;
        if (worldArgumentsBuffer is null || (ulong)worldArguments.Length * 20 > worldArgumentsBuffer.GetSize())
        {
            // Bundles read this buffer, so a new one records them all again.
            worldArgumentsBuffer?.Destroy();
            worldArgumentsBuffer = device.CreateBuffer(new BufferDescriptor { Size = (ulong)worldArguments.Length * 20, Usage = BufferUsage.Indirect | BufferUsage.CopyDst });
            foreach (WorldGpuAsset asset in worldAssets.Values)
                ReleaseAssetBundles(asset);
        }
        queue.WriteBuffer(worldArgumentsBuffer, 0, (ReadOnlySpan<WorldDrawArguments>)worldArguments.AsSpan(0, length));
    }

    internal void CreateWorldPipelines()
    {
        if (worldInstanceLayout is not null)
            return;
        CreateRdr2Pipelines();
        // An asset's placements, and the world's local lights and their screen tiles, the same buffers in every asset's
        // group: they keep their size, so bundles keep reading them.
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
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = LocalLightFrame.LightBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = LocalLightFrame.TileBytes },
                },
            ],
        });
        localLightBuffer = device.CreateBuffer(new BufferDescriptor { Size = LocalLightFrame.LightBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDst });
        localLightTiles = device.CreateBuffer(new BufferDescriptor { Size = LocalLightFrame.TileBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDst });
        worldEmptyLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = [] });
        worldEmptyGroup = device.CreateBindGroup(new BindGroupDescriptor { Layout = worldEmptyLayout, Entries = [] });
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = [sceneLayout, rdr2Layout!, lightLayout!, worldInstanceLayout],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = LitShader("rdr2_light.wgsl") + "\n" + Read("rdr2_terrain_blending.wgsl") + "\n" + Read("rdr2_material.wgsl") + "\n"
                + Read("rdr2_vertex.wgsl") + "\n" + Read("rdr2_world.wgsl"),
        }));
        // Which of the asset's placements each instance is.
        static VertexBufferLayout InstanceLayout() => new()
        {
            ArrayStride = sizeof(uint),
            StepMode = VertexStepMode.Instance,
            Attributes = [new VertexAttribute { ShaderLocation = 8, Offset = 0, Format = VertexFormat.Uint32 }],
        };
        // Decals are pulled towards the camera a little, as RedFury's are, over the surface they lie on.
        RenderPipeline Pipeline(CullMode cull, bool blend, bool decal = false) =>
            gpu.Pipeline($"rdr2-world/{cull}/{blend}/{decal}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = layout,
                Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_rdr2_world", Buffers = [SurfaceVertexLayout(), InstanceLayout()] },
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
        worldUnculled = Pipeline(CullMode.None, blend: false);
        worldBlended = Pipeline(CullMode.None, blend: true);
        worldDecal = Pipeline(CullMode.None, blend: true, decal: true);

        // Into the shadow: solid casters' depth alone, cut-outs through their material's alpha. Both sides cast.
        Lazy<ShaderModule> shadowShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("rdr2_world_shadow.wgsl") }));
        var position = new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 };
        worldShadow = gpu.Pipeline("rdr2-world-shadow", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout!, worldEmptyLayout, worldEmptyLayout, worldInstanceLayout] }),
            Vertex = new VertexState
            {
                Module = shadowShader.Value,
                EntryPoint = "vs_world_shadow",
                Buffers = [new VertexBufferLayout { ArrayStride = SurfaceVertex.Size, Attributes = new[] { position } }, InstanceLayout()],
            },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            DepthStencil = CasterDepth(),
        }));
        worldMaskedShadow = gpu.Pipeline("rdr2-world-shadow-masked", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout!, rdr2Layout!, worldEmptyLayout, worldInstanceLayout] }),
            Vertex = new VertexState
            {
                Module = shadowShader.Value,
                EntryPoint = "vs_world_shadow_masked",
                Buffers =
                [
                    new VertexBufferLayout
                    {
                        ArrayStride = SurfaceVertex.Size,
                        Attributes = new[]
                        {
                            position,
                            new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                            new VertexAttribute { ShaderLocation = 5, Offset = 56, Format = VertexFormat.Unorm8x4 },
                        },
                    },
                    InstanceLayout(),
                ],
            },
            Fragment = new FragmentState { Module = shadowShader.Value, EntryPoint = "fs_world_shadow_masked", Targets = [] },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            DepthStencil = CasterDepth(),
        }));
    }

    // RedFury's casters' depth: less or equal, pushed back by 2 and 2.5 times the slope, so surfaces don't shadow
    // themselves.
    private static DepthStencilState CasterDepth() => new()
    {
        Format = DepthFormat,
        DepthWriteEnabled = OptionalBool.True,
        DepthCompare = CompareFunction.LessEqual,
        DepthBias = 2,
        DepthBiasSlopeScale = 2.5f,
    };

    // Where a world's draws go: a bundle being recorded, which reads its counts from the arguments, or a pass, which
    // draws the counts as they are.
    private interface IWorldTarget
    {
        void SetPipeline(RenderPipeline pipeline);

        void SetBindGroup(uint index, BindGroup group);

        void SetVertexBuffer(uint slot, Buffer buffer);

        void SetIndexBuffer(Buffer buffer);

        void Draw(int argument);
    }

    private readonly struct BundleTarget(RenderBundleEncoder encoder, Buffer arguments) : IWorldTarget
    {
        public void SetPipeline(RenderPipeline pipeline) => encoder.SetPipeline(pipeline);

        public void SetBindGroup(uint index, BindGroup group) => encoder.SetBindGroup(index, group);

        public void SetVertexBuffer(uint slot, Buffer buffer) => encoder.SetVertexBuffer(slot, buffer, 0);

        public void SetIndexBuffer(Buffer buffer) => encoder.SetIndexBuffer(buffer, IndexFormat.Uint32, 0);

        public void Draw(int argument) => encoder.DrawIndexedIndirect(arguments, (ulong)argument * 20);
    }

    private readonly struct PassTarget(RenderPassEncoder pass, WorldDrawArguments[] arguments) : IWorldTarget
    {
        public void SetPipeline(RenderPipeline pipeline) => pass.SetPipeline(pipeline);

        public void SetBindGroup(uint index, BindGroup group) => pass.SetBindGroup(index, group);

        public void SetVertexBuffer(uint slot, Buffer buffer) => pass.SetVertexBuffer(slot, buffer, 0);

        public void SetIndexBuffer(Buffer buffer) => pass.SetIndexBuffer(buffer, IndexFormat.Uint32, 0);

        public void Draw(int argument)
        {
            WorldDrawArguments draw = arguments[argument];
            if (draw.InstanceCount != 0)
                pass.DrawIndexed(draw.IndexCount, draw.InstanceCount, draw.FirstIndex, draw.BaseVertex, draw.FirstInstance);
        }
    }

    // An asset's draws for a bundle's pass, its levels limited to those in use; how many.
    private int EncodeWorldAsset<T>(T target, WorldGpuAsset asset, int bundle, byte levels) where T : IWorldTarget
    {
        if (asset.Vertices is null || asset.SourceGroup is null)
            return 0;
        bool shadow = bundle >= ShadowBundle;
        int pass = shadow ? 1 + bundle - ShadowBundle : 0;
        Buffer? instances = shadow ? asset.ShadowInstances : asset.Instances;
        if (instances is null)
            return 0;
        target.SetBindGroup(3, asset.SourceGroup);
        target.SetVertexBuffer(0, asset.Vertices);
        target.SetVertexBuffer(1, instances);
        target.SetIndexBuffer(asset.Indices!);
        int draws = 0;
        if (shadow)
        {
            target.SetPipeline(worldShadow!);
            target.SetBindGroup(1, worldEmptyGroup!);
            for (int r = 0; r < asset.DepthRuns.Count; r++)
                if ((levels >> asset.DepthRuns[r].Level & 1) != 0)
                {
                    target.Draw(ArgumentIndex(asset, asset.Meshes.Count + r, pass));
                    draws++;
                }
        }
        bool masked = false;
        for (int i = 0; i < asset.Meshes.Count; i++)
        {
            WorldMesh mesh = asset.Meshes[i];
            if ((levels >> mesh.Level & 1) == 0)
                continue;
            RenderPipeline pipeline;
            if (shadow)
            {
                if (mesh.Mode != AlphaMode.Mask)
                    continue;
                pipeline = worldMaskedShadow!;
                if (!masked)
                    target.SetPipeline(pipeline);
                masked = true;
            }
            else if (bundle == DecalBundle)
            {
                if (!mesh.Decal)
                    continue;
                target.SetPipeline(worldDecal!);
            }
            else
            {
                if (!mesh.Opaque)
                    continue;
                // Cut-outs, two-sided surfaces and mirrored placements aren't culled, as RedFury has them.
                target.SetPipeline(mesh.Mode == AlphaMode.Mask || mesh.DoubleSided || asset.Reflected ? worldUnculled! : worldSolid!);
            }
            target.SetBindGroup(1, mesh.Material);
            target.Draw(ArgumentIndex(asset, i, pass));
            draws++;
        }
        return draws;
    }

    // An asset's bundle for a pass, recorded with the levels of detail in use.
    private RenderBundle? RecordWorldBundle(WorldGpuAsset asset, int bundle)
    {
        bool shadow = bundle >= ShadowBundle;
        RenderBundleEncoder encoder = device.CreateRenderBundleEncoder(new RenderBundleEncoderDescriptor
        {
            ColorFormats = shadow ? [] : [HdrFormat],
            DepthStencilFormat = DepthFormat,
            SampleCount = 1,
        });
        var target = new BundleTarget(encoder, worldArgumentsBuffer!);
        if (shadow)
        {
            target.SetBindGroup(0, shadowGroups[bundle - ShadowBundle]);
            target.SetBindGroup(2, worldEmptyGroup!);
        }
        else
        {
            target.SetBindGroup(0, sceneGroup);
            target.SetBindGroup(2, lightGroup!);
        }
        asset.BundleLods[bundle] = asset.NeededLods;
        asset.BundleDraws[bundle] = EncodeWorldAsset(target, asset, bundle, asset.NeededLods);
        return asset.BundleDraws[bundle] == 0 ? null : encoder.Finish();
    }

    // A pass's world draws: each asset with placements in it, from its bundle (recorded first if it has none), or directly.
    private void DrawWorldPass(RenderPassEncoder pass, int bundle)
    {
        if (worldInstanceLayout is null)
            return;
        if (!IndirectWorld)
        {
            if (bundle >= ShadowBundle)
            {
                pass.SetBindGroup(0, shadowGroups[bundle - ShadowBundle]);
                pass.SetBindGroup(2, worldEmptyGroup!);
            }
            var direct = new PassTarget(pass, worldArguments);
            foreach (WorldGpuAsset asset in listedAssets)
                if (HasPassInstances(asset, bundle))
                    EncodeWorldAsset(direct, asset, bundle, 0xF);
            return;
        }
        if (bundle < ShadowBundle && !ReferenceEquals(bundledLightGroup, lightGroup))
        {
            // A new light group (a new shadow map): the colour bundles bound the old one.
            foreach (WorldGpuAsset asset in worldAssets.Values)
            {
                asset.Bundles[OpaqueBundle] = asset.Bundles[DecalBundle] = null;
                asset.BundleLods[OpaqueBundle] = asset.BundleLods[DecalBundle] = 0;
            }
            bundledLightGroup = lightGroup;
        }
        executableBundles.Clear();
        directAssets.Clear();
        foreach (WorldGpuAsset asset in listedAssets)
        {
            if (!HasPassInstances(asset, bundle))
                continue;
            if (asset.Bundles[bundle] is null && asset.BundleLods[bundle] == 0)
            {
                if (recordsThisFrame >= RecordsPerFrame)
                {
                    directAssets.Add(asset);
                    continue;
                }
                asset.Bundles[bundle] = RecordWorldBundle(asset, bundle);
                recordsThisFrame++;
            }
            if (asset.Bundles[bundle] is { } recorded)
            {
                executableBundles.Add(recorded);
                if (bundle >= ShadowBundle)
                    worldShadowDraws += asset.BundleDraws[bundle];
                else
                    worldViewDraws += asset.BundleDraws[bundle];
            }
        }
        if (executableBundles.Count != 0)
            pass.ExecuteBundles(CollectionsMarshal.AsSpan(executableBundles));
        if (directAssets.Count == 0)
            return;
        // Bundles leave the pass's bindings cleared.
        if (bundle >= ShadowBundle)
        {
            pass.SetBindGroup(0, shadowGroups[bundle - ShadowBundle]);
            pass.SetBindGroup(2, worldEmptyGroup!);
        }
        else
        {
            pass.SetBindGroup(0, sceneGroup);
            pass.SetBindGroup(2, lightGroup!);
        }
        var waiting = new PassTarget(pass, worldArguments);
        foreach (WorldGpuAsset asset in directAssets)
            EncodeWorldAsset(waiting, asset, bundle, 0xF);
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
        DrawWorldPass(pass, OpaqueBundle);
        DrawWorldPass(pass, DecalBundle);
        worldViewDraws += worldBlendedDraws.Count;
        // Bundles leave the pass's bindings cleared.
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(2, lightGroup!);
        RenderPipeline? current = null;
        WorldGpuAsset? boundAsset = null;
        BindGroup? boundMaterial = null;
        foreach (WorldBlendedDraw draw in worldBlendedDraws)
        {
            if (current is null)
            {
                pass.SetPipeline(worldBlended!);
                current = worldBlended;
            }
            if (draw.Asset != boundAsset)
            {
                pass.SetBindGroup(3, draw.Asset.SourceGroup!);
                pass.SetVertexBuffer(0, draw.Asset.Vertices!);
                pass.SetVertexBuffer(1, draw.Asset.Instances!);
                pass.SetIndexBuffer(draw.Asset.Indices!, IndexFormat.Uint32);
                boundAsset = draw.Asset;
            }
            if (draw.Mesh.Material != boundMaterial)
            {
                pass.SetBindGroup(1, draw.Mesh.Material);
                boundMaterial = draw.Mesh.Material;
            }
            pass.DrawIndexed(draw.Mesh.Part.IndexCount, 1, draw.Mesh.Part.FirstIndex, 0, draw.Instance);
        }
    }
}
