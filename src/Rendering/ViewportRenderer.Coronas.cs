using System.Numerics;
using System.Runtime.InteropServices;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// GTA V's coronas over its world (gta_coronas.wgsl), drawn as the game draws them after the world:
// added into the HDR target in a pass of their own, reading the scene's depth, before the bloom and the tone map. Only
// with a GTA V sky shown, once the world has given its coronas' textures (ShowWorldCoronas) and the lights to draw
// them on (SetWorldCoronas).
internal sealed unsafe partial class ViewportRenderer
{
    private GtaCoronaData? coronaData;
    private WebGpuSharp.Texture[] coronaTextures = [];
    private TextureView? coronaView, flareView;
    private Buffer? coronaInstances, coronaUniforms;
    private int coronaCapacity, coronaCount;
    private BindGroupLayout? coronaLayout;
    private BindGroup? coronaGroup;
    private TextureView? coronaGroupDepth;
    private Sampler? coronaSampler;
    private RenderPipeline? coronaPipeline;
    private readonly Vector4[] coronaValues = new Vector4[GtaCoronas.Bytes / 16];

    /// <summary>Whether a world's coronas can be drawn: their textures are up.</summary>
    public bool HasWorldCoronas => coronaData is not null;

    /// <summary>The coronas given last, drawn while a GTA V sky is shown; for tests.</summary>
    internal int WorldCoronaCount => coronaCount;

    /// <summary>The coronas' textures and settings, in place of any before; null shows none.</summary>
    public void ShowWorldCoronas(GtaCoronaData? data)
    {
        ClearWorldCoronas();
        if (data is null)
            return;
        var owned = new List<WebGpuSharp.Texture>();
        TextureView Own(FiveFury.Textures.Texture? source)
        {
            if (source is null || CreateTexture(source) is not var (texture, block))
                return clear!;
            for (int level = 0; level < source.MipCount; level++)
                WriteLevel(source, texture, block, level);
            owned.Add(texture);
            return texture.CreateView();
        }
        coronaView = Own(data.Corona);
        flareView = Own(data.Flare);
        coronaTextures = [.. owned];
        coronaData = data;
    }

    /// <summary>
    /// The coronas to draw, as registered from the world's lights (GtaCoronas.Build); the camera's part is worked out
    /// each frame. Copied to the GPU now.
    /// </summary>
    public void SetWorldCoronas(List<GtaCoronaInstance> coronas)
    {
        coronaCount = coronas.Count;
        if (coronaCount == 0)
            return;
        if (coronaInstances is null || coronaCapacity < coronaCount)
        {
            coronaInstances?.Destroy();
            // Room to grow, so a camera moving about doesn't make it again each time.
            coronaCapacity = Math.Max(1024, (int)BitOperations.RoundUpToPowerOf2((uint)coronaCount));
            coronaInstances = device.CreateBuffer(new BufferDescriptor
            {
                Size = (ulong)coronaCapacity * GtaCoronaInstance.Bytes,
                Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            });
            coronaGroup = null;
        }
        queue.WriteBuffer(coronaInstances, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(coronas)));
    }

    // The coronas' GPU memory goes now, not whenever the handles are collected.
    private void ClearWorldCoronas()
    {
        coronaData = null;
        coronaCount = 0;
        foreach (WebGpuSharp.Texture texture in coronaTextures)
            texture.Destroy();
        coronaTextures = [];
        coronaView = flareView = null;
        coronaInstances?.Destroy();
        coronaInstances = null;
        coronaCapacity = 0;
        coronaGroup = null;
    }

    /// <summary>
    /// The coronas' pipeline. With <paramref name="prepare"/>, it only starts compiling in the background, for when a
    /// world's coronas are drawn.
    /// </summary>
    internal void CreateCoronaPipeline(bool prepare = false)
    {
        if (coronaPipeline is not null)
            return;
        BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        coronaLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaSky.Bytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 8,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaCoronas.Bytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 9,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = GtaCoronaInstance.Bytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 10,
                    Visibility = ShaderStage.Vertex,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
                Map(11),
                Map(12),
                new BindGroupLayoutEntry { Binding = 13, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("sky_system.wgsl") + "\n" + Read("gta_fog.wgsl") + "\n" + Read("gta_sky.wgsl") + "\n" + Read("gta_coronas.wgsl"),
        }));
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, coronaLayout] });
        // Added (BS_Add), over whatever's there; the HDR target's alpha, the sky's cover, is left as it is.
        var add = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.One };
        coronaPipeline = Compile("gta-coronas", () => new RenderPipelineDescriptor
        {
            Layout = layout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_gta_corona" },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleStrip, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_gta_corona",
                Targets = new[] { new ColorTargetState { Format = HdrFormat, Blend = new BlendState { Color = add, Alpha = add }, WriteMask = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue } },
            },
        }, prepare);
        if (prepare)
        {
            coronaPipeline = null;
            return;
        }
        coronaUniforms ??= device.CreateBuffer(new BufferDescriptor { Size = GtaCoronas.Bytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        coronaSampler ??= device.CreateSampler(new SamplerDescriptor
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
    }

    /// <summary>The coronas, added over the world once it's drawn; nothing without a GTA V sky or coronas to draw.</summary>
    private void DrawWorldCoronas(CommandEncoder encoder)
    {
        if (!worldShown || worldSky is null || coronaData is not { } data || coronaCount == 0 || coronaInstances is null || hdrDepthView is null)
            return;
        CreateCoronaPipeline();
        if (coronaGroup is null || !ReferenceEquals(coronaGroupDepth, hdrDepthView))
        {
            coronaGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = coronaLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, Buffer = skyUniforms!, Size = GtaSky.Bytes },
                    new BindGroupEntry { Binding = 8, Buffer = coronaUniforms!, Size = GtaCoronas.Bytes },
                    new BindGroupEntry { Binding = 9, Buffer = coronaInstances, Size = (ulong)coronaCapacity * GtaCoronaInstance.Bytes },
                    new BindGroupEntry { Binding = 10, TextureView = hdrDepthView },
                    new BindGroupEntry { Binding = 11, TextureView = coronaView! },
                    new BindGroupEntry { Binding = 12, TextureView = flareView! },
                    new BindGroupEntry { Binding = 13, Sampler = coronaSampler! },
                ],
            });
            coronaGroupDepth = hdrDepthView;
        }
        Vector3 eye = Camera.Eye;
        float ground = data.HeightMap?.MinimumInterpolated(eye.X, eye.Y) ?? 0;
        GtaCoronas.Write(coronaValues, data.Visuals, Hours, Camera.Right, Camera.Up, Camera.Forward, OrbitCamera.TanHalf, Aspect, hdrWidth,
            waterClock.Elapsed.TotalSeconds, MathF.Max(0, eye.Z - ground), coronaCount);
        queue.WriteBuffer(coronaUniforms!, 0, (ReadOnlySpan<Vector4>)coronaValues);
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        pass.SetPipeline(coronaPipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, coronaGroup);
        // Each corona's two layers, then its flare.
        pass.Draw(4, (uint)coronaCount * 3);
        pass.End();
    }
}
