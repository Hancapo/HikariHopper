using System.Numerics;
using System.Runtime.InteropServices;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// Drawables' light, after RedFury: the day's key light through the atmosphere, its shadow, and the sky's irradiance,
// with drawables drawn in HDR and tone-mapped over the backdrop. Collisions, navmeshes and the backdrop keep their
// own flat shading, in the theme's colours.
//   sky tables (once) -> shadow map -> backdrop and lines to the display -> drawables to HDR -> tone map over them
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat HdrFormat = TextureFormat.RGBA16Float;
    private const TextureFormat TableFormat = TextureFormat.RGBA16Float;
    private const int SkyBytes = 48, HarmonicsBytes = 9 * 16, LightBytes = 112, ShadowViewBytes = 64;
    internal const int ShadowResolution = 2048;

    private BindGroupLayout? lightLayout;
    private BindGroup? lightGroup;
    private Buffer? lightBuffer;
    private Buffer? harmonics;
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
    private Vector3 groundIlluminance;
    private Matrix4x4 shadowMatrix;
    private float shadowTexel, shadowDepth;

    /// <summary>The time of day drawables are lit at, in hours after midnight.</summary>
    public float Hours { get; private set; } = DayCycle.DefaultHours;

    /// <summary>The exposure, in stops; RedFury's eye adaptation adds to it at dusk and night.</summary>
    public float Exposure { get; set; }

    private void CreateLighting()
    {
        lightBuffer = device.CreateBuffer(new BufferDescriptor { Size = LightBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        harmonics = device.CreateBuffer(new BufferDescriptor { Size = HarmonicsBytes, Usage = BufferUsage.Storage });
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
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = HarmonicsBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Comparison } },
            ],
        });
        lightGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = lightLayout,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = lightBuffer, Size = LightBytes },
                new BindGroupEntry { Binding = 1, Buffer = harmonics, Size = HarmonicsBytes },
                new BindGroupEntry { Binding = 2, TextureView = shadowView },
                new BindGroupEntry { Binding = 3, Sampler = compare },
            ],
        });

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
            ],
        });
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

    /// <summary>Lights drawables at this time of day: the sun's place and colour, and the sky's irradiance with it.</summary>
    public void SetTime(float hours)
    {
        Hours = hours;
        key = DayCycle.Evaluate(hours);
        // Direct light at the ground: the sun's or moon's top-of-atmosphere illuminance through the air.
        groundIlluminance = key.Illuminance * Atmosphere.SunTransmittance(2, key.Direction);
        ComputeSky();
    }

    /// <summary>RedFury's sky tables for this key light, and the harmonics of the sky's irradiance from them.</summary>
    private void ComputeSky()
    {
        Texture Table(uint width, uint height) => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.StorageBinding | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(width, height, 1),
            Format = TableFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        Texture transmittance = Table(256, 64), multiple = Table(32, 32), view = Table(192, 108);
        Buffer skyBuffer = device.CreateBuffer(new BufferDescriptor { Size = SkyBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        Span<Vector4> sky = stackalloc Vector4[SkyBytes / 16];
        sky[0] = new Vector4(key.Direction, 0);
        sky[1] = new Vector4(key.Illuminance, 0);
        // The eye a couple of metres above the ground.
        sky[2] = new Vector4(0, 0, 2, 0);
        queue.WriteBuffer(skyBuffer, 0, (ReadOnlySpan<Vector4>)sky);
        Sampler clamp = device.CreateSampler(new SamplerDescriptor
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
        })!;

        BindGroupLayout skyLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = SkyBytes },
                },
            ],
        });
        BindGroup skyGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = skyLayout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = skyBuffer, Size = SkyBytes }],
        });
        static BindGroupLayoutEntry Sampled(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        static BindGroupLayoutEntry Filtering(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        static BindGroupLayoutEntry Output(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout { Access = StorageTextureAccess.WriteOnly, Format = TableFormat, ViewDimension = TextureViewDimension.D2 },
        };
        ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("sky.wgsl") + "\n" + Read("sky_luts.wgsl") });
        (ComputePipeline Pipeline, BindGroup Group) Pass(string entry, BindGroupLayoutEntry[] layoutEntries, BindGroupEntry[] entries)
        {
            BindGroupLayout layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = layoutEntries });
            ComputePipeline pipeline = device.CreateComputePipelineSync(new ComputePipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [skyLayout, layout] }),
                Compute = new ComputeState { Module = shader, EntryPoint = entry },
            });
            return (pipeline, device.CreateBindGroup(new BindGroupDescriptor { Layout = layout, Entries = entries }));
        }
        var transmittancePass = Pass("cs_transmittance", [Output(3)], [new BindGroupEntry { Binding = 3, TextureView = transmittance.CreateView() }]);
        var multiplePass = Pass("cs_multiple_scattering", [Sampled(0), Filtering(2), Output(3)],
        [
            new BindGroupEntry { Binding = 0, TextureView = transmittance.CreateView() },
            new BindGroupEntry { Binding = 2, Sampler = clamp },
            new BindGroupEntry { Binding = 3, TextureView = multiple.CreateView() },
        ]);
        var viewPass = Pass("cs_sky_view", [Sampled(0), Sampled(1), Filtering(2), Output(3)],
        [
            new BindGroupEntry { Binding = 0, TextureView = transmittance.CreateView() },
            new BindGroupEntry { Binding = 1, TextureView = multiple.CreateView() },
            new BindGroupEntry { Binding = 2, Sampler = clamp },
            new BindGroupEntry { Binding = 3, TextureView = view.CreateView() },
        ]);
        var irradiancePass = Pass("cs_sky_irradiance",
        [
            Filtering(2), Sampled(5),
            new BindGroupLayoutEntry
            {
                Binding = 6,
                Visibility = ShaderStage.Compute,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = HarmonicsBytes },
            },
        ],
        [
            new BindGroupEntry { Binding = 2, Sampler = clamp },
            new BindGroupEntry { Binding = 5, TextureView = view.CreateView() },
            new BindGroupEntry { Binding = 6, Buffer = harmonics!, Size = HarmonicsBytes },
        ]);

        CommandEncoder encoder = device.CreateCommandEncoder();
        ComputePassEncoder pass = encoder.BeginComputePass();
        void Dispatch((ComputePipeline Pipeline, BindGroup Group) step, uint x, uint y)
        {
            pass.SetPipeline(step.Pipeline);
            pass.SetBindGroup(0, skyGroup);
            pass.SetBindGroup(1, step.Group);
            pass.DispatchWorkgroups(x, y, 1);
        }
        Dispatch(transmittancePass, 256 / 8, 64 / 8);
        Dispatch(multiplePass, 32, 32);
        Dispatch(viewPass, (192 + 7) / 8, (108 + 7) / 8);
        Dispatch(irradiancePass, 1, 1);
        pass.End();
        queue.Submit(encoder.Finish());
        ThrowIfFailed();
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
            Entries = [new BindGroupEntry { Binding = 0, TextureView = hdrView }],
        });
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
        vectors[0] = new Vector4(key.Direction, MathF.Pow(2, Exposure));
        vectors[1] = new Vector4(groundIlluminance, 0);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(values[8..24]), in shadowMatrix);
        vectors[6] = new Vector4(shadowTexel, shadowDepth, ShadowResolution, shadows && key.Direction.Z > 0 ? 1 : 0);
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

    /// <summary>A shader lit by the sky: scene.wgsl, light.wgsl, then its own file.</summary>
    private static string LitShader(string name) => Read("scene.wgsl") + "\n" + Read("light.wgsl") + "\n" + Read(name);
}
