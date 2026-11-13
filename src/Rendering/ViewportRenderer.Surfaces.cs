using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameFormat = CommonFury.Textures.TextureFormat;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Drawables: their surfaces drawn with surface.wgsl (terrain with terrain.wgsl) into the HDR target, solid parts
// first, then blended ones from far to near; ViewportRenderer.Lighting.cs lights and tone-maps them.
internal sealed unsafe partial class ViewportRenderer
{
    private readonly bool compressedTextures;
    private BindGroupLayout? materialLayout;
    private RenderPipeline? solidSurfaces;
    private RenderPipeline? doubleSidedSurfaces;
    private RenderPipeline? blendedSurfaces;
    private RenderPipeline? solidReflected, doubleSidedReflected, blendedReflected;
    private BindGroupLayout? terrainLayout;
    private RenderPipeline? terrainSurfaces, terrainReflected;
    private Sampler? surfaceSampler;
    private int anisotropy = Core.GraphicsSettings.DefaultAnisotropy;
    // The materials shown, by index, to make their groups again with another sampler.
    private IReadOnlyList<SurfaceMaterial>? shownMaterials;

    /// <summary>
    /// The textures' anisotropic filtering, 1 (trilinear alone) to 16 (Core.GraphicsSettings.Anisotropies); a change
    /// makes the shown materials' groups again with the new sampler, from the textures and values they have.
    /// </summary>
    public int Anisotropy
    {
        get => anisotropy;
        set
        {
            value = Math.Clamp(value, 1, 16);
            if (value == anisotropy)
                return;
            anisotropy = value;
            if (surfaceSampler is null)
                return;
            surfaceSampler = SurfaceSampler();
            RebuildMaterialGroups();
        }
    }

    // Repeating, trilinear, at the anisotropy asked for.
    private Sampler SurfaceSampler() => device.CreateSampler(new SamplerDescriptor
    {
        AddressModeU = AddressMode.Repeat,
        AddressModeV = AddressMode.Repeat,
        AddressModeW = AddressMode.Repeat,
        MagFilter = FilterMode.Linear,
        MinFilter = FilterMode.Linear,
        MipmapFilter = MipmapFilterMode.Linear,
        LodMinClamp = 0,
        LodMaxClamp = 32,
        MaxAnisotropy = (ushort)anisotropy,
    });
    private Sampler? paletteSampler;
    private TextureView? white;
    private TextureView? missing;
    private TextureView? flat;
    // A missing second diffuse layer lays nothing over the paint.
    private TextureView? clear;
    private readonly Dictionary<GameTexture, TextureView?> textures = new(ReferenceEqualityComparer.Instance);
    private readonly List<WebGpuSharp.Texture> uploaded = [];
    private Buffer? surfaceVertices;
    private Buffer? surfaceIndices;
    // Fringe: cut-out hair's material again, blended, for its second pass.
    private (SurfacePart Part, BindGroup Material, AlphaMode Mode, bool Terrain, BindGroup? Fringe)[] surfaceParts = [];
    // Which of the parts are drawn: at first each drawable's most detailed level.
    private bool[] partsShown = [];
    private readonly List<Buffer> materialBuffers = [];
    // Each material's uniforms, and hair's second pass's, by material: what an edit of its values writes to.
    private Buffer[] materialUniforms = [];
    private Buffer?[] fringeUniforms = [];

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
                // A vehicle's dirt, its paint's ramp and its second diffuse layer; a _detail shader's detail map; a
                // _pxm shader's height map; hair's noise map and mask.
                Map(7), Map(8), Map(9), Map(10), Map(11), Map(12), Map(13),
            ],
        });
        // Terrain: its four layers' diffuse and bump maps, the lookup map, the tint palette and the layers' height maps.
        terrainLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = TerrainUniformBytes },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                Map(2), Map(3), Map(4), Map(5), Map(6), Map(7), Map(8), Map(9), Map(10),
                new BindGroupLayoutEntry
                {
                    Binding = 11,
                    Visibility = ShaderStage.Vertex,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 12, Visibility = ShaderStage.Vertex, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                Map(13), Map(14), Map(15), Map(16),
            ],
        });
        PipelineLayout Layout(BindGroupLayout material, bool reflected) => device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = reflected ? [sceneLayout, material, lightLayout!, reflectionViewLayout!] : [sceneLayout, material, lightLayout!],
        });
        PipelineLayout layout = Layout(materialLayout, false), reflectedLayout = Layout(materialLayout, true);
        PipelineLayout terrainPipelineLayout = Layout(terrainLayout, false), terrainReflectedLayout = Layout(terrainLayout, true);
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("surface.wgsl") }));
        Lazy<ShaderModule> terrainShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("terrain.wgsl") }));
        RenderPipeline Pipeline(CullMode cull, bool blend, bool reflected = false, bool terrain = false) => gpu.Pipeline($"{(terrain ? "terrain" : "surface")}/{cull}/{blend}/{reflected}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = terrain ? (reflected ? terrainReflectedLayout : terrainPipelineLayout) : reflected ? reflectedLayout : layout,
            Vertex = new VertexState
            {
                Module = terrain ? terrainShader.Value : shader.Value,
                EntryPoint = (terrain, reflected) switch
                {
                    (true, true) => "vs_terrain_paraboloid",
                    (true, false) => "vs_terrain",
                    (false, true) => "vs_surface_paraboloid",
                    _ => "vs_surface",
                },
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
                            new VertexAttribute { ShaderLocation = 7, Offset = 64, Format = VertexFormat.Float32x2 },
                        },
                    },
                },
            },
            // GTA's triangles face the camera when wound counter-clockwise.
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.CCW, CullMode = cull },
            Fragment = new FragmentState
            {
                Module = terrain ? terrainShader.Value : shader.Value,
                EntryPoint = (terrain, reflected) switch
                {
                    (true, true) => "fs_terrain_reflected",
                    (true, false) => "fs_terrain",
                    (false, true) => "fs_surface_reflected",
                    _ => "fs_surface",
                },
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
        // Terrain is solid ground, its backs culled.
        terrainSurfaces = Pipeline(CullMode.Back, blend: false, terrain: true);
        terrainReflected = Pipeline(CullMode.None, blend: false, reflected: true, terrain: true);
        surfaceSampler = SurfaceSampler();
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
        // What a material without a map reads: white for diffuse and specular, a flat normal; a colour map it names but
        // that wasn't found reads a magenta and black checkerboard, so it stands out.
        white = Solid(255, 255, 255, 255);
        missing = Checker();
        flat = Solid(128, 128, 255, 255);
        clear = Solid(0, 0, 0, 0);

        // The sun's shadow of cut-out surfaces: their depth where the diffuse map's alpha passes the cutoff, read through
        // their material (shadow.wgsl).
        Lazy<ShaderModule> shadowShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("shadow.wgsl") }));
        maskedShadowPipeline = gpu.Pipeline($"shadow-masked/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [shadowViewLayout!, materialLayout] }),
            Vertex = new VertexState
            {
                Module = shadowShader.Value,
                EntryPoint = "vs_shadow_masked",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = SurfaceVertex.Size,
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                        },
                    },
                },
            },
            Fragment = new FragmentState { Module = shadowShader.Value, EntryPoint = "fs_shadow_masked", Targets = [] },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.Less },
        }));
    }

    /// <summary>Draws only the drawable's parts marked true, one flag per <see cref="DrawableScene.Parts"/>.</summary>
    public void ShowParts(IReadOnlyList<bool> shown)
    {
        if (shown.Count == partsShown.Length)
            for (int i = 0; i < shown.Count; i++)
                partsShown[i] = shown[i];
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
        var fringes = new BindGroup?[drawable.Materials.Count];
        materialUniforms = new Buffer[groups.Length];
        fringeUniforms = new Buffer?[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            SurfaceMaterial material = drawable.Materials[i];
            groups[i] = material.Terrain ? TerrainGroup(material) : MaterialGroup(material, material.Uniforms);
            materialUniforms[i] = materialBuffers[^1];
            // Hair is drawn again, blended, for what isn't solid (SurfaceMaterial.HairFringe).
            if (material.HairFringe)
            {
                fringes[i] = MaterialGroup(material, Fringe(material));
                fringeUniforms[i] = materialBuffers[^1];
            }
        }
        // Decals blend over what they lie on; terrain is always solid.
        surfaceParts = [.. drawable.Parts.Select(part =>
        {
            SurfaceMaterial material = drawable.Materials[part.Material];
            AlphaMode mode = material.Terrain ? AlphaMode.Opaque : material.Decal ? AlphaMode.Blend : material.AlphaMode;
            return (part, groups[part.Material], mode, material.Terrain, fringes[part.Material]);
        })];
        partsShown = [.. drawable.Parts.Select(part => part.Primary)];
        shownMaterials = drawable.Materials;
        FitShadow(Math.Max(drawable.Box.Size.Length() / 2, .25f));
        ThrowIfFailed();
    }

    // Hair's second pass: its uniforms, blended.
    private static float[] Fringe(SurfaceMaterial material)
    {
        float[] blended = [.. material.Uniforms];
        blended[10] = (float)AlphaMode.Blend;
        return blended;
    }

    // Each material's group (and hair's second pass's) made again, as after a change of sampler: from the same
    // textures, reading the same uniform buffers, so edited values stay.
    private void RebuildMaterialGroups()
    {
        if (shownMaterials is not { } materials || materials.Count != materialUniforms.Length)
            return;
        var groups = new BindGroup[materials.Count];
        var fringes = new BindGroup?[materials.Count];
        for (int i = 0; i < materials.Count; i++)
        {
            SurfaceMaterial material = materials[i];
            groups[i] = material.Terrain ? TerrainGroup(material, materialUniforms[i]) : MaterialGroup(material, material.Uniforms, materialUniforms[i]);
            if (fringeUniforms[i] is { } fringe)
                fringes[i] = MaterialGroup(material, material.Uniforms, fringe);
        }
        surfaceParts = [.. surfaceParts.Select(p => (p.Part, groups[p.Part.Material], p.Mode, p.Terrain, fringes[p.Part.Material]))];
    }

    /// <summary>
    /// Draws a material with its uniforms as <paramref name="material"/> has them now, after its values were edited: its
    /// textures and how it blends stay as they were shown.
    /// </summary>
    public void UpdateMaterial(int index, SurfaceMaterial material)
    {
        if (index < 0 || index >= materialUniforms.Length)
            return;
        queue.WriteBuffer(materialUniforms[index], 0, (ReadOnlySpan<float>)material.Uniforms);
        if (fringeUniforms[index] is { } fringe)
            queue.WriteBuffer(fringe, 0, (ReadOnlySpan<float>)Fringe(material));
    }

    private void ClearSurfaces()
    {
        // The GPU memory goes now, not whenever the handles are collected.
        surfaceVertices?.Destroy();
        surfaceIndices?.Destroy();
        foreach (Buffer buffer in materialBuffers)
            buffer.Destroy();
        materialBuffers.Clear();
        materialUniforms = [];
        fringeUniforms = [];
        shownMaterials = null;
        surfaceVertices = surfaceIndices = null;
        ShowOverlays([]);
        ShowSolid([]);
        shadowRadius = 0;
        surfaceParts = [];
        partsShown = [];
        partsPicked = [];
        foreach (WebGpuSharp.Texture texture in uploaded)
            texture.Destroy();
        uploaded.Clear();
        textures.Clear();
    }

    // With a uniform buffer of its own, filled with the values; or reading one it had, as it is.
    private BindGroup MaterialGroup(SurfaceMaterial material, float[] values, Buffer? had = null)
    {
        Buffer uniforms = had ?? device.CreateBuffer(new BufferDescriptor { Size = SurfaceMaterial.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        if (had is null)
        {
            materialBuffers.Add(uniforms);
            queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<float>)values);
        }
        return device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = materialLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = SurfaceMaterial.UniformBytes },
                new BindGroupEntry { Binding = 1, Sampler = surfaceSampler! },
                new BindGroupEntry { Binding = 2, TextureView = View(material.Diffuse) ?? (material.MissingRoles.Contains("diffuse") ? missing! : white!) },
                new BindGroupEntry { Binding = 3, TextureView = View(material.Normal) ?? flat! },
                new BindGroupEntry { Binding = 4, TextureView = View(material.Specular) ?? white! },
                new BindGroupEntry { Binding = 5, TextureView = View(material.TintPalette) ?? white! },
                new BindGroupEntry { Binding = 6, Sampler = paletteSampler! },
                new BindGroupEntry { Binding = 7, TextureView = View(material.Dirt) ?? white! },
                new BindGroupEntry { Binding = 8, TextureView = View(material.DiffuseRamp) ?? white! },
                new BindGroupEntry { Binding = 9, TextureView = View(material.DiffuseLayer) ?? clear! },
                new BindGroupEntry { Binding = 10, TextureView = View(material.Detail) ?? white! },
                new BindGroupEntry { Binding = 11, TextureView = View(material.Height) ?? white! },
                new BindGroupEntry { Binding = 12, TextureView = View(material.HairNoise) ?? white! },
                new BindGroupEntry { Binding = 13, TextureView = View(material.DiffuseMask) ?? white! },
            ],
        });
    }

    /// <summary>The bytes of terrain.wgsl's Terrain uniform, the start of the material's.</summary>
    private const int TerrainUniformBytes = 112;

    private BindGroup TerrainGroup(SurfaceMaterial material, Buffer? had = null)
    {
        Buffer uniforms = had ?? device.CreateBuffer(new BufferDescriptor { Size = SurfaceMaterial.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        if (had is null)
        {
            materialBuffers.Add(uniforms);
            queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<float>)material.Uniforms);
        }
        // A missing layer reads white, as a missing diffuse map does elsewhere, a missing bump map flat and a missing
        // height map as high as it goes; without a lookup map every weight is nothing, which is all the first layer.
        var entries = new List<BindGroupEntry>
        {
            new() { Binding = 0, Buffer = uniforms, Size = SurfaceMaterial.UniformBytes },
            new() { Binding = 1, Sampler = surfaceSampler! },
        };
        for (int i = 0; i < 8; i++)
            entries.Add(new BindGroupEntry
            {
                Binding = (uint)(2 + i),
                TextureView = View(material.Layers[i]) ?? (i >= 4 ? flat! : material.MissingRoles.Contains($"layer{i}") ? missing! : white!),
            });
        entries.Add(new BindGroupEntry { Binding = 10, TextureView = View(material.Lookup) ?? clear! });
        entries.Add(new BindGroupEntry { Binding = 11, TextureView = View(material.TintPalette) ?? white! });
        entries.Add(new BindGroupEntry { Binding = 12, Sampler = paletteSampler! });
        for (int i = 0; i < 4; i++)
            entries.Add(new BindGroupEntry { Binding = (uint)(13 + i), TextureView = View(material.Layers[8 + i]) ?? white! });
        return device.CreateBindGroup(new BindGroupDescriptor { Layout = terrainLayout!, Entries = [.. entries] });
    }

    private void DrawSurfaces(RenderPassEncoder pass, bool reflected = false)
    {
        if (surfaceVertices is null || surfaceIndices is null || surfaceParts.Length == 0)
            return;
        pass.SetVertexBuffer(0, surfaceVertices);
        pass.SetIndexBuffer(surfaceIndices, IndexFormat.Uint32);
        // Solid parts cull their backs, as in the game; masked ones (leaves, fences) are seen from both sides.
        for (int i = 0; i < surfaceParts.Length; i++)
        {
            var (part, group, mode, terrain, _) = surfaceParts[i];
            if (mode != AlphaMode.Blend && partsShown[i])
            {
                pass.SetPipeline(terrain ? (reflected ? terrainReflected! : terrainSurfaces!)
                    : reflected ? (mode == AlphaMode.Mask ? doubleSidedReflected! : solidReflected!)
                    : mode == AlphaMode.Mask ? doubleSidedSurfaces! : solidSurfaces!);
                pass.SetBindGroup(1, group);
                pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
            }
        }
        // Then what blends, furthest first: blended parts, and hair's second pass, which keeps the depth its first wrote
        // where it is solid and blends the rest in by its alpha, so hair covers a pixel as much as its alpha says, as the
        // game's noise-dropped hair does once its TAA has averaged it.
        Vector3 eye = Camera.Eye - origin;
        pass.SetPipeline(reflected ? blendedReflected! : blendedSurfaces!);
        foreach (var (part, group) in surfaceParts.Where((p, i) => (p.Mode == AlphaMode.Blend || p.Fringe is not null) && partsShown[i])
            .OrderByDescending(p => Vector3.DistanceSquared(p.Part.Center, eye)).Select(p => (p.Part, p.Fringe ?? p.Material)))
        {
            pass.SetBindGroup(1, group);
            pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
        }
    }

    /// <summary>The size of the missing-texture checkerboard, and how many of its squares cross a texture.</summary>
    internal const int CheckerSize = 64, CheckerSquares = 4;

    /// <summary>
    /// The missing-texture checkerboard: magenta and black squares, four across a texture, with every mip, the small
    /// ones the two mixed, so it reads as a checkerboard close up and a dark magenta far off, without shimmering.
    /// </summary>
    internal static byte[][] CheckerMips()
    {
        var mips = new List<byte[]>();
        for (int size = CheckerSize; size >= 1; size /= 2)
        {
            int square = size / CheckerSquares;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool magenta = square == 0 || (x / square + y / square) % 2 == 0;
                    byte level = square == 0 ? (byte)128 : magenta ? (byte)255 : (byte)0;
                    int at = (y * size + x) * 4;
                    (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (level, 0, level, 255);
                }
            mips.Add(pixels);
        }
        return [.. mips];
    }

    private TextureView Checker()
    {
        byte[][] mips = CheckerMips();
        WebGpuSharp.Texture texture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(CheckerSize, CheckerSize, 1),
            Format = TextureFormat.RGBA8Unorm,
            MipLevelCount = (uint)mips.Length,
            SampleCount = 1,
        });
        for (int level = 0; level < mips.Length; level++)
        {
            uint size = (uint)(CheckerSize >> level);
            var copy = new TexelCopyTextureInfo { Texture = texture, MipLevel = (uint)level, Aspect = TextureAspect.All };
            var layout = new TexelCopyBufferLayout { BytesPerRow = 4 * size, RowsPerImage = size };
            queue.WriteTexture(copy, (ReadOnlySpan<byte>)mips[level], layout, new Extent3D(size, size, 1));
        }
        return texture.CreateView();
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
