using System.Numerics;
using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// GTA V's cloud hats (Gta5/cloud_hats.wgsl) over its sky, as the game draws them each frame: after
// the world's solid surfaces and its water, before its other blended surfaces, as the game draws them just after its
// sky, which comes after its water and before its alpha. Depth-tested against the world and not written; the soft
// ones read the scene's depth, which is the water's copy of it (or one made for them when no water is in view). Only
// with a world's sky, whose fog they take.
internal sealed unsafe partial class ViewportRenderer
{
    // A geometry of a layer, on the GPU: its buffers, its constants and its group.
    private sealed record CloudHatDraw(Buffer Vertices, Buffer Indices, uint IndexCount, Buffer Uniforms, BindGroup Group);

    private sealed record WorldCloudHatResources(GtaCloudHats Hats, CloudHatDraw[] Draws, HikariHopper.Gpu.Texture[] Textures);

    private WorldCloudHatResources? worldCloudHats;
    private BindGroupLayout? cloudHatLayout, cloudHatDepthLayout;
    private RenderPipeline? cloudHatPipeline;
    private Sampler? cloudHatClampAlong, cloudHatWrap;
    private BindGroup? cloudHatDepthGroup;
    private TextureView? cloudHatDepthOf;
    private CloudHatAnimation cloudHatAnimation = new();
    private double cloudHatClock = double.NaN;
    private Vector4[] cloudHatValues = [];
    private bool[] cloudHatDrawn = [];

    // A vertex: place, normal, colour, coordinates, tangent (GtaCloudHatGeometry.Floats floats).
    private const uint CloudHatStride = GtaCloudHatGeometry.Floats * sizeof(float);

    /// <summary>Whether a world's cloud hat is shown.</summary>
    public bool HasWorldCloudHats => worldCloudHats is not null;

    /// <summary>How many of the cloud hat's geometries the last frame drew; for tests.</summary>
    internal int WorldCloudHatDraws { get; private set; }

    // The layer's group: its constants, the density and normal maps' sampler and the detail maps', then its six maps;
    // and the scene's depth.
    private void CreateCloudHatLayouts()
    {
        if (cloudHatLayout is not null)
            return;
        static BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        static BindGroupLayoutEntry Filtering(uint binding) =>
            new() { Binding = binding, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        cloudHatLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaCloudHats.Bytes },
                },
                Filtering(1), Filtering(2), Map(3), Map(4), Map(5), Map(6), Map(7), Map(8),
            ],
        });
        cloudHatDepthLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
    }

    /// <summary>
    /// The cloud hats' pipeline. With <paramref name="prepare"/>, it only starts compiling in the background, for when
    /// a world's cloud hat is shown.
    /// </summary>
    internal void CreateCloudHatPipeline(bool prepare = false)
    {
        if (cloudHatPipeline is not null)
            return;
        CreateSkyLayouts();
        CreateCloudHatLayouts();
        RenderPipelineDescriptor Describe()
        {
            ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
            {
                Code = Read("Scene/scene.wgsl") + "\n" + Read("Gta5/sky_system.wgsl") + "\n" + Read("Gta5/fog.wgsl") + "\n" + Read("Gta5/sky.wgsl") + "\n" + Read("Gta5/cloud_hats.wgsl"),
            });
            return new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
                {
                    BindGroupLayouts = [sceneLayout, gtaSkyLayout!, cloudHatLayout!, cloudHatDepthLayout!],
                }),
                Vertex = new VertexState
                {
                    Module = shader,
                    EntryPoint = "vs_gta_cloud_hat",
                    Buffers =
                    [
                        new VertexBufferLayout
                        {
                            ArrayStride = CloudHatStride,
                            Attributes = new[]
                            {
                                new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                                new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x3 },
                                new VertexAttribute { ShaderLocation = 2, Offset = 24, Format = VertexFormat.Float32x4 },
                                new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                                new VertexAttribute { ShaderLocation = 4, Offset = 48, Format = VertexFormat.Float32x4 },
                            },
                        },
                    ],
                },
                // The game's default raster state: back faces culled, its triangles wound as the world's.
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.CCW, CullMode = CullMode.Back },
                Fragment = new FragmentState
                {
                    Module = shader,
                    EntryPoint = "fs_gta_cloud_hat",
                    Targets = HdrTargets(new ColorTargetState
                    {
                        Format = HdrFormat,
                        // BS_Normal over the colour; the target's alpha is the sky's cover, kept.
                        Blend = new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                        },
                        WriteMask = ColorWriteMask.All,
                    }),
                },
                // DSS_TestOnly_LessEqual_Invert: tested against the world's depth, not written.
                DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.GreaterEqual },
            };
        }
        cloudHatPipeline = Compile("gta-cloud-hats", Describe, prepare);
        if (prepare)
            cloudHatPipeline = null;
    }

    /// <summary>Shows this cloud hat over the world's sky, in place of any before; null shows none.</summary>
    public void ShowWorldCloudHats(GtaCloudHats? hats)
    {
        ClearWorldCloudHats();
        if (hats is null)
            return;
        CreateCloudHatPipeline();
        Sampler? Hat(AddressMode along) => device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = along,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            // HQLINEAR.
            MaxAnisotropy = 8,
        });
        cloudHatClampAlong ??= Hat(AddressMode.ClampToEdge);
        cloudHatWrap ??= Hat(AddressMode.Repeat);
        var owned = new List<HikariHopper.Gpu.Texture>();
        var views = new Dictionary<GameTexture, TextureView>(ReferenceEqualityComparer.Instance);
        TextureView Own(GameTexture? source)
        {
            if (source is null)
                return clear!;
            if (views.TryGetValue(source, out TextureView? known))
                return known;
            if (CreateTexture(source) is not var (texture, block))
                return views[source] = clear!;
            for (int level = 0; level < source.MipCount; level++)
                WriteLevel(source, texture, block, level);
            owned.Add(texture);
            return views[source] = texture.CreateView();
        }
        var draws = new List<CloudHatDraw>();
        foreach (GtaCloudHatLayer layer in hats.Layers)
            foreach (GtaCloudHatGeometry geometry in layer.Geometries)
            {
                Buffer vertices = Upload((ReadOnlySpan<float>)geometry.Vertices, sizeof(float));
                Buffer indices = Upload((ReadOnlySpan<uint>)geometry.Indices, sizeof(uint), BufferUsage.Index);
                Buffer uniforms = device.CreateBuffer(new BufferDescriptor { Size = GtaCloudHats.Bytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
                BindGroup group = device.CreateBindGroup(new BindGroupDescriptor
                {
                    Layout = cloudHatLayout!,
                    Entries =
                    [
                        new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = GtaCloudHats.Bytes },
                        new BindGroupEntry { Binding = 1, Sampler = geometry.Effect.CameraScroll ? cloudHatWrap : cloudHatClampAlong },
                        new BindGroupEntry { Binding = 2, Sampler = cloudHatWrap },
                        .. geometry.Textures.Select((texture, i) => new BindGroupEntry { Binding = (uint)(3 + i), TextureView = Own(texture) }),
                    ],
                });
                draws.Add(new CloudHatDraw(vertices, indices, (uint)geometry.Indices.Length, uniforms, group));
            }
        worldCloudHats = new WorldCloudHatResources(hats, [.. draws], [.. owned]);
        cloudHatValues = new Vector4[draws.Count * (GtaCloudHats.Bytes / 16)];
        cloudHatDrawn = new bool[draws.Count];
        // Its animation starts as it's shown, as the game's does as a hat transitions in.
        cloudHatAnimation = new CloudHatAnimation();
        cloudHatClock = double.NaN;
    }

    // The cloud hat's GPU memory goes now, not whenever the handles are collected.
    private void ClearWorldCloudHats()
    {
        if (worldCloudHats is not { } hats)
            return;
        worldCloudHats = null;
        WorldCloudHatDraws = 0;
        foreach (CloudHatDraw draw in hats.Draws)
        {
            draw.Vertices.Destroy();
            draw.Indices.Destroy();
            draw.Uniforms.Destroy();
        }
        foreach (HikariHopper.Gpu.Texture texture in hats.Textures)
            texture.Destroy();
    }

    /// <summary>
    /// The cloud hat, in the world's HDR pass after its water (<paramref name="depthCopied"/>: whether the scene's depth
    /// was copied for the water already; if not, the pass ends while it's copied for the soft clouds). Returns the pass
    /// to go on with, its scene and light groups bound again.
    /// </summary>
    private RenderPassEncoder DrawCloudHats(RenderPassEncoder pass, CommandEncoder encoder, bool depthCopied)
    {
        WorldCloudHatDraws = 0;
        if (!worldShown || worldSky is not { } sky || worldCloudHats is not { } hats || cloudHatPipeline is null)
            return pass;
        // The animation steps on by the renderer's clock.
        double now = waterClock.Elapsed.TotalSeconds;
        float step = double.IsNaN(cloudHatClock) ? 0 : (float)(now - cloudHatClock);
        cloudHatClock = now;
        Vector3 camera = Camera.Eye;
        cloudHatAnimation.Advance(hats.Hats, step, hats.Hats.Wind(camera.Z, Hours, timecycle));
        CloudFrame frame = GtaCloudHats.Frame(hats.Hats.Settings, Hours, Sky, timecycle);
        bool[] drawn = cloudHatDrawn;
        if (hats.Hats.Write(cloudHatValues, drawn, frame, cloudHatAnimation, camera, Camera.Near, GtaSky.WaterLevel) == 0)
            return pass;
        int slot = GtaCloudHats.Bytes / 16;
        for (int i = 0; i < drawn.Length; i++)
            if (drawn[i])
                queue.WriteBuffer(hats.Draws[i].Uniforms, 0, (ReadOnlySpan<Vector4>)cloudHatValues.AsSpan(i * slot, slot));

        if (!depthCopied)
        {
            pass.End();
            var size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1);
            encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdrDepth!, Aspect = TextureAspect.DepthOnly },
                new TexelCopyTextureInfo { Texture = waterDepth!, Aspect = TextureAspect.DepthOnly }, size);
            pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = HdrAttachments(clear: false),
                DepthStencilAttachment = new RenderPassDepthStencilAttachment
                {
                    View = hdrDepthView!,
                    DepthLoadOp = LoadOp.Load,
                    DepthStoreOp = StoreOp.Store,
                },
            });
        }
        if (cloudHatDepthGroup is null || cloudHatDepthOf != waterDepthView)
        {
            cloudHatDepthOf = waterDepthView;
            cloudHatDepthGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = cloudHatDepthLayout!,
                Entries = [new BindGroupEntry { Binding = 0, TextureView = waterDepthView! }],
            });
        }
        pass.SetPipeline(cloudHatPipeline);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, sky.Group);
        pass.SetBindGroup(3, cloudHatDepthGroup);
        for (int i = 0; i < drawn.Length; i++)
        {
            if (!drawn[i])
                continue;
            CloudHatDraw draw = hats.Draws[i];
            pass.SetBindGroup(2, draw.Group);
            pass.SetVertexBuffer(0, draw.Vertices);
            pass.SetIndexBuffer(draw.Indices, IndexFormat.Uint32);
            pass.DrawIndexed(draw.IndexCount);
            WorldCloudHatDraws++;
        }
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(2, lightGroup!);
        return pass;
    }
}
