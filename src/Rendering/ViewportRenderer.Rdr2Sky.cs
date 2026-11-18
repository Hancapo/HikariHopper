using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Red Dead Redemption 2's sky behind its world (rdr2_sky_tables.wgsl, rdr2_sky.wgsl): the game's precomputed
// atmosphere, its tables built on the GPU as its sky system builds them (the transmittance, the single scattering, then
// each later order gathered, marched and added in) whenever the air they're made for changes, then the game's dome drawn
// from them first in the world's HDR pass, with the sun's and the moon's scattering, the stars, the air glow and the
// discs, after which the sky covers the frame; and its aerial perspective over the world (rdr2_aerial.wgsl), once its
// solid surfaces are drawn. Only once an RDR2 world has given its sky (ShowRdr2Sky); until then the backdrop shows behind
// it as before.
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat Rdr2TableFormat = TextureFormat.RGBA16Float;

    // The tables' sizes: the transmittance's, the scattering's (8 cosines to the sun of 32 sun heights, 64 view cosines,
    // 32 heights) and the gathered light's.
    private static readonly Extent3D Rdr2TransmittanceSize = new(128, 32, 1), Rdr2ScatteringSize = new(256, 64, 32), Rdr2GatherSize = new(32, 32, 32);

    /// <summary>
    /// The RDR2 sky's GPU side: its tables, the totals' two (added into in turn), the dome's mesh, its textures, and its
    /// groups.
    /// </summary>
    private sealed record Rdr2SkyResources(Rdr2Sky Sky, WebGpuSharp.Texture Transmittance, WebGpuSharp.Texture Delta, WebGpuSharp.Texture Gathered,
        WebGpuSharp.Texture[] Totals, BindGroup[] Passes, BindGroup[] Domes, Buffer Tables, Buffer Dome, Buffer Vertices, Buffer Indices,
        uint IndexCount, WebGpuSharp.Texture[] Textures, Buffer Light, WebGpuSharp.Texture[] GatheredSums, BindGroup[] GatheredAdds,
        WebGpuSharp.Texture[] Aerial, BindGroup AerialOut, TextureView[] AerialViews);

    // The aerial perspective's grid: 32 by 32 over the frame, 32 slices out to its far clip.
    private static readonly Extent3D Rdr2AerialSize = new(32, 32, 32);

    // The height fog's: 64 by 36 over the frame, 64 slices out to the far clip.
    private static readonly Extent3D Rdr2FogSize = new(64, 36, 64);

    // Which of the gathered light's sums the last order was added into, as the orders are always as many.
    private const int Rdr2GatheredSum = (Rdr2Sky.Orders - 2) % 2;

    // The sky's light on the world: the key light's illuminance and the ambient cube's six faces (rdr2_sky.wgsl).
    private const int Rdr2SkyLightBytes = 7 * 16;

    private Rdr2SkyResources? rdr2Sky;
    private BindGroupLayout? rdr2TransmittanceLayout, rdr2SingleLayout, rdr2GatherLayout, rdr2OrderLayout, rdr2AccumulateLayout, rdr2DomeLayout;
    private ComputePipeline? rdr2TransmittancePipeline, rdr2SinglePipeline, rdr2GatherPipeline, rdr2OrderPipeline, rdr2AccumulatePipeline;
    private RenderPipeline? rdr2DomePipeline;
    private ComputePipeline? rdr2SkyLightPipeline, rdr2AerialPipeline, rdr2FogPipeline;
    private BindGroupLayout? rdr2NothingLayout, rdr2AerialOutLayout, rdr2AerialApplyLayout;
    private BindGroup? rdr2Nothing, rdr2AerialApply;
    private RenderPipeline? rdr2AerialTransmittancePipeline, rdr2AerialInscatterPipeline;
    // The depth the aerial perspective's apply group reads, made again only when the depth is (a resize).
    private TextureView? rdr2AerialDepth;
    private Sampler? rdr2Sampler, rdr2Wrap;
    private readonly Vector4[] rdr2Values = new Vector4[Rdr2Sky.Vectors], rdr2Built = new Vector4[Rdr2Sky.Vectors];
    private bool rdr2TablesBuilt;
    // Which of the totals the last order was added into, which the dome reads.
    private int rdr2Total;

    /// <summary>Whether an RDR2 world's sky is shown behind it.</summary>
    public bool HasRdr2Sky => rdr2Sky is not null;

    /// <summary>
    /// How an RDR2 world shown with its sky is exposed and tone-mapped at this hour, as the game does, in its own units;
    /// null otherwise.
    /// </summary>
    internal Rdr2Exposure? Rdr2WorldExposure => worldShown && rdr2Sky is { } sky ? sky.Sky.Exposure(Hours) : null;

    /// <summary>How many times the sky's tables have been built; for tests.</summary>
    internal int Rdr2SkyBuilds { get; private set; }

    /// <summary>
    /// The sky's light on the world as last worked out (the key light's illuminance, then the ambient cube's faces), read
    /// back, for tests; none without the sky.
    /// </summary>
    internal Vector4[] Rdr2SkyLight()
    {
        if (rdr2Sky is not { } sky)
            return [];
        Buffer readback = device.CreateBuffer(new BufferDescriptor { Size = Rdr2SkyLightBytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.CopyBufferToBuffer(sky.Light, 0, readback, 0, Rdr2SkyLightBytes);
        queue.Submit(encoder.Finish());
        readback.MapSync(MapMode.Read);
        Vector4[] values = readback.GetConstMappedRange<Vector4[], Vector4>(0, 7, span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        return values;
    }

    private void CreateRdr2SkyPipelines()
    {
        if (rdr2DomePipeline is not null)
            return;
        static BindGroupLayoutEntry Constants(uint binding, ShaderStage stage, ulong size = Rdr2Sky.TableBytes) => new()
        {
            Binding = binding,
            Visibility = stage,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = size },
        };
        static BindGroupLayoutEntry Filtering(uint binding, ShaderStage stage) =>
            new() { Binding = binding, Visibility = stage, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } };
        static BindGroupLayoutEntry Table(uint binding, TextureViewDimension dimension, ShaderStage stage) => new()
        {
            Binding = binding,
            Visibility = stage,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = dimension },
        };
        static BindGroupLayoutEntry Output(uint binding, TextureViewDimension dimension) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout { Access = StorageTextureAccess.WriteOnly, Format = Rdr2TableFormat, ViewDimension = dimension },
        };
        const ShaderStage Compute = ShaderStage.Compute;
        BindGroupLayout Layout(params BindGroupLayoutEntry[] entries) => device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = entries });
        rdr2TransmittanceLayout = Layout(Constants(0, Compute), Output(5, TextureViewDimension.D2));
        rdr2SingleLayout = Layout(Constants(0, Compute), Filtering(1, Compute), Table(2, TextureViewDimension.D2, Compute), Output(6, TextureViewDimension.D3));
        rdr2GatherLayout = Layout(Constants(0, Compute), Filtering(1, Compute), Table(3, TextureViewDimension.D3, Compute), Output(6, TextureViewDimension.D3));
        rdr2OrderLayout = rdr2GatherLayout;
        rdr2AccumulateLayout = Layout(Constants(0, Compute), Table(3, TextureViewDimension.D3, Compute), Table(4, TextureViewDimension.D3, Compute),
            Output(6, TextureViewDimension.D3));
        // The dome's, which its light's pass reads too.
        const ShaderStage Fragment = ShaderStage.Fragment | ShaderStage.Compute;
        rdr2DomeLayout = Layout(Constants(0, ShaderStage.Vertex | Fragment, Rdr2Sky.Bytes), Filtering(1, Fragment), Table(2, TextureViewDimension.D2, Fragment),
            Table(3, TextureViewDimension.D3, Fragment), Filtering(4, Fragment), Table(5, TextureViewDimension.D2, Fragment),
            Table(6, TextureViewDimension.D2, Fragment), Table(7, TextureViewDimension.D2, Fragment), new BindGroupLayoutEntry
            {
                Binding = 8,
                Visibility = ShaderStage.Compute,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = Rdr2SkyLightBytes },
            }, Table(9, TextureViewDimension.D3, Fragment));
        rdr2AerialOutLayout = Layout(Output(0, TextureViewDimension.D3), Output(1, TextureViewDimension.D3), Output(2, TextureViewDimension.D3),
            Output(3, TextureViewDimension.D3));
        rdr2AerialApplyLayout = Layout(Constants(0, ShaderStage.Fragment, Rdr2Sky.Bytes), Filtering(1, ShaderStage.Fragment), new BindGroupLayoutEntry
        {
            Binding = 10,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
        }, Table(11, TextureViewDimension.D3, ShaderStage.Fragment), Table(12, TextureViewDimension.D3, ShaderStage.Fragment),
            Table(13, TextureViewDimension.D3, ShaderStage.Fragment), Table(14, TextureViewDimension.D3, ShaderStage.Fragment));
        rdr2NothingLayout = Layout();
        rdr2Nothing = device.CreateBindGroup(new BindGroupDescriptor { Layout = rdr2NothingLayout, Entries = [] });

        Lazy<ShaderModule> tables = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("rdr2_atmosphere.wgsl") + "\n" + Read("rdr2_sky_tables.wgsl"),
        }));
        ComputePipeline Pass(string entry, BindGroupLayout layout) => gpu.Compute($"rdr2-sky/{entry}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] }),
            Compute = new ComputeState { Module = tables.Value, EntryPoint = entry },
        }));
        rdr2TransmittancePipeline = Pass("cs_transmittance", rdr2TransmittanceLayout);
        rdr2SinglePipeline = Pass("cs_single_scatter", rdr2SingleLayout);
        rdr2GatherPipeline = Pass("cs_gather_light", rdr2GatherLayout);
        rdr2OrderPipeline = Pass("cs_scattering_order", rdr2OrderLayout);
        rdr2AccumulatePipeline = Pass("cs_accumulate", rdr2AccumulateLayout);
        Lazy<ShaderModule> dome = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("rdr2_atmosphere.wgsl") + "\n" + Read("rdr2_sky.wgsl"),
        }));
        rdr2SkyLightPipeline = gpu.Compute("rdr2-sky/light", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [rdr2NothingLayout, rdr2DomeLayout] }),
            Compute = new ComputeState { Module = dome.Value, EntryPoint = "cs_rdr2_sky_light" },
        }));
        Lazy<ShaderModule> aerial = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("rdr2_atmosphere.wgsl") + "\n" + Read("rdr2_sky.wgsl") + "\n" + Read("rdr2_aerial.wgsl"),
        }));
        rdr2AerialPipeline = gpu.Compute("rdr2-sky/aerial", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [rdr2NothingLayout, rdr2DomeLayout, rdr2AerialOutLayout] }),
            Compute = new ComputeState { Module = aerial.Value, EntryPoint = "cs_rdr2_aerial" },
        }));
        rdr2FogPipeline = gpu.Compute("rdr2-sky/fog", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [rdr2NothingLayout, rdr2DomeLayout, rdr2AerialOutLayout] }),
            Compute = new ComputeState { Module = aerial.Value, EntryPoint = "cs_rdr2_fog" },
        }));
        // The frame times the transmittance, then plus the light scattered in; colour alone, the sky's cover kept.
        RenderPipeline Apply(string entry, BlendState blend) => gpu.Pipeline($"rdr2-sky/{entry}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, rdr2AerialApplyLayout] }),
            Vertex = new VertexState { Module = aerial.Value, EntryPoint = "vs_rdr2_aerial" },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = aerial.Value,
                EntryPoint = entry,
                Targets = HdrTargets(new ColorTargetState
                {
                    Format = HdrFormat,
                    Blend = blend,
                    WriteMask = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue,
                }),
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        }));
        static BlendState Blend(BlendFactor source, BlendFactor destination) => new()
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = source, DstFactor = destination },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
        };
        rdr2AerialTransmittancePipeline = Apply("fs_rdr2_aerial_transmittance", Blend(BlendFactor.Zero, BlendFactor.Src));
        rdr2AerialInscatterPipeline = Apply("fs_rdr2_aerial_inscatter", Blend(BlendFactor.One, BlendFactor.One));
        rdr2DomePipeline = gpu.Pipeline("rdr2-sky/dome", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, rdr2DomeLayout] }),
            Vertex = new VertexState
            {
                Module = dome.Value,
                EntryPoint = "vs_rdr2_sky",
                Buffers =
                [
                    new VertexBufferLayout
                    {
                        ArrayStride = Rdr2SkyDome.Stride,
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x2 },
                        },
                    },
                ],
            },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = dome.Value,
                EntryPoint = "fs_rdr2_sky",
                Targets = HdrTargets(new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All }),
            },
            // At the far plane, behind whatever the world draws over it.
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        }));
    }

    /// <summary>Shows this sky behind an RDR2 world, in place of any before; null shows none (the backdrop again).</summary>
    public void ShowRdr2Sky(Rdr2Sky? sky)
    {
        ClearRdr2Sky();
        if (sky is null)
            return;
        CreateRdr2SkyPipelines();
        rdr2Sampler ??= device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        });
        rdr2Wrap ??= device.CreateSampler(new SamplerDescriptor
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
        WebGpuSharp.Texture Table(Extent3D size, TextureUsage extra = TextureUsage.None) => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.StorageBinding | TextureUsage.TextureBinding | extra,
            Dimension = size.DepthOrArrayLayers > 1 ? TextureDimension.D3 : TextureDimension.D2,
            Size = size,
            Format = Rdr2TableFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        WebGpuSharp.Texture transmittance = Table(Rdr2TransmittanceSize), delta = Table(Rdr2ScatteringSize, TextureUsage.CopySrc),
            gathered = Table(Rdr2GatherSize, TextureUsage.CopySrc);
        WebGpuSharp.Texture[] gatheredSums = [Table(Rdr2GatherSize, TextureUsage.CopyDst), Table(Rdr2GatherSize, TextureUsage.CopyDst)];
        WebGpuSharp.Texture[] aerial = [Table(Rdr2AerialSize), Table(Rdr2AerialSize), Table(Rdr2FogSize), Table(Rdr2FogSize)];
        WebGpuSharp.Texture[] totals = [Table(Rdr2ScatteringSize, TextureUsage.CopyDst), Table(Rdr2ScatteringSize, TextureUsage.CopyDst)];
        Buffer tables = device.CreateBuffer(new BufferDescriptor { Size = Rdr2Sky.TableBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        Buffer dome = device.CreateBuffer(new BufferDescriptor { Size = Rdr2Sky.Bytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        TextureView transmittanceView = transmittance.CreateView(), deltaView = delta.CreateView(), gatheredView = gathered.CreateView();
        TextureView[] totalViews = [totals[0].CreateView(), totals[1].CreateView()];
        TextureView[] sumViews = [gatheredSums[0].CreateView(), gatheredSums[1].CreateView()];
        TextureView[] aerialViews = [.. aerial.Select(texture => texture.CreateView())];
        BindGroup Group(BindGroupLayout layout, params BindGroupEntry[] entries) =>
            device.CreateBindGroup(new BindGroupDescriptor { Layout = layout, Entries = entries });
        BindGroupEntry Uniform(Buffer buffer, ulong size = Rdr2Sky.TableBytes) => new() { Binding = 0, Buffer = buffer, Size = size };
        BindGroupEntry Sampling() => new() { Binding = 1, Sampler = rdr2Sampler };
        static BindGroupEntry View(uint binding, TextureView view) => new() { Binding = binding, TextureView = view };
        BindGroup[] passes =
        [
            Group(rdr2TransmittanceLayout!, Uniform(tables), View(5, transmittanceView)),
            Group(rdr2SingleLayout!, Uniform(tables), Sampling(), View(2, transmittanceView), View(6, deltaView)),
            Group(rdr2GatherLayout!, Uniform(tables), Sampling(), View(3, deltaView), View(6, gatheredView)),
            Group(rdr2OrderLayout!, Uniform(tables), Sampling(), View(3, gatheredView), View(6, deltaView)),
            // Each order added from one total into the other.
            Group(rdr2AccumulateLayout!, Uniform(tables), View(3, totalViews[0]), View(4, deltaView), View(6, totalViews[1])),
            Group(rdr2AccumulateLayout!, Uniform(tables), View(3, totalViews[1]), View(4, deltaView), View(6, totalViews[0])),
        ];
        // Each order's gathered light added from one sum into the other.
        BindGroup[] gatheredAdds =
        [
            Group(rdr2AccumulateLayout!, Uniform(tables), View(3, sumViews[0]), View(4, gatheredView), View(6, sumViews[1])),
            Group(rdr2AccumulateLayout!, Uniform(tables), View(3, sumViews[1]), View(4, gatheredView), View(6, sumViews[0])),
        ];
        BindGroup aerialOut = Group(rdr2AerialOutLayout!, View(0, aerialViews[0]), View(1, aerialViews[1]), View(2, aerialViews[2]),
            View(3, aerialViews[3]));
        // The game's dome and its textures, or a sphere where its files haven't them, with nothing on it.
        Rdr2SkyDome shape = sky.Dome ?? Rdr2SkyDome.Sphere();
        var owned = new List<WebGpuSharp.Texture>();
        TextureView Own(GameTexture? source)
        {
            if (source is null || CreateTexture(source) is not var (texture, block))
                return clear!;
            for (int level = 0; level < source.MipCount; level++)
                WriteLevel(source, texture, block, level);
            owned.Add(texture);
            return texture.CreateView();
        }
        TextureView stars = Own(shape.Stars), airGlow = Own(shape.AirGlow), moon = Own(shape.Moon);
        Buffer vertices = Upload((ReadOnlySpan<float>)shape.Vertices, sizeof(float));
        Buffer indices = Upload((ReadOnlySpan<uint>)shape.Indices, sizeof(uint), BufferUsage.Index);
        Buffer light = device.CreateBuffer(new BufferDescriptor { Size = Rdr2SkyLightBytes, Usage = BufferUsage.Storage | BufferUsage.CopySrc });
        BindGroup Dome(TextureView total) => Group(rdr2DomeLayout!, Uniform(dome, Rdr2Sky.Bytes), Sampling(), View(2, transmittanceView),
            View(3, total), new BindGroupEntry { Binding = 4, Sampler = rdr2Wrap }, View(5, stars), View(6, airGlow), View(7, moon),
            new BindGroupEntry { Binding = 8, Buffer = light, Size = Rdr2SkyLightBytes }, View(9, sumViews[Rdr2GatheredSum]));
        BindGroup[] domes = [Dome(totalViews[0]), Dome(totalViews[1])];
        rdr2Sky = new Rdr2SkyResources(sky, transmittance, delta, gathered, totals, passes, domes, tables, dome, vertices, indices,
            (uint)shape.Indices.Length, [.. owned], light, gatheredSums, gatheredAdds, aerial, aerialOut, aerialViews);
        rdr2TablesBuilt = false;
        rdr2AerialDepth = null;
    }

    // The sky's GPU memory goes now, not whenever the handles are collected.
    private void ClearRdr2Sky()
    {
        if (rdr2Sky is not { } sky)
            return;
        rdr2Sky = null;
        rdr2AerialApply = null;
        foreach (WebGpuSharp.Texture texture in (ReadOnlySpan<WebGpuSharp.Texture>)[sky.Transmittance, sky.Delta, sky.Gathered, .. sky.Totals, .. sky.Textures,
            .. sky.GatheredSums, .. sky.Aerial])
            texture.Destroy();
        foreach (Buffer buffer in (ReadOnlySpan<Buffer>)[sky.Tables, sky.Dome, sky.Vertices, sky.Indices, sky.Light])
            buffer.Destroy();
        // The world's lit by the viewer's light again.
        if (lightBuffer is not null)
            queue.WriteBuffer(lightBuffer, LightWrittenBytes, (ReadOnlySpan<byte>)new byte[Rdr2SkyLightBytes]);
    }

    /// <summary>
    /// The sky's constants for this frame, and its tables built again where the air they're made for changed, before the
    /// world's HDR pass; false where no RDR2 sky is shown.
    /// </summary>
    private bool PrepareRdr2Sky(CommandEncoder encoder)
    {
        if (!worldShown || rdr2Sky is not { } sky)
            return false;
        sky.Sky.Write(rdr2Values, Hours, DayCycle.SunDirection(Hours, Sky), DayCycle.MoonDirection(Hours, Sky), key.Sun, Camera.Eye,
            waterClock.Elapsed.TotalSeconds);
        // The camera's rays, for the aerial perspective.
        rdr2Values[Rdr2Sky.CameraRays] = new Vector4(Camera.Forward, sky.Sky.AerialFarClip);
        rdr2Values[Rdr2Sky.CameraRays + 1] = new Vector4(Camera.Right * (Aspect * OrbitCamera.TanHalf), 0);
        rdr2Values[Rdr2Sky.CameraRays + 2] = new Vector4(Camera.Up * OrbitCamera.TanHalf, Camera.Near);
        queue.WriteBuffer(sky.Dome, 0, (ReadOnlySpan<Vector4>)rdr2Values);
        if (!rdr2TablesBuilt || !Rdr2Sky.SameTables(rdr2Values, rdr2Built))
            BuildRdr2SkyTables(encoder, sky);
        // The sky's light on the world, from the tables, into the light the world's drawn with.
        ComputePassEncoder light = encoder.BeginComputePass();
        light.SetPipeline(rdr2SkyLightPipeline!);
        light.SetBindGroup(0, rdr2Nothing!);
        light.SetBindGroup(1, sky.Domes[rdr2Total]);
        light.DispatchWorkgroups(1, 1, 1);
        // The aerial perspective's grid, from the camera.
        light.SetPipeline(rdr2AerialPipeline!);
        light.SetBindGroup(2, sky.AerialOut);
        light.DispatchWorkgroups(Rdr2AerialSize.Width / 8, Rdr2AerialSize.Height / 8, 1);
        // And the height fog's, lit by the sky's light just worked out.
        light.SetPipeline(rdr2FogPipeline!);
        light.DispatchWorkgroups((Rdr2FogSize.Width + 7) / 8, (Rdr2FogSize.Height + 7) / 8, 1);
        light.End();
        encoder.CopyBufferToBuffer(sky.Light, 0, lightBuffer!, LightWrittenBytes, Rdr2SkyLightBytes);
        return true;
    }

    // The tables, built again for the air they're now made for.
    private void BuildRdr2SkyTables(CommandEncoder encoder, Rdr2SkyResources sky)
    {
        rdr2Values.CopyTo(rdr2Built.AsSpan());
        rdr2TablesBuilt = true;
        Rdr2SkyBuilds++;
        queue.WriteBuffer(sky.Tables, 0, ((ReadOnlySpan<Vector4>)rdr2Values)[..Rdr2Sky.TableVectors]);
        static uint Groups(uint size, uint kernel) => (size + kernel - 1) / kernel;
        ComputePassEncoder pass = encoder.BeginComputePass();
        pass.SetPipeline(rdr2TransmittancePipeline!);
        pass.SetBindGroup(0, sky.Passes[0]);
        pass.DispatchWorkgroups(Groups(Rdr2TransmittanceSize.Width, 2), Groups(Rdr2TransmittanceSize.Height, 32), 1);
        pass.SetPipeline(rdr2SinglePipeline!);
        pass.SetBindGroup(0, sky.Passes[1]);
        DispatchScattering(pass);
        pass.End();
        // The single scattering is the first total.
        encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = sky.Delta }, new TexelCopyTextureInfo { Texture = sky.Totals[0] }, Rdr2ScatteringSize);
        int total = 0, sum = 0;
        pass = encoder.BeginComputePass();
        for (int order = 1; order < Rdr2Sky.Orders; order++)
        {
            pass.SetPipeline(rdr2GatherPipeline!);
            pass.SetBindGroup(0, sky.Passes[2]);
            DispatchGathered(pass);
            // The gathered light added up over the orders: the first's as it is, each later one's by the factor.
            if (order == 1)
            {
                pass.End();
                encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = sky.Gathered }, new TexelCopyTextureInfo { Texture = sky.GatheredSums[0] },
                    Rdr2GatherSize);
                pass = encoder.BeginComputePass();
            }
            else
            {
                pass.SetPipeline(rdr2AccumulatePipeline!);
                pass.SetBindGroup(0, sky.GatheredAdds[sum]);
                DispatchGathered(pass);
                sum = 1 - sum;
            }
            pass.SetPipeline(rdr2OrderPipeline!);
            pass.SetBindGroup(0, sky.Passes[3]);
            DispatchScattering(pass);
            pass.SetPipeline(rdr2AccumulatePipeline!);
            pass.SetBindGroup(0, sky.Passes[4 + total]);
            DispatchScattering(pass);
            total = 1 - total;
        }
        pass.End();
        rdr2Total = total;

        static void DispatchScattering(ComputePassEncoder pass) => pass.DispatchWorkgroups(Groups(Rdr2ScatteringSize.Width, 4),
            Groups(Rdr2ScatteringSize.Height, 4), Groups(Rdr2ScatteringSize.DepthOrArrayLayers, 4));
        static void DispatchGathered(ComputePassEncoder pass) => pass.DispatchWorkgroups(Groups(Rdr2GatherSize.Width, 4),
            Groups(Rdr2GatherSize.Height, 4), Groups(Rdr2GatherSize.DepthOrArrayLayers, 4));
    }

    /// <summary>Whether the aerial perspective is laid over the world's solid surfaces: an RDR2 world shown with its sky.</summary>
    private bool Rdr2Aerial => worldShown && rdr2Sky is not null && rdr2TablesBuilt;

    /// <summary>
    /// The aerial perspective over what's drawn, in a pass that reads the world's depth: the frame times the
    /// transmittance from the camera, then plus the light scattered in on the way.
    /// </summary>
    private void DrawRdr2Aerial(RenderPassEncoder pass)
    {
        if (rdr2Sky is not { } sky)
            return;
        if (rdr2AerialApply is null || !ReferenceEquals(rdr2AerialDepth, hdrDepthView))
        {
            rdr2AerialDepth = hdrDepthView;
            rdr2AerialApply = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = rdr2AerialApplyLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, Buffer = sky.Dome, Size = Rdr2Sky.Bytes },
                    new BindGroupEntry { Binding = 1, Sampler = rdr2Sampler },
                    new BindGroupEntry { Binding = 10, TextureView = hdrDepthView! },
                    new BindGroupEntry { Binding = 11, TextureView = sky.AerialViews[0] },
                    new BindGroupEntry { Binding = 12, TextureView = sky.AerialViews[1] },
                    new BindGroupEntry { Binding = 13, TextureView = sky.AerialViews[2] },
                    new BindGroupEntry { Binding = 14, TextureView = sky.AerialViews[3] },
                ],
            });
        }
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, rdr2AerialApply);
        pass.SetPipeline(rdr2AerialTransmittancePipeline!);
        pass.Draw(3);
        pass.SetPipeline(rdr2AerialInscatterPipeline!);
        pass.Draw(3);
    }

    /// <summary>The sky, first in the HDR pass: the game's dome with the night and the discs, covering the frame.</summary>
    private void DrawRdr2Sky(RenderPassEncoder pass)
    {
        if (rdr2Sky is not { } sky)
            return;
        pass.SetPipeline(rdr2DomePipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, sky.Domes[rdr2Total]);
        pass.SetVertexBuffer(0, sky.Vertices);
        pass.SetIndexBuffer(sky.Indices, IndexFormat.Uint32);
        pass.DrawIndexed(sky.IndexCount);
    }
}
