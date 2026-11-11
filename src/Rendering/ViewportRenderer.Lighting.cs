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
//   the frame's mean luminance -> their bloom -> tone map over them, exposed for it
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat HdrFormat = TextureFormat.RGBA16Float;
    private const int LightBytes = 560, ShadowViewBytes = 64;
    internal const int ShadowResolution = 2048;

    /// <summary>
    /// How many cascades the sun's shadow is split into by distance from the camera, as GTA V splits its own: the nearest
    /// covers a few metres in front of the camera at the map's full resolution, the furthest what the model reaches to.
    /// </summary>
    internal const int ShadowCascades = 4;

    private BindGroupLayout? lightLayout;
    private BindGroup? lightGroup, skyLightGroup;
    private Buffer? lightBuffer;
    private Texture? shadowMap;
    // The cascades, all of them as the shading reads them, and each one as its pass draws it.
    private TextureView? shadowView;
    private readonly TextureView[] shadowLayers = new TextureView[ShadowCascades];
    private readonly Buffer[] shadowViewBuffers = new Buffer[ShadowCascades];
    private readonly BindGroup[] shadowGroups = new BindGroup[ShadowCascades];
    private BindGroupLayout? shadowViewLayout;
    private RenderPipeline? shadowPipeline;
    // Cut-out surfaces, with their material for the diffuse map's alpha (made with the surface pipelines).
    private RenderPipeline? maskedShadowPipeline;
    private RenderPipeline? groundShadowPipeline;
    private BindGroup? noGroup;
    private BindGroupLayout? tonemapLayout;
    private RenderPipeline? tonemapPipeline;
    private Texture? hdr;
    private TextureView? hdrView;
    private BindGroup? tonemapGroup;
    private KeyLight key;
    private TimecycleLight cycle;
    // Each cascade's view from the sun, its texel in metres and its depth range in metres; and how far from the camera,
    // along its view, each one reaches.
    private readonly Matrix4x4[] shadowMatrices = new Matrix4x4[ShadowCascades];
    private Vector4 shadowTexels, shadowDepths, shadowSplits;
    // The drawables' bounding radius around the scene's origin, which the cascades reach to; none without a drawable.
    private float shadowRadius;

    /// <summary>The time of day drawables are lit at, in hours after midnight.</summary>
    public float Hours { get; private set; } = DayCycle.DefaultHours;

    /// <summary>The sun's and moon's paths, from the game's <c>time.xml</c>; the game's own until one is read.</summary>
    public SkyPath Sky { get; private set; } = SkyPath.Game;

    /// <summary>Moves the sun and moon along these paths, and lights drawables from where they then are.</summary>
    public void SetSky(SkyPath sky)
    {
        Sky = sky;
        SetTime(Hours);
    }

    /// <summary>Whether the sun casts shadows, on the drawable and on the ground under it.</summary>
    public bool Shadows { get; set; } = true;

    /// <summary>Stops added to the exposure the game's post-process works out from the frame.</summary>
    public float Exposure { get; set; }

    private void CreateLighting()
    {
        lightBuffer = device.CreateBuffer(new BufferDescriptor { Size = LightBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        shadowMap = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(ShadowResolution, ShadowResolution, ShadowCascades),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        shadowView = shadowMap.CreateView(new TextureViewDescriptor
        {
            Format = DepthFormat,
            Dimension = TextureViewDimension.D2Array,
            MipLevelCount = 1,
            ArrayLayerCount = ShadowCascades,
            Aspect = TextureAspect.All,
        });
        for (int i = 0; i < ShadowCascades; i++)
            shadowLayers[i] = shadowMap.CreateView(new TextureViewDescriptor
            {
                Format = DepthFormat,
                Dimension = TextureViewDimension.D2,
                MipLevelCount = 1,
                BaseArrayLayer = (uint)i,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All,
            });
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
                // The terrain's vertices read the sun's direction as well.
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = LightBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2Array },
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
        shadowViewLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
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
        for (int i = 0; i < ShadowCascades; i++)
        {
            shadowViewBuffers[i] = device.CreateBuffer(new BufferDescriptor { Size = ShadowViewBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
            shadowGroups[i] = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = shadowViewLayout,
                Entries = [new BindGroupEntry { Binding = 0, Buffer = shadowViewBuffers[i], Size = ShadowViewBytes }],
            });
        }
        Lazy<ShaderModule> shadowShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("shadow.wgsl") }));
        shadowPipeline = gpu.Pipeline($"shadow/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout] }),
            Vertex = new VertexState
            {
                Module = shadowShader.Value,
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
        }));

        // The drawable's shadow on the backdrop's ground, multiplied over it. Group 1 is unused.
        BindGroupLayout none = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = [] });
        noGroup = device.CreateBindGroup(new BindGroupDescriptor { Layout = none, Entries = [] });
        Lazy<ShaderModule> groundShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("ground_shadow.wgsl") }));
        groundShadowPipeline = gpu.Pipeline($"ground-shadow/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, none, lightLayout] }),
            Vertex = new VertexState { Module = groundShader.Value, EntryPoint = "vs_ground_shadow" },
            Fragment = new FragmentState
            {
                Module = groundShader.Value,
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
        }));

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
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
            ],
        });
        CreateExposure();
        CreateBloom();
        Lazy<ShaderModule> tonemapShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("tonemap.wgsl") }));
        tonemapPipeline = gpu.Pipeline($"tonemap/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, tonemapLayout, lightLayout] }),
            Vertex = new VertexState { Module = tonemapShader.Value, EntryPoint = "vs_tonemap" },
            Fragment = new FragmentState
            {
                Module = tonemapShader.Value,
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
        }));
        SetTime(Hours);
    }

    /// <summary>Lights drawables at this time of day: where the light comes from, and the timecycle's light then.</summary>
    public void SetTime(float hours)
    {
        // Evaluated first: an hour that isn't a number throws and changes nothing.
        key = DayCycle.Evaluate(hours, Sky);
        cycle = Timecycle.Evaluate(hours);
        Hours = hours;
        // A drawable's shadow follows the sun.
        if (shadowRadius > 0)
            FitShadow(shadowRadius);
        DrawReflections(hours);
    }

    /// <summary>The HDR target drawables are drawn to, sized to the window, its bloom, and the tone map's view of them.</summary>
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
        CreateBloomChain();
        tonemapGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = tonemapLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = hdrView },
                new BindGroupEntry { Binding = 1, Buffer = measured!, Size = MeasuredBytes },
                new BindGroupEntry { Binding = 2, TextureView = bloomViews[0] },
                new BindGroupEntry { Binding = 3, Sampler = bloomSampler! },
            ],
        });
        CreateExposureGroup();
    }

    /// <summary>The drawables' bounding radius around the scene's origin, which the shadow's cascades reach to.</summary>
    private void FitShadow(float radius) => shadowRadius = radius;

    /// <summary>
    /// Fits the shadow's cascades to the view, each frame: the distance from the camera to the far side of the
    /// drawables is split, more finely near the camera (GTA's practical split, between even and logarithmic), and each
    /// slice of the view gets an orthographic view along the sun around the sphere that holds it. That sphere's size
    /// doesn't turn with the camera, and its middle is snapped to whole texels, so the shadow's edges don't crawl as the
    /// camera orbits. A slice wider than the drawables takes their own sphere. Each view is deep enough to take in all
    /// of the drawables casting into it, and the ground a low sun throws their shadow onto.
    /// </summary>
    private void FitCascades()
    {
        float radius = shadowRadius;
        if (radius <= 0)
            return;
        Vector3 sun = key.Direction;
        Vector3 eye = Camera.Eye - origin;
        Vector3 forward = Camera.Forward;
        float aspect = height > 0 ? (float)width / height : 1;
        // How far a slice's corners are from its middle line, per metre along it.
        float spread = OrbitCamera.TanHalf * MathF.Sqrt(1 + aspect * aspect);
        float reach = Math.Max(eye.Length() + radius, .5f);
        float near = Math.Clamp(eye.Length() - radius, reach * .002f, reach * .5f);
        Vector3 up = MathF.Abs(sun.Z) > .99f ? Vector3.UnitY : Vector3.UnitZ;
        // The sun's view axes, as CreateLookAt builds them, for snapping the middle to texels.
        Vector3 lightRight = Vector3.Normalize(Vector3.Cross(up, sun));
        Vector3 lightUp = Vector3.Cross(sun, lightRight);
        float start = near;
        Span<float> splits = stackalloc float[ShadowCascades];
        Span<float> texels = stackalloc float[ShadowCascades];
        Span<float> depths = stackalloc float[ShadowCascades];
        for (int i = 0; i < ShadowCascades; i++)
        {
            float t = (i + 1f) / ShadowCascades;
            float end = i == ShadowCascades - 1 ? reach : .85f * near * MathF.Pow(reach / near, t) + .15f * (near + (reach - near) * t);
            // The sphere round the slice from start to end: its middle on the view's line, as far along as holds both
            // ends' corners, its size the same whichever way the camera turns.
            float along = Math.Min((start + end) * (1 + spread * spread) / 2, end);
            float size = MathF.Sqrt((end - along) * (end - along) + end * spread * end * spread);
            Vector3 middle = eye + forward * along;
            if (size >= radius || Vector3.Distance(middle, Vector3.Zero) + radius <= size)
                (middle, size) = (Vector3.Zero, radius);
            float texel = size * 2 / ShadowResolution;
            float x = Vector3.Dot(middle, lightRight), y = Vector3.Dot(middle, lightUp);
            middle += lightRight * (MathF.Round(x / texel) * texel - x) + lightUp * (MathF.Round(y / texel) * texel - y);
            // From the sun's side, beyond everything that casts into the slice; deep enough for the ground behind.
            float back = middle.Length() + radius * 2;
            float depth = back + middle.Length() + Math.Max(radius, size) + radius;
            Matrix4x4 view = Matrix4x4.CreateLookAt(middle + sun * back, middle, up);
            shadowMatrices[i] = view * Matrix4x4.CreateOrthographic(size * 2, size * 2, 0, depth);
            queue.WriteBuffer(shadowViewBuffers[i], 0, MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4x4>(in shadowMatrices[i])));
            splits[i] = end;
            texels[i] = texel;
            depths[i] = depth;
            start = end;
        }
        shadowSplits = new Vector4(splits[0], splits[1], splits[2], splits[3]);
        shadowTexels = new Vector4(texels[0], texels[1], texels[2], texels[3]);
        shadowDepths = new Vector4(depths[0], depths[1], depths[2], depths[3]);
    }

    /// <summary>How far from the camera, along its view, each of the shadow's cascades reaches, as last fitted.</summary>
    internal Vector4 ShadowSplits => shadowSplits;

    /// <summary>Each of the shadow's cascades' texel, in metres, as last fitted.</summary>
    internal Vector4 ShadowTexels => shadowTexels;

    private void WriteLight(bool shadows)
    {
        if (shadows)
            FitCascades();
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
        vectors[9] = new Vector4(bright.E, bright.F, bright.W, 1 / Timecycle.DisplayGamma);
        vectors[10] = new Vector4(dark.A, dark.B, dark.C, dark.D);
        vectors[11] = new Vector4(dark.E, dark.F, dark.W, 0);
        vectors[12] = new Vector4(ReflectionMips, 0, 0, 0);
        for (int i = 0; i < ShadowCascades; i++)
            MemoryMarshal.Write(MemoryMarshal.AsBytes(values[(52 + i * 16)..(68 + i * 16)]), in shadowMatrices[i]);
        vectors[29] = shadowSplits;
        vectors[30] = shadowTexels;
        vectors[31] = shadowDepths;
        vectors[32] = new Vector4(0, 0, ShadowResolution, shadows && key.Direction.Z > 0 ? 1 : 0);
        vectors[33] = Timecycle.BloomParameters(cycle);
        vectors[34] = new Vector4(cycle.OcclusionPed, cycle.PedRim, 0, 0);
        queue.WriteBuffer(lightBuffer!, 0, (ReadOnlySpan<float>)values);
    }

    /// <summary>The sun's shadow of the drawables, a cascade at a time: their solid and masked parts' depth from the sun.</summary>
    private void DrawShadow(CommandEncoder encoder)
    {
        for (int cascade = 0; cascade < ShadowCascades; cascade++)
        {
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [],
                DepthStencilAttachment = new RenderPassDepthStencilAttachment
                {
                    View = shadowLayers[cascade],
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1,
                },
            });
            pass.SetPipeline(shadowPipeline!);
            pass.SetBindGroup(0, shadowGroups[cascade]);
            pass.SetVertexBuffer(0, surfaceVertices!);
            pass.SetIndexBuffer(surfaceIndices!, IndexFormat.Uint32);
            for (int i = 0; i < surfaceParts.Length; i++)
                if (surfaceParts[i].Mode != AlphaMode.Blend && !CutOut(i) && partsShown[i])
                    pass.DrawIndexed(surfaceParts[i].Part.IndexCount, 1, surfaceParts[i].Part.FirstIndex, surfaceParts[i].Part.BaseVertex, 0);
            // Then the cut-out ones, each with its material for its diffuse map: only what the main pass keeps casts.
            pass.SetPipeline(maskedShadowPipeline!);
            for (int i = 0; i < surfaceParts.Length; i++)
                if (CutOut(i) && partsShown[i])
                {
                    pass.SetBindGroup(1, surfaceParts[i].Material);
                    pass.DrawIndexed(surfaceParts[i].Part.IndexCount, 1, surfaceParts[i].Part.FirstIndex, surfaceParts[i].Part.BaseVertex, 0);
                }
            pass.End();
        }

        // Terrain's material is laid out differently, and cuts nothing out.
        bool CutOut(int i) => surfaceParts[i].Mode == AlphaMode.Mask && !surfaceParts[i].Terrain;
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
