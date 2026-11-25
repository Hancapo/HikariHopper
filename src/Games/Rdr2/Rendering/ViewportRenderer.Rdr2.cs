using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Red Dead Redemption 2's drawables: their materials drawn with RedFury's (Rdr2/surface.wgsl over Rdr2/material.wgsl), lit by the
// viewer's light. Its pipelines are made the first time an RDR2 material is shown, so GTA V's drawables don't wait for
// its shader.
internal sealed unsafe partial class ViewportRenderer
{
    /// <summary>
    /// The textures RDR2's material reads in its fragment stage: its 24 slots, and Scene/light.wgsl's shadow and reflection
    /// maps. A GPU that can't bind as many draws its materials with Gta5/surface.wgsl's plain textured program instead.
    /// </summary>
    internal const uint Rdr2TextureSlots = Rdr2Material.Slots + 2;

    private bool? rdr2Supported;
    private BindGroupLayout? rdr2Layout;
    private RenderPipeline? rdr2Solid, rdr2DoubleSided, rdr2Blended, rdr2Reflected, rdr2BlendedReflected, rdr2MaskedShadow;
    private Sampler? clampSampler;
    private TextureView? black;
    // Each part's RDR2 material, when it's drawn with Rdr2/surface.wgsl.
    private Rdr2Material?[] partRdr2 = [];

    /// <summary>Whether the device binds as many textures as RDR2's material reads.</summary>
    internal bool Rdr2Supported
    {
        get
        {
            if (rdr2Supported is { } known)
                return known;
            var limits = new WGpuFury.WGPULimits();
            device.GetLimits(ref limits);
            rdr2Supported = limits.MaxSampledTexturesPerShaderStage >= Rdr2TextureSlots;
            return rdr2Supported.Value;
        }
    }

    // Whether a material is drawn with RDR2's material: one of RDR2's, on a device that can.
    private bool DrawsRdr2(SurfaceMaterial material)
    {
        if (material.Rdr2 is null || !Rdr2Supported)
            return false;
        CreateRdr2Pipelines();
        return true;
    }

    private void CreateRdr2Pipelines()
    {
        if (rdr2Layout is not null)
            return;
        var entries = new List<BindGroupLayoutEntry>
        {
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = Rdr2Material.UniformBytes },
            },
            new() { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
        };
        for (int slot = 0; slot < Rdr2Material.Slots; slot++)
            entries.Add(new BindGroupLayoutEntry
            {
                Binding = (uint)(slot + 2),
                // The tint palette is read per vertex.
                Visibility = slot == 12 ? ShaderStage.Vertex | ShaderStage.Fragment : ShaderStage.Fragment,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
            });
        entries.Add(new BindGroupLayoutEntry { Binding = 26, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } });
        rdr2Layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { Entries = [.. entries] });
        PipelineLayout own = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, rdr2Layout, lightLayout!] });
        PipelineLayout mirrored = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = [sceneLayout, rdr2Layout, lightLayout!, reflectionViewLayout!],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = LitShader("Rdr2/light.wgsl") + "\n" + Read("Rdr2/terrain_blending.wgsl") + "\n" + Read("Rdr2/cutout.wgsl") + "\n" + Read("Rdr2/material.wgsl") + "\n" + Read("Rdr2/vertex.wgsl") + "\n" + Read("Rdr2/surface.wgsl"),
        }));
        RenderPipeline Pipeline(CullMode cull, bool blend, bool reflected) =>
            gpu.Pipeline($"rdr2/{cull}/{blend}/{reflected}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = reflected ? mirrored : own,
                Vertex = new VertexState
                {
                    Module = shader.Value,
                    EntryPoint = reflected ? "vs_rdr2_paraboloid" : "vs_rdr2",
                    Buffers = [SurfaceVertexLayout()],
                },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.CCW, CullMode = cull },
                Fragment = new FragmentState
                {
                    Module = shader.Value,
                    EntryPoint = reflected ? "fs_rdr2_reflected" : "fs_rdr2",
                    Targets = ViewTargets(new ColorTargetState
                    {
                        Format = HdrFormat,
                        Blend = blend ? new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                        } : null,
                        WriteMask = ColorWriteMask.All,
                    }, reflected),
                },
                DepthStencil = new DepthStencilState
                {
                    Format = DepthFormat,
                    DepthWriteEnabled = blend ? OptionalBool.False : OptionalBool.True,
                    DepthCompare = reflected ? CompareFunction.LessEqual : CompareFunction.GreaterEqual,
                },
            }));
        rdr2Solid = Pipeline(CullMode.Back, blend: false, reflected: false);
        rdr2DoubleSided = Pipeline(CullMode.None, blend: false, reflected: false);
        rdr2Blended = Pipeline(CullMode.None, blend: true, reflected: false);
        rdr2Reflected = Pipeline(CullMode.None, blend: false, reflected: true);
        rdr2BlendedReflected = Pipeline(CullMode.None, blend: true, reflected: true);

        // Cut-out surfaces' shadow: their depth where RedFury's simple alpha test keeps them (Rdr2/shadow.wgsl).
        Lazy<ShaderModule> shadowShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("Rdr2/cutout.wgsl") + "\n" + Read("Rdr2/shadow.wgsl") }));
        rdr2MaskedShadow = gpu.Pipeline($"rdr2-shadow-masked/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout!, rdr2Layout] }),
            Vertex = new VertexState
            {
                Module = shadowShader.Value,
                EntryPoint = "vs_rdr2_shadow",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = SurfaceVertex.Size,
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                            new VertexAttribute { ShaderLocation = 5, Offset = 56, Format = VertexFormat.Unorm8x4 },
                        },
                    },
                },
            },
            Fragment = new FragmentState { Module = shadowShader.Value, EntryPoint = "fs_rdr2_shadow", Targets = [] },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.Less },
        }));
        // RedFury's clamped sampler, for the terrain's macro colour and lookup maps.
        clampSampler = device.CreateSampler(new SamplerDescriptor
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
        black = Solid(0, 0, 0, 255);
    }

    // The viewer's surface vertex, as every surface pipeline reads it (SurfaceVertex).
    private static VertexBufferLayout SurfaceVertexLayout() => new()
    {
        ArrayStride = SurfaceVertex.Size,
        Attributes = new[]
        {
            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
            new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x3 },
            new VertexAttribute { ShaderLocation = 2, Offset = 24, Format = VertexFormat.Float32x4 },
            new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
            new VertexAttribute { ShaderLocation = 4, Offset = 48, Format = VertexFormat.Float32x2 },
            new VertexAttribute { ShaderLocation = 5, Offset = 56, Format = VertexFormat.Unorm8x4 },
            new VertexAttribute { ShaderLocation = 6, Offset = 60, Format = VertexFormat.Unorm8x4 },
            new VertexAttribute { ShaderLocation = 7, Offset = 64, Format = VertexFormat.Float32x2 },
        },
    };

    /// <summary>
    /// An RDR2 material's group, with a uniform buffer of its own or the one it had: its slots as RedFury binds them, a
    /// colour slot through its sRGB view; a missing slot as RedFury fills it: a flat normal for a normal map, black for
    /// the emissive map, a checkerboard for a colour map it names that wasn't found, white otherwise.
    /// </summary>
    private BindGroup Rdr2Group(Rdr2Material material, Buffer? had = null, ulong offset = 0)
    {
        Buffer uniforms = had ?? device.CreateBuffer(new BufferDescriptor { Size = Rdr2Material.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        if (had is null)
        {
            materialBuffers.Add(uniforms);
            queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<byte>)material.Uniforms);
        }
        var entries = new List<BindGroupEntry>
        {
            new() { Binding = 0, Buffer = uniforms, Offset = offset, Size = Rdr2Material.UniformBytes },
            new() { Binding = 1, Sampler = surfaceSampler! },
        };
        for (int slot = 0; slot < Rdr2Material.Slots; slot++)
        {
            GameTexture? texture = material.Textures[slot];
            TextureView? view = material.Srgb(slot) ? SrgbView(texture) : View(texture);
            view ??= Rdr2Material.NormalSlots.Contains(slot) ? flat! : slot == 3 ? black! : material.RequiredTextures[slot] ? missing! : white!;
            entries.Add(new BindGroupEntry { Binding = (uint)(slot + 2), TextureView = view });
        }
        entries.Add(new BindGroupEntry { Binding = 26, Sampler = clampSampler! });
        return device.CreateBindGroup(new BindGroupDescriptor { Layout = rdr2Layout!, Entries = [.. entries] });
    }

    // A format's sRGB variant, where it has one.
    private static TextureFormat? SrgbOf(TextureFormat format) => format switch
    {
        TextureFormat.BC1RGBAUnorm => TextureFormat.BC1RGBAUnormSrgb,
        TextureFormat.BC2RGBAUnorm => TextureFormat.BC2RGBAUnormSrgb,
        TextureFormat.BC3RGBAUnorm => TextureFormat.BC3RGBAUnormSrgb,
        TextureFormat.BC7RGBAUnorm => TextureFormat.BC7RGBAUnormSrgb,
        TextureFormat.RGBA8Unorm => TextureFormat.RGBA8UnormSrgb,
        TextureFormat.BGRA8Unorm => TextureFormat.BGRA8UnormSrgb,
        _ => null,
    };

    /// <summary>A texture read as sRGB, decoded to linear as it's sampled; as stored where its format has no sRGB variant.</summary>
    private TextureView? SrgbView(GameTexture? texture)
    {
        if (View(texture) is not { } plain)
            return null;
        if (srgbViews.TryGetValue(texture!, out TextureView? known))
            return known;
        HikariHopper.Gpu.Texture gpuTexture = uploadedOf[texture!];
        TextureView view = SrgbOf(gpuTexture.GetFormat()) is { } srgb
            ? gpuTexture.CreateView(new TextureViewDescriptor
            {
                Format = srgb,
                Dimension = TextureViewDimension.D2,
                BaseMipLevel = 0,
                MipLevelCount = gpuTexture.GetMipLevelCount(),
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All,
            })
            : plain;
        srgbViews[texture!] = view;
        return view;
    }

    // Whether a part is an RDR2 surface cut out by its alpha, which casts through its material.
    private bool Rdr2CutOut(int i) => surfaceParts[i].Kind == PartKind.Rdr2 && surfaceParts[i].Mode == AlphaMode.Mask;

    /// <summary>RDR2's cut-out surfaces into a cascade of the sun's shadow, each through its material's alpha test.</summary>
    private void DrawRdr2Shadow(RenderPassEncoder pass)
    {
        if (rdr2MaskedShadow is null)
            return;
        bool any = false;
        for (int i = 0; i < surfaceParts.Length; i++)
            if (Rdr2CutOut(i) && partsShown[i])
            {
                if (!any)
                    pass.SetPipeline(rdr2MaskedShadow);
                any = true;
                pass.SetBindGroup(1, surfaceParts[i].Material);
                pass.DrawIndexed(surfaceParts[i].Part.IndexCount, 1, surfaceParts[i].Part.FirstIndex, surfaceParts[i].Part.BaseVertex, 0);
            }
    }
}
