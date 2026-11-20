using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// GTA V's sky behind its world (Gta5/sky.wgsl), drawn as the game draws it each frame: the cloud noise into its map,
// then, first in the world's HDR pass, the game's sky dome, the moon and the sun's disc, after which the sky covers the
// frame and the world is drawn over it. Only once a world has given its sky (ShowWorldSky); until then, and for a world
// with none, the backdrop shows behind it as before.
internal sealed unsafe partial class ViewportRenderer
{
    // The cloud noise's map, as the game's perlin noise map.
    private const int PerlinSide = 512;
    private const TextureFormat PerlinFormat = TextureFormat.RGBA8Unorm;

    // The fan the game draws each disc with: 88 triangles.
    private const uint DiscCorners = 88 * 3;

    /// <summary>The sky's GPU side: the dome's buffers, its textures, and its groups.</summary>
    private sealed record WorldSkyResources(Buffer Vertices, Buffer Indices, uint IndexCount, WebGpuSharp.Texture[] Textures, BindGroup Group,
        BindGroup PerlinGroup, GtaSkyVisuals Visuals);

    private WorldSkyResources? worldSky;
    private Buffer? skyUniforms;
    private WebGpuSharp.Texture? perlinMap;
    private TextureView? perlinView;
    private BindGroupLayout? gtaSkyLayout, gtaPerlinLayout;
    private Sampler? skyWrap, skyClamp;
    private RenderPipeline? skyDome, skyMoon, skySun, skyCover, skyPerlin;
    private GtaSky.Passes skyPasses;
    private readonly Vector4[] skyValues = new Vector4[GtaSky.Bytes / 16];

    /// <summary>Whether a world's sky is shown behind it.</summary>
    public bool HasWorldSky => worldSky is not null;

    /// <summary>Which of the game's sky passes the last frame drew; for tests.</summary>
    internal GtaSky.Passes WorldSkyPasses => skyPasses;

    // The sky's groups' layouts: its constants, a repeating and a clamping sampler, then the cloud noise's map, the
    // detail noise, the dither, the stars and the moon; and the cloud noise's pass: its constants, a repeating sampler
    // and the base noise.
    private void CreateSkyLayouts()
    {
        if (gtaSkyLayout is not null)
            return;
        static BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        static BindGroupLayoutEntry Filtering(uint binding) =>
            new() { Binding = binding, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        static BindGroupLayoutEntry Constants() => new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaSky.Bytes },
        };
        gtaSkyLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries = [Constants(), Filtering(1), Filtering(2), Map(3), Map(4), Map(5), Map(6), Map(7)],
        });
        gtaPerlinLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = [Constants(), Filtering(1), Map(2)] });
    }

    /// <summary>
    /// The sky's pipelines. With <paramref name="prepare"/>, they only start compiling in the background, for when a
    /// world's sky is shown.
    /// </summary>
    internal void CreateSkyPipelines(bool prepare = false)
    {
        if (skyDome is not null)
            return;
        CreateSkyLayouts();
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("Gta5/sky_system.wgsl") + "\n" + Read("Gta5/fog.wgsl") + "\n" + Read("Gta5/sky.wgsl"),
        }));
        PipelineLayout skyPipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, gtaSkyLayout!] });
        // At the far plane, behind whatever the world draws over it.
        DepthStencilState Far() => new() { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always };
        RenderPipelineDescriptor Sky(string vertex, string fragment, BlendState? blend, ColorWriteMask mask = ColorWriteMask.All,
            VertexBufferLayout[]? buffers = null) => new()
        {
            Layout = skyPipelineLayout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = vertex, Buffers = buffers ?? [] },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = fragment,
                Targets = HdrTargets(new ColorTargetState { Format = HdrFormat, Blend = blend, WriteMask = mask }),
            },
            DepthStencil = Far(),
        };
        // The sky's alpha is how much of a disc shows: the moon is added by it, the sun blended towards by it.
        static BlendState ByDestinationAlpha(BlendFactor destination) => new()
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.DstAlpha, DstFactor = destination },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
        };
        VertexBufferLayout[] dome =
        [
            new VertexBufferLayout
            {
                ArrayStride = GtaSkyDome.Stride,
                Attributes = new[]
                {
                    new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                    new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x2 },
                    new VertexAttribute { ShaderLocation = 2, Offset = 20, Format = VertexFormat.Float32x2 },
                },
            },
        ];
        skyDome = Compile("gta-sky/dome", () => Sky("vs_gta_sky", "fs_gta_sky", null, buffers: dome), prepare);
        skyMoon = Compile("gta-sky/moon", () => Sky("vs_gta_moon", "fs_gta_moon", ByDestinationAlpha(BlendFactor.One)), prepare);
        skySun = Compile("gta-sky/sun", () => Sky("vs_gta_sun", "fs_gta_sun", ByDestinationAlpha(BlendFactor.OneMinusDstAlpha)), prepare);
        skyCover = Compile("gta-sky/cover", () => Sky("vs_gta_sky_cover", "fs_gta_sky_cover", null, ColorWriteMask.Alpha), prepare);
        skyPerlin = Compile("gta-sky/perlin", () => new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, gtaPerlinLayout!] }),
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_gta_perlin" },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_gta_perlin",
                Targets = new[] { new ColorTargetState { Format = PerlinFormat, WriteMask = ColorWriteMask.All } },
            },
        }, prepare);
        if (prepare)
            skyDome = skyMoon = skySun = skyCover = skyPerlin = null;
    }

    /// <summary>Shows this sky behind the world, in place of any before; null shows none (the backdrop again).</summary>
    public void ShowWorldSky(GtaSkyDome? dome)
    {
        ClearWorldSky();
        if (dome is null)
            return;
        CreateSkyPipelines();
        skyWrap ??= device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        skyClamp ??= device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        skyUniforms ??= device.CreateBuffer(new BufferDescriptor { Size = GtaSky.Bytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        if (perlinMap is null)
        {
            perlinMap = device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.D2,
                Size = new Extent3D(PerlinSide, PerlinSide, 1),
                Format = PerlinFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            perlinView = perlinMap.CreateView();
        }
        var owned = new List<WebGpuSharp.Texture>();
        TextureView Own(GameTexture? source, TextureView missing)
        {
            if (source is null || CreateTexture(source) is not var (texture, block))
                return missing;
            for (int level = 0; level < source.MipCount; level++)
                WriteLevel(source, texture, block, level);
            owned.Add(texture);
            return texture.CreateView();
        }
        TextureView noise = Own(dome.Noise, clear!), detail = Own(dome.HighDetail, clear!), dither = Own(dome.Dither, clear!),
            stars = Own(dome.Stars, clear!), moon = Own(dome.Moon, clear!);
        Buffer vertices = Upload((ReadOnlySpan<float>)dome.Vertices, sizeof(float));
        Buffer indices = Upload((ReadOnlySpan<uint>)dome.Indices, sizeof(uint), BufferUsage.Index);
        BindGroup group = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = gtaSkyLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = skyUniforms, Size = GtaSky.Bytes },
                new BindGroupEntry { Binding = 1, Sampler = skyWrap },
                new BindGroupEntry { Binding = 2, Sampler = skyClamp },
                new BindGroupEntry { Binding = 3, TextureView = perlinView! },
                new BindGroupEntry { Binding = 4, TextureView = detail },
                new BindGroupEntry { Binding = 5, TextureView = dither },
                new BindGroupEntry { Binding = 6, TextureView = stars },
                new BindGroupEntry { Binding = 7, TextureView = moon },
            ],
        });
        BindGroup perlin = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = gtaPerlinLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = skyUniforms, Size = GtaSky.Bytes },
                new BindGroupEntry { Binding = 1, Sampler = skyWrap },
                new BindGroupEntry { Binding = 2, TextureView = noise },
            ],
        });
        worldSky = new WorldSkyResources(vertices, indices, (uint)dome.Indices.Length, [.. owned], group, perlin, dome.Visuals);
    }

    // The sky's GPU memory goes now, not whenever the handles are collected.
    private void ClearWorldSky()
    {
        if (worldSky is not { } sky)
            return;
        worldSky = null;
        ClearWorldFog();
        sky.Vertices.Destroy();
        sky.Indices.Destroy();
        foreach (WebGpuSharp.Texture texture in sky.Textures)
            texture.Destroy();
    }

    /// <summary>
    /// The sky's constants for this frame, and its cloud noise drawn from them, before the world's HDR pass; false
    /// where no sky is shown.
    /// </summary>
    private bool DrawWorldSkyNoise(CommandEncoder encoder)
    {
        if (!worldShown || worldSky is not { } sky)
            return false;
        skyPasses = GtaSky.Write(skyValues, sky.Visuals, Hours, Sky, Camera.Eye, waterClock.Elapsed.TotalSeconds, timecycle);
        queue.WriteBuffer(skyUniforms!, 0, (ReadOnlySpan<Vector4>)skyValues);
        WriteWorldFog(skyValues);
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = perlinView!, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) }],
        });
        pass.SetPipeline(skyPerlin!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, sky.PerlinGroup);
        pass.Draw(3);
        pass.End();
        return true;
    }

    /// <summary>The sky, first in the HDR pass: the dome, the moon and the sun, as the game's passes say, then its cover.</summary>
    private void DrawWorldSky(RenderPassEncoder pass)
    {
        if (worldSky is not { } sky)
            return;
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, sky.Group);
        pass.SetPipeline(skyDome!);
        pass.SetVertexBuffer(0, sky.Vertices);
        pass.SetIndexBuffer(sky.Indices, IndexFormat.Uint32);
        pass.DrawIndexed(sky.IndexCount);
        if (skyPasses.Moon)
        {
            pass.SetPipeline(skyMoon!);
            pass.Draw(DiscCorners);
        }
        if (skyPasses.Sun)
        {
            pass.SetPipeline(skySun!);
            pass.Draw(DiscCorners);
        }
        pass.SetPipeline(skyCover!);
        pass.Draw(3);
    }
}
