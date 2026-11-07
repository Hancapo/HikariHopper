using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameFormat = CommonFury.Textures.TextureFormat;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Drawables: their surfaces drawn with surface.wgsl into the HDR target, solid parts first, then blended ones from
// far to near; ViewportRenderer.Lighting.cs lights and tone-maps them.
internal sealed unsafe partial class ViewportRenderer
{
    private readonly bool compressedTextures;
    private BindGroupLayout? materialLayout;
    private RenderPipeline? solidSurfaces;
    private RenderPipeline? doubleSidedSurfaces;
    private RenderPipeline? blendedSurfaces;
    private RenderPipeline? solidReflected, doubleSidedReflected, blendedReflected;
    private Sampler? surfaceSampler;
    private Sampler? paletteSampler;
    private TextureView? white;
    private TextureView? flat;
    private readonly Dictionary<GameTexture, TextureView?> textures = new(ReferenceEqualityComparer.Instance);
    private readonly List<WebGpuSharp.Texture> uploaded = [];
    private Buffer? surfaceVertices;
    private Buffer? surfaceIndices;
    private (SurfacePart Part, BindGroup Material, AlphaMode Mode)[] surfaceParts = [];
    private readonly List<Buffer> materialBuffers = [];

    private void CreateSurfacePipelines()
    {
        static BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        materialLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = SurfaceMaterial.UniformBytes },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                Map(2), Map(3), Map(4),
                // The tint palette is read per vertex.
                new BindGroupLayoutEntry
                {
                    Binding = 5,
                    Visibility = ShaderStage.Vertex,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 6, Visibility = ShaderStage.Vertex, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                // A vehicle's dirt, its paint's ramp and its second diffuse layer.
                Map(7), Map(8), Map(9),
            ],
        });
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, materialLayout, lightLayout!] });
        PipelineLayout reflectedLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = [sceneLayout, materialLayout, lightLayout!, reflectionViewLayout!],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("surface.wgsl") }));
        RenderPipeline Pipeline(CullMode cull, bool blend, bool reflected = false) => gpu.Pipeline($"surface/{cull}/{blend}/{reflected}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = reflected ? reflectedLayout : layout,
            Vertex = new VertexState
            {
                Module = shader.Value,
                EntryPoint = reflected ? "vs_surface_paraboloid" : "vs_surface",
                Buffers = new[]
                {
                    new VertexBufferLayout
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
                        },
                    },
                },
            },
            // GTA's triangles face the camera when wound counter-clockwise.
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.CCW, CullMode = cull },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = reflected ? "fs_surface_reflected" : "fs_surface",
                Targets = new[]
                {
                    new ColorTargetState
                    {
                        // Linear HDR with coverage, for the tone map.
                        Format = HdrFormat,
                        Blend = blend ? new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                        } : null,
                        WriteMask = ColorWriteMask.All,
                    },
                },
            },
            DepthStencil = new DepthStencilState
            {
                Format = DepthFormat,
                DepthWriteEnabled = blend ? OptionalBool.False : OptionalBool.True,
                // The main view's depth is reversed; the reflection map's is the distance.
                DepthCompare = reflected ? CompareFunction.LessEqual : CompareFunction.GreaterEqual,
            },
        }));
        solidSurfaces = Pipeline(CullMode.Back, blend: false);
        doubleSidedSurfaces = Pipeline(CullMode.None, blend: false);
        blendedSurfaces = Pipeline(CullMode.None, blend: true);
        // Into the reflection map: nothing culled, as the paraboloid mirrors the winding and the map's halves differ.
        solidReflected = Pipeline(CullMode.None, blend: false, reflected: true);
        doubleSidedReflected = solidReflected;
        blendedReflected = Pipeline(CullMode.None, blend: true, reflected: true);
        surfaceSampler = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 8,
        });
        // A palette's texels are colours apart: each is read whole, and its edges don't wrap.
        paletteSampler = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Nearest,
            MinFilter = FilterMode.Nearest,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        // What a material without a map reads: white for diffuse and specular, a flat normal.
        white = Solid(255, 255, 255, 255);
        flat = Solid(128, 128, 255, 255);
    }

    /// <summary>Shows a drawable's surfaces in place of anything before; null shows none.</summary>
    public void Show(DrawableScene? drawable)
    {
        Show((ViewportMesh?)null);
        if (drawable is null || drawable.Indices.Length == 0)
            return;
        origin = drawable.Origin;
        surfaceVertices = Upload((ReadOnlySpan<SurfaceVertex>)drawable.Vertices, SurfaceVertex.Size);
        surfaceIndices = Upload((ReadOnlySpan<uint>)drawable.Indices, sizeof(uint), BufferUsage.Index);
        var groups = new BindGroup[drawable.Materials.Count];
        for (int i = 0; i < groups.Length; i++)
            groups[i] = MaterialGroup(drawable.Materials[i]);
        // Decals blend over what they lie on.
        surfaceParts = [.. drawable.Parts.Select(part =>
        {
            SurfaceMaterial material = drawable.Materials[part.Material];
            return (part, groups[part.Material], material.Decal ? AlphaMode.Blend : material.AlphaMode);
        })];
        FitShadow(drawable);
        ThrowIfFailed();
    }

    private void ClearSurfaces()
    {
        // The GPU memory goes now, not whenever the handles are collected.
        surfaceVertices?.Destroy();
        surfaceIndices?.Destroy();
        foreach (Buffer buffer in materialBuffers)
            buffer.Destroy();
        materialBuffers.Clear();
        surfaceVertices = surfaceIndices = null;
        surfaceParts = [];
        foreach (WebGpuSharp.Texture texture in uploaded)
            texture.Destroy();
        uploaded.Clear();
        textures.Clear();
    }

    private BindGroup MaterialGroup(SurfaceMaterial material)
    {
        Buffer uniforms = device.CreateBuffer(new BufferDescriptor { Size = SurfaceMaterial.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        materialBuffers.Add(uniforms);
        queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<float>)material.Uniforms);
        return device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = materialLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = SurfaceMaterial.UniformBytes },
                new BindGroupEntry { Binding = 1, Sampler = surfaceSampler! },
                new BindGroupEntry { Binding = 2, TextureView = View(material.Diffuse) ?? white! },
                new BindGroupEntry { Binding = 3, TextureView = View(material.Normal) ?? flat! },
                new BindGroupEntry { Binding = 4, TextureView = View(material.Specular) ?? white! },
                new BindGroupEntry { Binding = 5, TextureView = View(material.TintPalette) ?? white! },
                new BindGroupEntry { Binding = 6, Sampler = paletteSampler! },
                new BindGroupEntry { Binding = 7, TextureView = View(material.Dirt) ?? white! },
                new BindGroupEntry { Binding = 8, TextureView = View(material.DiffuseRamp) ?? white! },
                new BindGroupEntry { Binding = 9, TextureView = View(material.DiffuseLayer) ?? white! },
            ],
        });
    }

    private void DrawSurfaces(RenderPassEncoder pass, bool reflected = false)
    {
        if (surfaceVertices is null || surfaceIndices is null || surfaceParts.Length == 0)
            return;
        pass.SetVertexBuffer(0, surfaceVertices);
        pass.SetIndexBuffer(surfaceIndices, IndexFormat.Uint32);
        // Solid parts cull their backs, as in the game; masked ones (leaves, fences) are seen from both sides.
        foreach (var (part, group, mode) in surfaceParts)
            if (mode != AlphaMode.Blend)
            {
                pass.SetPipeline(reflected ? (mode == AlphaMode.Mask ? doubleSidedReflected! : solidReflected!)
                    : mode == AlphaMode.Mask ? doubleSidedSurfaces! : solidSurfaces!);
                pass.SetBindGroup(1, group);
                pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
            }
        Vector3 eye = Camera.Eye - origin;
        pass.SetPipeline(reflected ? blendedReflected! : blendedSurfaces!);
        foreach (var (part, group, _) in surfaceParts.Where(p => p.Mode == AlphaMode.Blend).OrderByDescending(p => Vector3.DistanceSquared(p.Part.Center, eye)))
        {
            pass.SetBindGroup(1, group);
            pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
        }
    }

    private TextureView Solid(byte r, byte g, byte b, byte a)
    {
        WebGpuSharp.Texture texture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(1, 1, 1),
            Format = TextureFormat.RGBA8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        var copy = new TexelCopyTextureInfo { Texture = texture, Aspect = TextureAspect.All };
        var layout = new TexelCopyBufferLayout { BytesPerRow = 4, RowsPerImage = 1 };
        var size = new Extent3D(1, 1, 1);
        queue.WriteTexture(copy, (ReadOnlySpan<byte>)[r, g, b, a], layout, size);
        return texture.CreateView();
    }

    /// <summary>A game texture on the GPU, uploaded once per scene; null when its format or shape can't be drawn.</summary>
    private TextureView? View(GameTexture? texture)
    {
        if (texture is null)
            return null;
        if (textures.TryGetValue(texture, out TextureView? known))
            return known;
        TextureView? view = Upload(texture);
        textures[texture] = view;
        return view;
    }

    private TextureView? Upload(GameTexture source)
    {
        // The shaders read texels as stored (albedo is squared by the lighting, as in the game), so sRGB formats are
        // read through their plain counterparts.
        (TextureFormat format, int block)? mapped = source.Format switch
        {
            GameFormat.BC1Unorm or GameFormat.BC1UnormSrgb => (TextureFormat.BC1RGBAUnorm, 4),
            GameFormat.BC2Unorm or GameFormat.BC2UnormSrgb => (TextureFormat.BC2RGBAUnorm, 4),
            GameFormat.BC3Unorm or GameFormat.BC3UnormSrgb => (TextureFormat.BC3RGBAUnorm, 4),
            GameFormat.BC4Unorm => (TextureFormat.BC4RUnorm, 4),
            GameFormat.BC5Unorm => (TextureFormat.BC5RGUnorm, 4),
            GameFormat.BC7Unorm => (TextureFormat.BC7RGBAUnorm, 4),
            GameFormat.R8G8B8A8Unorm or GameFormat.R8G8B8A8UnormSrgb => (TextureFormat.RGBA8Unorm, 1),
            GameFormat.B8G8R8A8Unorm or GameFormat.B8G8R8A8UnormSrgb or GameFormat.B8G8R8X8Unorm or GameFormat.B8G8R8X8UnormSrgb => (TextureFormat.BGRA8Unorm, 1),
            GameFormat.R8G8Unorm => (TextureFormat.RG8Unorm, 1),
            GameFormat.R8Unorm => (TextureFormat.R8Unorm, 1),
            _ => null,
        };
        if (mapped is not var (format, block) || source.Shape.Dimension != CommonFury.Textures.TextureDimension.TwoD || source.Shape.ArraySize != 1
            || source.Width <= 0 || source.Height <= 0 || source.Width % block != 0 || source.Height % block != 0 || (block > 1 && !compressedTextures))
            return null;
        WebGpuSharp.Texture texture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)source.Width, (uint)source.Height, 1),
            Format = format,
            MipLevelCount = (uint)source.MipCount,
            SampleCount = 1,
        });
        uploaded.Add(texture);
        for (int level = 0; level < source.MipCount; level++)
        {
            CommonFury.Textures.TextureSubresource part = source.GetSubresource(level);
            // A mip smaller than a block is still a whole block.
            uint width = (uint)((part.Mip.Width + block - 1) / block * block), height = (uint)((part.Mip.Height + block - 1) / block * block);
            var copy = new TexelCopyTextureInfo { Texture = texture, MipLevel = (uint)level, Aspect = TextureAspect.All };
            var layout = new TexelCopyBufferLayout { BytesPerRow = (uint)part.Mip.RowPitch, RowsPerImage = height / (uint)block };
            var size = new Extent3D(width, height, 1);
            queue.WriteTexture(copy, source.GetEncodedSlice(level), layout, size);
        }
        return texture.CreateView();
    }
}
