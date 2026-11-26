using System.Numerics;
using HikariHopper.Rendering.World;
using RedFury.World;
using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;

namespace HikariHopper.Rendering;

/// <summary>A placement picked in the world: its asset, its node and which of its instances (-1 for none), and where it is.</summary>
internal sealed record WorldPick(WorldAssetKey Asset, WorldLodNode Node, int Instance, Matrix4x4 Transform)
{
    /// <summary>Where it stands in the world.</summary>
    public Vector3 Position => Transform.Translation;
}

// What's under the pointer in the world (Rdr2/world_pick.wgsl): on a click, every placement in view is drawn into a
// target of ids the frame's size, nearest first, and the pixel under the pointer read back; then the placement picked is
// outlined as the model viewer outlines its parts, its shape drawn into Scene/outline.wgsl's mask.
internal sealed unsafe partial class ViewportRenderer
{
    // Each asset's slot of picking ids, as far apart as uniform offsets must be.
    private const int PickSlot = 256;

    private RenderPipeline? worldMask;
    private readonly RenderPipeline?[] worldPick = new RenderPipeline?[WorldFamilies], worldMaskedPick = new RenderPipeline?[WorldFamilies];
    private BindGroupLayout? pickAssetLayout;
    private Texture? pickTexture, pickDepth;
    private Buffer? pickAssets, pickReadback, pickedInstance;
    private BindGroup? pickAssetGroup;
    private int pickAssetCapacity;

    // The placement outlined, and its matrix's place in its asset's storage, as last found.
    private WorldPick? worldPicked;
    private int pickedSource = -1;
    private long pickedRevision = -1;

    /// <summary>Outlines a placement; none outlines none.</summary>
    public void PickWorld(WorldPick? pick)
    {
        worldPicked = pick;
        pickedRevision = -1;
    }

    /// <summary>The placement outlined.</summary>
    internal WorldPick? WorldPicked => worldPicked;

    internal void CreatePickPipelines()
    {
        if (pickAssetLayout is not null)
            return;
        CreateWorldPipelines();
        pickAssetLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true, MinBindingSize = 16 },
                },
            ],
        });
        // The outline's mask needs no material.
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Label = "world/pick", Code = Module("world/pick"),
        }));
        var instance = WorldInstanceLayout();
        worldMask = gpu.Pipeline($"world-mask/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
            {
                BindGroupLayouts = [sceneLayout, worldEmptyLayout!, worldEmptyLayout!, worldInstanceLayout!],
            }),
            Vertex = new VertexState
            {
                Module = shader.Value,
                EntryPoint = "vs_world_mask",
                Buffers = [new VertexBufferLayout { ArrayStride = SurfaceVertex.Size, Attributes = new[] { new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 } } }, instance],
            },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState { Module = shader.Value, EntryPoint = "fs_world_mask", Targets = new[] { new ColorTargetState { Format = MaskFormat } } },
        }));
        pickReadback = device.CreateBuffer(new BufferDescriptor { Size = 256, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        pickedInstance = device.CreateBuffer(new BufferDescriptor { Size = 4, Usage = BufferUsage.Vertex | BufferUsage.CopyDst });
    }

    // A family's pipelines into the id target: everything solid, and cut-outs through their game's alpha test.
    internal void CreatePickFamily(WorldFamily family)
    {
        int f = (int)family;
        if (worldPick[f] is not null)
            return;
        CreatePickPipelines();
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Label = family == WorldFamily.Rdr2 ? "world/rdr2-pick" : "world/gta-pick",
            Code = Module(family == WorldFamily.Rdr2 ? "world/rdr2-pick" : "world/gta-pick"),
        }));
        var vertex = new VertexBufferLayout
        {
            ArrayStride = SurfaceVertex.Size,
            Attributes = new[]
            {
                new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                new VertexAttribute { ShaderLocation = 5, Offset = 56, Format = VertexFormat.Unorm8x4 },
            },
        };
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = [sceneLayout, WorldMaterialLayout(family), pickAssetLayout!, worldInstanceLayout!],
        });
        RenderPipeline Pick(string fragment) => gpu.Pipeline($"world-{family}-{fragment}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = layout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_world_pick", Buffers = [vertex, WorldInstanceLayout()] },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = fragment,
                Targets = new[] { new ColorTargetState { Format = TextureFormat.RG32Uint, WriteMask = ColorWriteMask.All } },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.GreaterEqual },
        }));
        worldPick[f] = Pick("fs_world_pick");
        // Terrain is never cut out.
        worldMaskedPick[f] = family == WorldFamily.GtaTerrain ? worldPick[f] : Pick("fs_world_pick_masked");
    }

    /// <summary>
    /// The placement under a pixel of the view, as the last frame drew it: the nearest solid surface there, cut-outs
    /// where their alpha keeps them, grass and blended surfaces left out; null for nothing.
    /// </summary>
    public WorldPick? PickWorldAt(int x, int y)
    {
        if (!worldShown || width <= 0 || height <= 0 || x < 0 || y < 0 || x >= width || y >= height || listedAssets.Count == 0)
            return null;
        CreatePickPipelines();
        if (pickTexture is null || pickTexture.GetWidth() != width || pickTexture.GetHeight() != height)
        {
            pickTexture?.Destroy();
            pickDepth?.Destroy();
            pickTexture = device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Dimension = TextureDimension.D2,
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = TextureFormat.RG32Uint,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            pickDepth = device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.D2,
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = DepthFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
        }
        // Each asset's number in a slot of its own.
        if (listedAssets.Count > pickAssetCapacity)
        {
            pickAssets?.Destroy();
            pickAssetCapacity = Math.Max(256, listedAssets.Count * 2);
            pickAssets = device.CreateBuffer(new BufferDescriptor { Size = (ulong)pickAssetCapacity * PickSlot, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
            pickAssetGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = pickAssetLayout!,
                Entries = [new BindGroupEntry { Binding = 0, Buffer = pickAssets, Size = 16 }],
            });
        }
        var slots = new uint[listedAssets.Count * PickSlot / 4];
        for (int i = 0; i < listedAssets.Count; i++)
            slots[i * PickSlot / 4] = (uint)listedAssets[i].Id;
        queue.WriteBuffer(pickAssets!, 0, (ReadOnlySpan<uint>)slots);

        CommandEncoder encoder = device.CreateCommandEncoder();
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = pickTexture.CreateView(), LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) }],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = pickDepth!.CreateView(),
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Discard,
                DepthClearValue = 0,
            },
        });
        pass.SetBindGroup(0, sceneGroup);
        // Only the pixel asked for is needed.
        pass.SetScissorRect((uint)x, (uint)y, 1, 1);
        for (int i = 0; i < listedAssets.Count; i++)
        {
            WorldGpuAsset asset = listedAssets[i];
            if (asset.Vertices is null || asset.SourceGroup is null || asset.Instances is null || asset.Data.Count == 0)
                continue;
            pass.SetBindGroup(2, pickAssetGroup!, (uint)(i * PickSlot));
            pass.SetBindGroup(3, asset.SourceGroup);
            SetSlice(pass, 0, asset.Vertices.Value);
            SetSlice(pass, 1, asset.Instances.Value);
            SetIndexSlice(pass, asset.Indices!.Value);
            foreach (WorldMesh mesh in asset.Meshes)
            {
                uint count = (uint)asset.LodCount[mesh.Level];
                if (!mesh.Opaque || count == 0)
                    continue;
                CreatePickFamily(mesh.Family);
                pass.SetPipeline(mesh.Mode == AlphaMode.Mask ? worldMaskedPick[(int)mesh.Family]! : worldPick[(int)mesh.Family]!);
                pass.SetBindGroup(1, mesh.Material);
                pass.DrawIndexed(mesh.Part.IndexCount, count, mesh.Part.FirstIndex, 0, (uint)asset.LodStart[mesh.Level]);
            }
        }
        pass.End();
        encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = pickTexture, Origin = new Origin3D((uint)x, (uint)y, 0) },
            new TexelCopyBufferInfo { Buffer = pickReadback!, Layout = new TexelCopyBufferLayout { BytesPerRow = 256, RowsPerImage = 1 } },
            new Extent3D(1, 1, 1));
        queue.Submit(encoder.Finish());
        pickReadback!.MapSync(MapMode.Read);
        uint[] picked = pickReadback.GetConstMappedRange<uint[], uint>(0, 2, span => span.ToArray());
        pickReadback.Unmap();
        uint id = picked[0], source = picked[1];
        if (id == 0 || worldAssets.Values.FirstOrDefault(asset => asset.Id == id) is not { } hit || source >= hit.Placements.Count)
            return null;
        var (node, instance) = hit.Placements[(int)source];
        Matrix4x4 transform = instance >= 0 ? node.Instances[instance].Transform : node.Transform;
        return new WorldPick(hit.Key, node, instance, transform);
    }

    /// <summary>The picked placement's outline over the frame, once the world is drawn and tone-mapped, when it's uploaded.</summary>
    private void DrawWorldOutline(CommandEncoder encoder)
    {
        if (worldPicked is not { } pick || worldMask is null || maskView is null || outlineGroup is null
            || !worldAssets.TryGetValue(pick.Asset, out WorldGpuAsset? asset) || asset.Vertices is null || asset.SourceGroup is null)
            return;
        // Where its matrix is, found again when the storage was laid out again.
        if (pickedRevision != asset.SourceRevision)
        {
            pickedSource = asset.Placements.FindIndex(p => ReferenceEquals(p.Node, pick.Node) && p.Instance == pick.Instance);
            pickedRevision = asset.SourceRevision;
            if (pickedSource >= 0)
                queue.WriteBuffer(pickedInstance!, 0, (ReadOnlySpan<uint>)[(uint)pickedSource]);
        }
        if (pickedSource < 0)
            return;
        Vector3 center = pick.Instance >= 0 ? pick.Node.Instances[pick.Instance].Center : pick.Node.Center;
        int level = asset.Level(Vector3.DistanceSquared(center, Camera.Eye));
        RenderPassEncoder masked = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = maskView, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) }],
        });
        masked.SetPipeline(worldMask);
        masked.SetBindGroup(0, sceneGroup);
        masked.SetBindGroup(1, worldEmptyGroup!);
        masked.SetBindGroup(2, worldEmptyGroup!);
        masked.SetBindGroup(3, asset.SourceGroup);
        masked.SetVertexBuffer(0, asset.Vertices.Value.Buffer, asset.Vertices.Value.Offset, asset.Vertices.Value.Size);
        masked.SetVertexBuffer(1, pickedInstance!);
        masked.SetIndexBuffer(asset.Indices!.Value.Buffer, IndexFormat.Uint32, asset.Indices.Value.Offset, asset.Indices.Value.Size);
        foreach (WorldMesh mesh in asset.Meshes)
            if (mesh.Level == level && !mesh.Decal)
                masked.DrawIndexed(mesh.Part.IndexCount, 1, mesh.Part.FirstIndex, 0, 0);
        masked.End();
        RenderPassEncoder outlined = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = frameView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        outlined.SetPipeline(outlinePipeline!);
        outlined.SetBindGroup(0, sceneGroup);
        outlined.SetBindGroup(1, outlineGroup);
        outlined.Draw(3);
        outlined.End();
    }
}
