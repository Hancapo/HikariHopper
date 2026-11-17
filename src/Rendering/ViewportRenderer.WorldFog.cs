using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// GTA V's fog over its world (gta_world_fog_pass.wgsl), as the game fogs it: once the world's solid surfaces and decals
// are drawn, its fog rays at half the frame's size (with the sun's shadow), blurred across and down, then a pass over
// the frame blends the fog along each pixel's ray over it; then the water and the blended surfaces are drawn, each
// fogging itself. The fog's constants are the sky's (GtaSky.Fog) and the timecycle's fog rays (GtaSky.FogRays), given
// to the world's shaders through the light groups (bindings 9 and 10, gta_world_fog.wgsl); with no world's sky, none.
internal sealed unsafe partial class ViewportRenderer
{
    /// <summary>The world's fog's constants: gta_world_fog.wgsl's WorldFog.</summary>
    private const int WorldFogBytes = GtaSky.FogBytes + 16;

    // The fog rays' map: how much of each ray the sun reaches, and its depth.
    private const TextureFormat FogRayFormat = TextureFormat.RG16Float;

    private Buffer? worldFogBuffer;
    private BindGroupLayout? worldFogLayout, fogRayBlurLayout;
    private BindGroup? worldFogGroup, fogRayAcrossGroup, fogRayDownGroup;
    private TextureView? worldFogDepth;
    private RenderPipeline? worldFogPipeline, fogRayPipeline, fogRayAcross, fogRayDown;
    // The rays as marched, blurred across, then down: the last is what the light groups bind.
    private Texture? fogRayMarched, fogRayAcrossMap, fogRayMap, fogRayPlaceholder;
    private TextureView? fogRayMarchedView, fogRayAcrossView, fogRayMapView;
    private readonly Vector4[] worldFogValues = new Vector4[WorldFogBytes / 16];

    /// <summary>Whether the last frame drew the world's fog, and its fog rays; for tests.</summary>
    internal bool WorldFogged { get; private set; }

    internal bool WorldFogRays { get; private set; }

    // The fog's constants the light groups bind: none until a world's sky is shown.
    private void CreateWorldFogBuffer()
    {
        worldFogBuffer ??= device.CreateBuffer(new BufferDescriptor { Size = WorldFogBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        ClearWorldFog();
    }

    private void ClearWorldFog()
    {
        if (worldFogBuffer is null)
            return;
        GtaSky.NoFog(worldFogValues);
        worldFogValues[GtaSky.FogVectors] = Vector4.Zero;
        queue.WriteBuffer(worldFogBuffer, 0, (ReadOnlySpan<Vector4>)worldFogValues);
    }

    // The frame's fog: the sky's fog constants, just written, and the fog rays' at this hour.
    private void WriteWorldFog(ReadOnlySpan<Vector4> skyValues)
    {
        skyValues.Slice(GtaSky.FogStart, GtaSky.FogVectors).CopyTo(worldFogValues);
        worldFogValues[GtaSky.FogVectors] = GtaSky.FogRays(Hours);
        queue.WriteBuffer(worldFogBuffer!, 0, (ReadOnlySpan<Vector4>)worldFogValues);
    }

    // The fog rays' maps, at half the HDR target's size; and the light groups' stand-in before they exist.
    private void CreateFogRayTargets()
    {
        fogRayMarched?.Destroy();
        fogRayAcrossMap?.Destroy();
        fogRayMap?.Destroy();
        var size = new Extent3D((uint)(hdrWidth + 1) / 2, (uint)(hdrHeight + 1) / 2, 1);
        Texture Target() => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = size,
            Format = FogRayFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        fogRayMarched = Target();
        fogRayAcrossMap = Target();
        fogRayMap = Target();
        fogRayMarchedView = fogRayMarched.CreateView();
        fogRayAcrossView = fogRayAcrossMap.CreateView();
        fogRayMapView = fogRayMap.CreateView();
        fogRayAcrossGroup = fogRayDownGroup = null;
    }

    private TextureView FogRayView()
    {
        if (fogRayMapView is { } view)
            return view;
        fogRayPlaceholder ??= device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(1, 1, 1),
            Format = FogRayFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        return fogRayPlaceholder.CreateView();
    }

    /// <summary>
    /// The fog's pipelines: the fog rays, their blurs, and the pass over the frame, blended by the fog's amount into
    /// colour only, reading the depth it's attached to. With <paramref name="prepare"/>, they only start compiling in
    /// the background.
    /// </summary>
    internal void CreateWorldFogPipeline(bool prepare = false)
    {
        if (worldFogPipeline is not null)
            return;
        worldFogLayout ??= device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
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
        fogRayBlurLayout ??= device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
            ],
        });
        Lazy<ShaderModule> module = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = LitShader("gta_fog.wgsl") + "\n" + Read("gta_world_fog.wgsl") + "\n" + Read("gta_world_fog_pass.wgsl"),
        }));
        PipelineLayout lit = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, worldFogLayout, lightLayout!] });
        PipelineLayout blur = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, fogRayBlurLayout] });
        RenderPipelineDescriptor Describe(PipelineLayout layout, string fragment, TextureFormat format, BlendState? blend, ColorWriteMask mask,
            DepthStencilState? depth) => new()
        {
            Layout = layout,
            Vertex = new VertexState { Module = module.Value, EntryPoint = "vs_gta_world_fog" },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = module.Value,
                EntryPoint = fragment,
                Targets = new[] { new ColorTargetState { Format = format, Blend = blend, WriteMask = mask } },
            },
            DepthStencil = depth,
        };
        // As the game's depthFXBlendState: by the fog's amount, colour alone.
        var byAmount = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
        };
        RenderPipeline fog = Compile("gta-world-fog", () => Describe(lit, "fs_gta_world_fog", HdrFormat, byAmount,
            ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue,
            new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always }), prepare);
        RenderPipeline rays = Compile("gta-fog-rays", () => Describe(lit, "fs_gta_fog_rays", FogRayFormat, null, ColorWriteMask.All, null), prepare);
        RenderPipeline across = Compile("gta-fog-rays-across", () => Describe(blur, "fs_gta_fog_rays_across", FogRayFormat, null, ColorWriteMask.All, null),
            prepare);
        RenderPipeline down = Compile("gta-fog-rays-down", () => Describe(blur, "fs_gta_fog_rays_down", FogRayFormat, null, ColorWriteMask.All, null),
            prepare);
        if (prepare)
            return;
        (worldFogPipeline, fogRayPipeline, fogRayAcross, fogRayDown) = (fog, rays, across, down);
    }

    // The fog's depth group, made again only when the depth is (a resize).
    private BindGroup WorldFogGroup()
    {
        if (!ReferenceEquals(worldFogDepth, hdrDepthView))
        {
            worldFogDepth = hdrDepthView;
            worldFogGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = worldFogLayout!,
                Entries = [new BindGroupEntry { Binding = 0, TextureView = hdrDepthView! }],
            });
        }
        return worldFogGroup!;
    }

    /// <summary>
    /// The fog rays, before the fog over the world, where the sun casts its shadow (as Render's), and the timecycle has
    /// them.
    /// </summary>
    private void DrawFogRays(CommandEncoder encoder)
    {
        WorldFogRays = Shadows && key.Direction.Z > 0 && worldFogValues[GtaSky.FogVectors].X > 0;
        if (!WorldFogRays)
            return;
        CreateWorldFogPipeline();
        BindGroup Blur(TextureView source) => device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = fogRayBlurLayout!,
            Entries = [new BindGroupEntry { Binding = 0, TextureView = source }, new BindGroupEntry { Binding = 1, Sampler = reflectionSampler! }],
        });
        fogRayAcrossGroup ??= Blur(fogRayMarchedView!);
        fogRayDownGroup ??= Blur(fogRayAcrossView!);
        void Pass(TextureView target, RenderPipeline pipeline, BindGroup group, bool lit)
        {
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = target, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(1, 0, 0, 0) }],
            });
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, sceneGroup);
            pass.SetBindGroup(1, group);
            if (lit)
                pass.SetBindGroup(2, lightGroup!);
            pass.Draw(3);
            pass.End();
        }
        Pass(fogRayMarchedView!, fogRayPipeline!, WorldFogGroup(), lit: true);
        Pass(fogRayAcrossView!, fogRayAcross!, fogRayAcrossGroup, lit: false);
        Pass(fogRayMapView!, fogRayDown!, fogRayDownGroup, lit: false);
    }

    /// <summary>The fog over the world drawn so far, first in the pass that reads the world's depth.</summary>
    private void DrawWorldFog(RenderPassEncoder pass)
    {
        CreateWorldFogPipeline();
        pass.SetPipeline(worldFogPipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, WorldFogGroup());
        pass.SetBindGroup(2, lightGroup!);
        pass.Draw(3);
    }
}
