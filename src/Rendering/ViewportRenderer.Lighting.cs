using System.Numerics;
using System.Runtime.InteropServices;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// Drawables' light: GTA V's timecycle at the viewer's time of day, the directional light's shadow (after RedFury's),
// and drawables drawn in HDR, then exposed and tone-mapped as the game's post-process does, over the backdrop.
// Collisions, navmeshes and the backdrop keep their own flat shading, in the theme's colours.
//   reflection map (when the time changes) -> shadow map -> backdrop and lines to the display -> drawables to HDR ->
//   the frame's mean luminance -> tone map over them, exposed for it
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat HdrFormat = TextureFormat.RGBA16Float;
    private const int LightBytes = 288, ShadowViewBytes = 64;
    internal const int ShadowResolution = 2048;

    private BindGroupLayout? lightLayout;
    private BindGroup? lightGroup, skyLightGroup;
    private Buffer? lightBuffer;
    private Texture? shadowMap;
    private TextureView? shadowView;
    private Buffer? shadowViewBuffer;
    private BindGroup? shadowGroup;
    private RenderPipeline? shadowPipeline;
    private RenderPipeline? groundShadowPipeline;
    private BindGroup? noGroup;
    private BindGroupLayout? tonemapLayout;
    private RenderPipeline? tonemapPipeline;
    private Texture? hdr;
    private TextureView? hdrView;
    private BindGroup? tonemapGroup;
    private KeyLight key;
    private TimecycleLight cycle;
    private Matrix4x4 shadowMatrix;
    private float shadowTexel, shadowDepth;

    /// <summary>The time of day drawables are lit at, in hours after midnight.</summary>
    public float Hours { get; private set; } = DayCycle.DefaultHours;

    /// <summary>Stops added to the exposure the game's post-process works out from the frame.</summary>
    public float Exposure { get; set; }

    private void CreateLighting()
    {
        lightBuffer = device.CreateBuffer(new BufferDescriptor { Size = LightBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        shadowMap = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(ShadowResolution, ShadowResolution, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        shadowView = shadowMap.CreateView();
        Sampler compare = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            Compare = CompareFunction.LessEqual,
            MaxAnisotropy = 1,
        })!;
        CreateReflections();
        lightLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = LightBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 2, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Comparison } },
                new BindGroupLayoutEntry
                {
                    Binding = 3,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 4, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
            ],
        });
        // Drawables reflect the scene map; drawn into it, they reflect the sky map.
        BindGroup LightGroup(TextureView reflections) => device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = lightLayout,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = lightBuffer, Size = LightBytes },
                new BindGroupEntry { Binding = 1, TextureView = shadowView },
                new BindGroupEntry { Binding = 2, Sampler = compare },
                new BindGroupEntry { Binding = 3, TextureView = reflections },
                new BindGroupEntry { Binding = 4, Sampler = reflectionSampler! },
            ],
        });
        lightGroup = LightGroup(sceneMap!.View);
        skyLightGroup = LightGroup(skyMap!.View);

        // The shadow map: depth only, from the sun, nearer is smaller.
        BindGroupLayout shadowViewLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = ShadowViewBytes },
                },
            ],
        });
        shadowViewBuffer = device.CreateBuffer(new BufferDescriptor { Size = ShadowViewBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        shadowGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = shadowViewLayout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = shadowViewBuffer, Size = ShadowViewBytes }],
        });
        ShaderModule shadowShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("shadow.wgsl") });
        shadowPipeline = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout] }),
            Vertex = new VertexState
            {
                Module = shadowShader,
                EntryPoint = "vs_shadow",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = SurfaceVertex.Size,
                        Attributes = new[] { new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 } },
                    },
                },
            },
            // Both sides cast: a model's one-sided panels still shadow what is behind them.
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.Less },
        });

        // The drawable's shadow on the backdrop's ground, multiplied over it. Group 1 is unused.
        BindGroupLayout none = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = [] });
        noGroup = device.CreateBindGroup(new BindGroupDescriptor { Layout = none, Entries = [] });
        ShaderModule groundShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("ground_shadow.wgsl") });
        groundShadowPipeline = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, none, lightLayout] }),
            Vertex = new VertexState { Module = groundShader, EntryPoint = "vs_ground_shadow" },
            Fragment = new FragmentState
            {
                Module = groundShader,
                EntryPoint = "fs_ground_shadow",
                Targets = new[]
                {
                    new ColorTargetState
                    {
                        Format = viewFormat,
                        Blend = new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.Src },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                        },
                        WriteMask = ColorWriteMask.All,
                    },
                },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        });

        // The tone map, blended premultiplied over the backdrop.
        tonemapLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = MeasuredBytes },
                },
            ],
        });
        CreateExposure();
        ShaderModule tonemapShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("tonemap.wgsl") });
        tonemapPipeline = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, tonemapLayout, lightLayout] }),
            Vertex = new VertexState { Module = tonemapShader, EntryPoint = "vs_tonemap" },
            Fragment = new FragmentState
            {
                Module = tonemapShader,
                EntryPoint = "fs_tonemap",
                Targets = new[]
                {
                    new ColorTargetState
                    {
                        Format = viewFormat,
                        Blend = new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                        },
                        WriteMask = ColorWriteMask.All,
                    },
                },
            },
        });
        SetTime(Hours);
    }

    /// <summary>Lights drawables at this time of day: where the light comes from, and the timecycle's light then.</summary>
    public void SetTime(float hours)
    {
        Hours = hours;
        key = DayCycle.Evaluate(hours);
        cycle = Timecycle.Evaluate(hours);
        DrawReflections(hours);
    }

    /// <summary>The HDR target drawables are drawn to, sized to the window, and the tone map's view of it.</summary>
    private void CreateHdr()
    {
        hdr?.Destroy();
        hdr = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = HdrFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        hdrView = hdr.CreateView();
        tonemapGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = tonemapLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = hdrView },
                new BindGroupEntry { Binding = 1, Buffer = measured!, Size = MeasuredBytes },
            ],
        });
        CreateExposureGroup();
    }

    /// <summary>
    /// Fits the sun's shadow to what is shown: an orthographic view along the sun, around the drawables' bounding
    /// sphere (positions are relative to its middle, the scene's origin), deep enough to reach the ground well beyond
    /// it, where a low sun casts long shadows.
    /// </summary>
    private void FitShadow(DrawableScene drawable)
    {
        float radius = Math.Max(drawable.Box.Size.Length() / 2, .25f);
        Vector3 sun = key.Direction;
        Vector3 up = MathF.Abs(sun.Z) > .99f ? Vector3.UnitY : Vector3.UnitZ;
        Matrix4x4 view = Matrix4x4.CreateLookAt(sun * radius * 2, Vector3.Zero, up);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(radius * 2, radius * 2, radius * .5f, radius * 8);
        shadowMatrix = view * projection;
        shadowTexel = radius * 2 / ShadowResolution;
        shadowDepth = radius * 7.5f;
        queue.WriteBuffer(shadowViewBuffer!, 0, MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4x4>(in shadowMatrix)));
    }

    private void WriteLight(bool shadows)
    {
        Span<float> values = stackalloc float[LightBytes / sizeof(float)];
        Span<Vector4> vectors = MemoryMarshal.Cast<float, Vector4>(values);
        vectors[0] = new Vector4(key.Direction, 0);
        vectors[1] = new Vector4(cycle.Directional, 0);
        vectors[2] = new Vector4(cycle.NaturalDown, cycle.DownWrap);
        vectors[3] = new Vector4(cycle.NaturalUp, cycle.OcclusionProp);
        vectors[4] = new Vector4(cycle.DirectionalAmbient, cycle.OcclusionVehicle);
        vectors[5] = new Vector4(Timecycle.DirectionalAmbientDirection(cycle, key.Direction), 0);
        vectors[6] = new Vector4(Timecycle.ExposureScale, Timecycle.ExposurePower, Timecycle.ExposureOffset, cycle.Exposure + Exposure);
        vectors[7] = new Vector4(cycle.ExposureMin, cycle.ExposureMax, Timecycle.BrightExposure, Timecycle.DarkExposure);
        Filmic bright = Timecycle.Bright, dark = Timecycle.Dark;
        vectors[8] = new Vector4(bright.A, bright.B, bright.C, bright.D);
        vectors[9] = new Vector4(bright.E, bright.F, bright.W, 0);
        vectors[10] = new Vector4(dark.A, dark.B, dark.C, dark.D);
        vectors[11] = new Vector4(dark.E, dark.F, dark.W, 0);
        vectors[12] = new Vector4(ReflectionMips, 0, 0, 0);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(values[52..68]), in shadowMatrix);
        vectors[17] = new Vector4(shadowTexel, shadowDepth, ShadowResolution, shadows && key.Direction.Z > 0 ? 1 : 0);
        queue.WriteBuffer(lightBuffer!, 0, (ReadOnlySpan<float>)values);
    }

    /// <summary>The sun's shadow of the drawables: their solid and masked parts' depth from the sun.</summary>
    private void DrawShadow(CommandEncoder encoder)
    {
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = shadowView!,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1,
            },
        });
        pass.SetPipeline(shadowPipeline!);
        pass.SetBindGroup(0, shadowGroup!);
        pass.SetVertexBuffer(0, surfaceVertices!);
        pass.SetIndexBuffer(surfaceIndices!, IndexFormat.Uint32);
        foreach (var (part, _, mode) in surfaceParts)
            if (mode != AlphaMode.Blend)
                pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
        pass.End();
    }

    /// <summary>The drawable's shadow on the backdrop's ground, over the backdrop just drawn.</summary>
    private void DrawGroundShadow(RenderPassEncoder pass)
    {
        pass.SetPipeline(groundShadowPipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, noGroup!);
        pass.SetBindGroup(2, lightGroup!);
        pass.Draw(3);
    }

    /// <summary>A shader with the light: scene.wgsl, light.wgsl, then its own file.</summary>
    private static string LitShader(string name) => Read("scene.wgsl") + "\n" + Read("light.wgsl") + "\n" + Read(name);
}
