using System.Numerics;
using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;
using GameFormat = CommonFury.Textures.TextureFormat;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Drawables: their surfaces drawn with Gta5/surface.wgsl (terrain with Gta5/terrain.wgsl, fur grass with Gta5/fur.wgsl, cables with
// Gta5/cable.wgsl) into the HDR target, solid parts first, then blended ones from far to near; ViewportRenderer.Lighting.cs
// lights and tone-maps them.
internal sealed unsafe partial class ViewportRenderer
{
    /// <summary>Which shader draws a part.</summary>
    private enum PartKind
    {
        Surface,
        Terrain,
        Fur,
        Cable,
        /// <summary>Red Dead Redemption 2's material, RedFury's (Rdr2/surface.wgsl).</summary>
        Rdr2,
    }

    /// <summary>The fur grass's shells, drawn as instances of one draw (Gta5/fur.wgsl's SHELLS).</summary>
    internal const uint FurShells = 8;

    private readonly bool compressedTextures;
    private BindGroupLayout? materialLayout;
    private RenderPipeline? solidSurfaces;
    private RenderPipeline? doubleSidedSurfaces;
    private RenderPipeline? blendedSurfaces;
    // Glass, blended with its back culled, as the game draws it: a vehicle's inside has glass of its own.
    private RenderPipeline? blendedCulledSurfaces;
    private RenderPipeline? solidReflected, doubleSidedReflected, blendedReflected;
    private BindGroupLayout? terrainLayout;
    private RenderPipeline? terrainSurfaces, terrainReflected;
    private BindGroupLayout? furLayout, cableLayout;
    private RenderPipeline? furSurfaces, furReflected, cableSurfaces;
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
    // What a _reflect shader reads where it has no environment of its kind: nothing, as a sphere map and as a cube.
    private TextureView? noEnvironment, noEnvironmentCube;
    private TextureView? missing;
    private TextureView? flat;
    // A missing second diffuse layer lays nothing over the paint.
    private TextureView? clear;
    private readonly Dictionary<GameTexture, TextureView?> textures = new(ReferenceEqualityComparer.Instance);
    // Each uploaded texture's GPU texture, and its sRGB view, for the colour slots of RDR2's material.
    private readonly Dictionary<GameTexture, HikariHopper.Gpu.Texture> uploadedOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GameTexture, TextureView?> srgbViews = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<HikariHopper.Gpu.Texture> uploaded = new(ReferenceEqualityComparer.Instance);
    private Buffer? surfaceVertices;
    private Buffer? surfaceIndices;
    // Fringe: cut-out hair's material again, blended, for its second pass.
    // Culled: blended with its back culled (glass).
    private (SurfacePart Part, BindGroup Material, AlphaMode Mode, PartKind Kind, BindGroup? Fringe, bool Culled)[] surfaceParts = [];
    // Which of the parts are drawn: at first each drawable's most detailed level.
    private bool[] partsShown = [];
    // Which are night-only geometry (SurfaceMaterial.NightOnly), cast into the shadow only by night.
    private bool[] partNightOnly = [];
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
                // _pxm shader's height map; hair's noise map and mask; a _reflect shader's environment, a sphere map
                // or a cube.
                Map(7), Map(8), Map(9), Map(10), Map(11), Map(12), Map(13), Map(14),
                new BindGroupLayoutEntry
                {
                    Binding = 15,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Cube },
                },
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
        // Fur: its diffuse, bump and specular maps, the four combo height maps, _mask's mask and colour maps, and _tnt's
        // tint palette, read per vertex.
        furLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = FurUniformBytes },
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
            ],
        });
        // Cables read no texture: their colours are the material's.
        cableLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = CableUniformBytes },
                },
            ],
        });
        PipelineLayout Layout(BindGroupLayout material, bool reflected) => device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayouts = reflected ? [sceneLayout, material, lightLayout!, reflectionViewLayout!] : [sceneLayout, material, lightLayout!],
        });
        PipelineLayout layout = Layout(materialLayout, false), reflectedLayout = Layout(materialLayout, true);
        PipelineLayout terrainPipelineLayout = Layout(terrainLayout, false), terrainReflectedLayout = Layout(terrainLayout, true);
        PipelineLayout furPipelineLayout = Layout(furLayout, false), furReflectedLayout = Layout(furLayout, true);
        PipelineLayout cablePipelineLayout = Layout(cableLayout, false);
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("Gta5/trees.wgsl") + "\n" + Read("Gta5/surface.wgsl") }));
        Lazy<ShaderModule> terrainShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("Gta5/terrain.wgsl") }));
        Lazy<ShaderModule> furShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("Gta5/fur.wgsl") }));
        Lazy<ShaderModule> cableShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("Gta5/cable.wgsl") }));
        RenderPipeline Pipeline(CullMode cull, bool blend, bool reflected = false, PartKind kind = PartKind.Surface)
        {
            string name = kind.ToString().ToLowerInvariant();
            (PipelineLayout own, PipelineLayout mirrored, Lazy<ShaderModule> module) = kind switch
            {
                PartKind.Terrain => (terrainPipelineLayout, terrainReflectedLayout, terrainShader),
                PartKind.Fur => (furPipelineLayout, furReflectedLayout, furShader),
                PartKind.Cable => (cablePipelineLayout, cablePipelineLayout, cableShader),
                _ => (layout, reflectedLayout, shader),
            };
            return gpu.Pipeline($"{name}/{cull}/{blend}/{reflected}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = reflected ? mirrored : own,
            Vertex = new VertexState
            {
                Module = module.Value,
                EntryPoint = reflected ? $"vs_{name}_paraboloid" : $"vs_{name}",
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
                Module = module.Value,
                EntryPoint = reflected ? $"fs_{name}_reflected" : $"fs_{name}",
                // Into the view, GTA V's solid surfaces and terrain leave their sub-sampled alpha, or none.
                Targets = ViewTargets(new ColorTargetState
                {
                    // Linear HDR with coverage, for the tone map.
                    Format = HdrFormat,
                    Blend = blend ? new BlendState
                    {
                        Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                        Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                    } : null,
                    WriteMask = ColorWriteMask.All,
                }, reflected, subSampled: !blend && kind is PartKind.Surface or PartKind.Terrain),
            },
            DepthStencil = new DepthStencilState
            {
                Format = DepthFormat,
                DepthWriteEnabled = blend ? OptionalBool.False : OptionalBool.True,
                // The main view's depth is reversed; the reflection map's is the distance.
                DepthCompare = reflected ? CompareFunction.LessEqual : CompareFunction.GreaterEqual,
            },
        }));
        }
        solidSurfaces = Pipeline(CullMode.Back, blend: false);
        doubleSidedSurfaces = Pipeline(CullMode.None, blend: false);
        blendedSurfaces = Pipeline(CullMode.None, blend: true);
        blendedCulledSurfaces = Pipeline(CullMode.Back, blend: true);
        // Into the reflection map: nothing culled, as the paraboloid mirrors the winding and the map's halves differ.
        solidReflected = Pipeline(CullMode.None, blend: false, reflected: true);
        doubleSidedReflected = solidReflected;
        blendedReflected = Pipeline(CullMode.None, blend: true, reflected: true);
        // Terrain is solid ground, its backs culled.
        terrainSurfaces = Pipeline(CullMode.Back, blend: false, kind: PartKind.Terrain);
        terrainReflected = Pipeline(CullMode.None, blend: false, reflected: true, kind: PartKind.Terrain);
        // Fur is cut out, seen from both sides as the other cut-out surfaces; cables are blended, and left out of the
        // reflection map, as their width is the camera's.
        furSurfaces = Pipeline(CullMode.None, blend: false, kind: PartKind.Fur);
        furReflected = Pipeline(CullMode.None, blend: false, reflected: true, kind: PartKind.Fur);
        cableSurfaces = Pipeline(CullMode.None, blend: true, kind: PartKind.Cable);
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
        noEnvironment = Solid(0, 0, 0, 255);
        noEnvironmentCube = SolidCube(0, 0, 0, 255);

        // The sun's shadow of cut-out surfaces: their depth where the diffuse map's alpha passes the cutoff, read through
        // their material (Scene/shadow.wgsl).
        Lazy<ShaderModule> shadowShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("Gta5/trees.wgsl") + "\n" + Read("Scene/shadow.wgsl") }));
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
                        // A tree's caster reads its billboard's size and alignment, or its card's centre, besides.
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 2, Offset = 24, Format = VertexFormat.Float32x4 },
                            new VertexAttribute { ShaderLocation = 3, Offset = 40, Format = VertexFormat.Float32x2 },
                            new VertexAttribute { ShaderLocation = 4, Offset = 48, Format = VertexFormat.Float32x2 },
                            new VertexAttribute { ShaderLocation = 7, Offset = 64, Format = VertexFormat.Float32x2 },
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
        modelLights = drawable.Lights;
        modelLit = null;
        surfaceVertices = Upload((ReadOnlySpan<SurfaceVertex>)drawable.Vertices, SurfaceVertex.Size);
        surfaceIndices = Upload((ReadOnlySpan<uint>)drawable.Indices, sizeof(uint), BufferUsage.Index);
        var groups = new BindGroup[drawable.Materials.Count];
        var fringes = new BindGroup?[drawable.Materials.Count];
        materialUniforms = new Buffer[groups.Length];
        fringeUniforms = new Buffer?[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            SurfaceMaterial material = drawable.Materials[i];
            groups[i] = DrawsRdr2(material) ? Rdr2Group(material.Rdr2!)
                : material.Terrain ? TerrainGroup(material) : material.Fur ? FurGroup(material) : material.Cable ? CableGroup(material)
                : MaterialGroup(material, material.Uniforms);
            materialUniforms[i] = materialBuffers[^1];
            // Hair is drawn again, blended, for what isn't solid (SurfaceMaterial.HairFringe).
            if (material.HairFringe)
            {
                fringes[i] = MaterialGroup(material, Fringe(material));
                fringeUniforms[i] = materialBuffers[^1];
            }
        }
        // Decals blend over what they lie on; terrain is always solid, and so is fur, which cuts its own shells out;
        // cables blend.
        surfaceParts = [.. drawable.Parts.Select(part =>
        {
            SurfaceMaterial material = drawable.Materials[part.Material];
            PartKind kind = DrawsRdr2(material) ? PartKind.Rdr2
                : material.Terrain ? PartKind.Terrain : material.Fur ? PartKind.Fur : material.Cable ? PartKind.Cable : PartKind.Surface;
            AlphaMode mode = kind switch
            {
                PartKind.Terrain or PartKind.Fur => AlphaMode.Opaque,
                PartKind.Cable => AlphaMode.Blend,
                _ => material.Decal ? AlphaMode.Blend : material.AlphaMode,
            };
            return (part, groups[part.Material], mode, kind, fringes[part.Material], material.Glass || material.CullsBack);
        })];
        partsShown = [.. drawable.Parts.Select(part => part.Primary)];
        partNightOnly = [.. drawable.Parts.Select(part => drawable.Materials[part.Material].NightOnly)];
        partRdr2 = [.. surfaceParts.Select(p => p.Kind == PartKind.Rdr2 ? drawable.Materials[p.Part.Material].Rdr2 : null)];
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
            groups[i] = DrawsRdr2(material) ? Rdr2Group(material.Rdr2!, materialUniforms[i])
                : material.Terrain ? TerrainGroup(material, materialUniforms[i]) : material.Fur ? FurGroup(material, materialUniforms[i])
                : material.Cable ? CableGroup(material, materialUniforms[i]) : MaterialGroup(material, material.Uniforms, materialUniforms[i]);
            if (fringeUniforms[i] is { } fringe)
                fringes[i] = MaterialGroup(material, material.Uniforms, fringe);
        }
        surfaceParts = [.. surfaceParts.Select(p => (p.Part, groups[p.Part.Material], p.Mode, p.Kind, fringes[p.Part.Material], p.Culled))];
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
        partNightOnly = [];
        partRdr2 = [];
        partsPicked = [];
        foreach (HikariHopper.Gpu.Texture texture in uploaded)
            texture.Destroy();
        uploaded.Clear();
        textures.Clear();
        uploadedOf.Clear();
        srgbViews.Clear();
    }

    // With a uniform buffer of its own, filled with the values; or reading one it had, as it is.
    private BindGroup MaterialGroup(SurfaceMaterial material, float[] values, Buffer? had = null, ulong offset = 0)
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
                new BindGroupEntry { Binding = 0, Buffer = uniforms, Offset = offset, Size = SurfaceMaterial.UniformBytes },
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
                new BindGroupEntry { Binding = 14, TextureView = (material.Reflection == EnvironmentKind.Sphere ? View(material.Environment) : null) ?? noEnvironment! },
                new BindGroupEntry { Binding = 15, TextureView = (material.Reflection == EnvironmentKind.Cube ? CubeView(material.Environment) : null) ?? noEnvironmentCube! },
            ],
        });
    }

    /// <summary>The bytes of Gta5/terrain.wgsl's Terrain uniform, the start of the material's.</summary>
    private const int TerrainUniformBytes = 112;

    /// <summary>The bytes of Gta5/fur.wgsl's Fur uniform and of Gta5/cable.wgsl's Cable uniform, the start of the material's.</summary>
    private const int FurUniformBytes = 144, CableUniformBytes = 64;

    private Buffer MaterialUniforms(SurfaceMaterial material)
    {
        Buffer uniforms = device.CreateBuffer(new BufferDescriptor { Size = SurfaceMaterial.UniformBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        materialBuffers.Add(uniforms);
        queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<float>)material.Uniforms);
        return uniforms;
    }

    // A missing combo height map has no fur, so only the first shell shows; a missing mask, no fur at all; a missing
    // colour map changes nothing.
    private BindGroup FurGroup(SurfaceMaterial material, Buffer? had = null) => device.CreateBindGroup(new BindGroupDescriptor
    {
        Layout = furLayout!,
        Entries =
        [
            new BindGroupEntry { Binding = 0, Buffer = had ?? MaterialUniforms(material), Size = SurfaceMaterial.UniformBytes },
            new BindGroupEntry { Binding = 1, Sampler = surfaceSampler! },
            new BindGroupEntry { Binding = 2, TextureView = View(material.Diffuse) ?? (material.MissingRoles.Contains("diffuse") ? missing! : white!) },
            new BindGroupEntry { Binding = 3, TextureView = View(material.Normal) ?? flat! },
            new BindGroupEntry { Binding = 4, TextureView = View(material.Specular) ?? white! },
            new BindGroupEntry { Binding = 5, TextureView = View(material.FurHeights[0]) ?? clear! },
            new BindGroupEntry { Binding = 6, TextureView = View(material.FurHeights[1]) ?? clear! },
            new BindGroupEntry { Binding = 7, TextureView = View(material.FurHeights[2]) ?? clear! },
            new BindGroupEntry { Binding = 8, TextureView = View(material.FurHeights[3]) ?? clear! },
            new BindGroupEntry { Binding = 9, TextureView = View(material.FurMask) ?? clear! },
            new BindGroupEntry { Binding = 10, TextureView = View(material.DiffuseHf) ?? white! },
            new BindGroupEntry { Binding = 11, TextureView = View(material.TintPalette) ?? white! },
            new BindGroupEntry { Binding = 12, Sampler = paletteSampler! },
        ],
    });

    private BindGroup CableGroup(SurfaceMaterial material, Buffer? had = null) => device.CreateBindGroup(new BindGroupDescriptor
    {
        Layout = cableLayout!,
        Entries = [new BindGroupEntry { Binding = 0, Buffer = had ?? MaterialUniforms(material), Size = SurfaceMaterial.UniformBytes }],
    });

    private BindGroup TerrainGroup(SurfaceMaterial material, Buffer? had = null, ulong offset = 0)
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
            new() { Binding = 0, Buffer = uniforms, Offset = offset, Size = SurfaceMaterial.UniformBytes },
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

    // In the main view (with the frame's encoder), the pass goes on after RDR2's sub-sampled cut-outs are put together,
    // between what's solid and what blends; the pass to go on with is returned.
    private RenderPassEncoder DrawSurfaces(RenderPassEncoder pass, bool reflected = false, CommandEncoder? encoder = null)
    {
        if (surfaceVertices is null || surfaceIndices is null || surfaceParts.Length == 0)
            return pass;
        pass.SetVertexBuffer(0, surfaceVertices);
        pass.SetIndexBuffer(surfaceIndices, IndexFormat.Uint32);
        // Solid parts cull their backs, as in the game; masked ones (leaves, fences) are seen from both sides, but for the
        // trees, which cull theirs. Each is drawn only into the passes its model's mask names (a tree's shadow proxy,
        // into none of these).
        for (int i = 0; i < surfaceParts.Length; i++)
        {
            var (part, group, mode, kind, _, culled) = surfaceParts[i];
            if (mode != AlphaMode.Blend && partsShown[i] && Drawn(part, reflected))
            {
                pass.SetPipeline(kind switch
                {
                    // RDR2's: culled only when solid and not drawn from both sides, as RedFury's CullBackFaces.
                    PartKind.Rdr2 => reflected ? rdr2Reflected! : mode == AlphaMode.Mask || partRdr2[i]?.DoubleSided == true ? rdr2DoubleSided! : rdr2Solid!,
                    PartKind.Terrain => reflected ? terrainReflected! : terrainSurfaces!,
                    PartKind.Fur => reflected ? furReflected! : furSurfaces!,
                    _ => reflected ? (mode == AlphaMode.Mask ? doubleSidedReflected! : solidReflected!)
                        : mode == AlphaMode.Mask && !culled ? doubleSidedSurfaces! : solidSurfaces!,
                });
                pass.SetBindGroup(1, group);
                // Fur: one instance a shell.
                pass.DrawIndexed(part.IndexCount, kind == PartKind.Fur ? FurShells : 1, part.FirstIndex, part.BaseVertex, 0);
            }
        }
        if (encoder is { } frame && SurfacesSubSampled())
        {
            pass = CombineSubSampledAlpha(pass, frame);
            pass.SetVertexBuffer(0, surfaceVertices);
            pass.SetIndexBuffer(surfaceIndices, IndexFormat.Uint32);
        }
        // Then what blends, furthest first: blended parts, and hair's second pass, which keeps the depth its first wrote
        // where it is solid and blends the rest in by its alpha, so hair covers a pixel as much as its alpha says, as the
        // game's noise-dropped hair does once its TAA has averaged it.
        // Cables among them, but not into the reflection map. Glass culls its back, as in the game, but into the
        // reflection map, whose paraboloid mirrors the winding.
        Vector3 eye = Camera.Eye - origin;
        foreach (var (part, group, kind, culled) in surfaceParts.Where((p, i) => (p.Mode == AlphaMode.Blend || p.Fringe is not null) && partsShown[i]
                && Drawn(p.Part, reflected) && !(reflected && p.Kind == PartKind.Cable))
            .OrderByDescending(p => Vector3.DistanceSquared(p.Part.Center, eye)).Select(p => (p.Part, p.Fringe ?? p.Material, p.Kind, p.Culled)))
        {
            pass.SetPipeline(kind == PartKind.Cable ? cableSurfaces!
                : kind == PartKind.Rdr2 ? (reflected ? rdr2BlendedReflected! : rdr2Blended!)
                : reflected ? blendedReflected! : culled ? blendedCulledSurfaces! : blendedSurfaces!);
            pass.SetBindGroup(1, group);
            pass.DrawIndexed(part.IndexCount, 1, part.FirstIndex, part.BaseVertex, 0);
        }
        return pass;
    }

    // Whether a part's model is drawn into the main view, or into the reflection map, by its render mask.
    internal static bool Drawn(SurfacePart part, bool reflected) =>
        reflected ? (part.Passes & FiveFury.Drawables.DrawableRenderMask.Reflection) != 0 : part.Shown;

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
        HikariHopper.Gpu.Texture texture = device.CreateTexture(new TextureDescriptor
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
        HikariHopper.Gpu.Texture texture = device.CreateTexture(new TextureDescriptor
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

    // One colour on each face of a cube.
    private TextureView SolidCube(byte r, byte g, byte b, byte a)
    {
        HikariHopper.Gpu.Texture texture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(1, 1, 6),
            Format = TextureFormat.RGBA8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        var copy = new TexelCopyTextureInfo { Texture = texture, Aspect = TextureAspect.All };
        var layout = new TexelCopyBufferLayout { BytesPerRow = 4, RowsPerImage = 1 };
        queue.WriteTexture(copy, (ReadOnlySpan<byte>)[r, g, b, a, r, g, b, a, r, g, b, a, r, g, b, a, r, g, b, a, r, g, b, a], layout, new Extent3D(1, 1, 6));
        return ShapedView(texture);
    }

    /// <summary>A game texture's GPU texture viewed as the shaders read it: a cube's six layers as a cube.</summary>
    private static TextureView ShapedView(HikariHopper.Gpu.Texture texture) => texture.GetDepthOrArrayLayers() == 6
        ? texture.CreateView(new TextureViewDescriptor
        {
            Format = texture.GetFormat(),
            Dimension = TextureViewDimension.Cube,
            MipLevelCount = texture.GetMipLevelCount(),
            ArrayLayerCount = 6,
            Aspect = TextureAspect.All,
        })
        : texture.CreateView();

    /// <summary>
    /// A game image on the GPU, uploaded once per scene; null when its format or shape can't be drawn, a cube among
    /// them (<see cref="CubeView"/>).
    /// </summary>
    private TextureView? View(GameTexture? texture) => texture is { Shape.Dimension: CommonFury.Textures.TextureDimension.Cube } ? null : Uploaded(texture);

    /// <summary>A game cube on the GPU, as <see cref="View"/> has an image; null for anything else.</summary>
    private TextureView? CubeView(GameTexture? texture) => texture is { Shape.Dimension: CommonFury.Textures.TextureDimension.Cube } ? Uploaded(texture) : null;

    private TextureView? Uploaded(GameTexture? texture)
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
        if (CreateTexture(source) is not var (texture, block))
            return null;
        for (int level = 0; level < source.MipCount; level++)
            WriteLevel(source, texture, block, level);
        return Register(source, texture);
    }

    /// <summary>
    /// A game texture's GPU texture, made but not written, and its formats' block size; null when its format or shape
    /// can't be drawn.
    /// </summary>
    private (HikariHopper.Gpu.Texture Texture, int Block)? CreateTexture(GameTexture source)
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
            GameFormat.BC7Unorm or GameFormat.BC7UnormSrgb => (TextureFormat.BC7RGBAUnorm, 4),
            GameFormat.R8G8B8A8Unorm or GameFormat.R8G8B8A8UnormSrgb => (TextureFormat.RGBA8Unorm, 1),
            GameFormat.B8G8R8A8Unorm or GameFormat.B8G8R8A8UnormSrgb or GameFormat.B8G8R8X8Unorm or GameFormat.B8G8R8X8UnormSrgb => (TextureFormat.BGRA8Unorm, 1),
            GameFormat.R8G8Unorm => (TextureFormat.RG8Unorm, 1),
            GameFormat.R8Unorm => (TextureFormat.R8Unorm, 1),
            _ => null,
        };
        // A plain image, or a cube (a _cubemap_reflect shader's environment), its faces six layers.
        bool cube = source.Shape.Dimension == CommonFury.Textures.TextureDimension.Cube && source.Width == source.Height;
        if (mapped is not var (format, block) || source.Shape.Dimension != CommonFury.Textures.TextureDimension.TwoD && !cube || source.Shape.ArraySize != 1
            || source.Width <= 0 || source.Height <= 0 || source.Width % block != 0 || source.Height % block != 0 || (block > 1 && !compressedTextures))
            return null;
        // Viewable as sRGB too, where the format has a variant: RDR2's material reads its colours so.
        TextureFormat? srgb = SrgbOf(format);
        HikariHopper.Gpu.Texture texture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)source.Width, (uint)source.Height, cube ? 6u : 1u),
            Format = format,
            MipLevelCount = (uint)source.MipCount,
            SampleCount = 1,
            ViewFormats = srgb is { } variant ? [variant] : [],
        });
        return (texture, block);
    }

    /// <summary>One mip level of a game texture into its GPU texture; how many bytes that was.</summary>
    private long WriteLevel(GameTexture source, HikariHopper.Gpu.Texture texture, int block, int level)
    {
        long bytes = 0;
        for (int first = 0; ;)
        {
            var (written, done, rows) = WriteRows(source, texture, block, level, first, int.MaxValue);
            bytes += written;
            first += rows;
            if (done)
                return bytes;
        }
    }

    /// <summary>
    /// Rows of one mip level (rows of blocks, for a compressed format) into its GPU texture, from <paramref name="first"/>
    /// at most <paramref name="rows"/>, within one face of a cube (its faces' rows one after another): how many bytes
    /// that was, whether the level is done, and how many rows.
    /// </summary>
    private (long Bytes, bool Done, int Rows) WriteRows(GameTexture source, HikariHopper.Gpu.Texture texture, int block, int level, int first, int rows)
    {
        int faces = source.Shape.Faces;
        CommonFury.Textures.TextureSubresource part = source.GetSubresource(level);
        // A mip smaller than a block is still a whole block.
        uint width = (uint)((part.Mip.Width + block - 1) / block * block), height = (uint)((part.Mip.Height + block - 1) / block * block);
        int total = (int)height / block, pitch = part.Mip.RowPitch;
        int face = first / total, start = first % total;
        int count = Math.Min(rows, total - start);
        var copy = new TexelCopyTextureInfo
        {
            Texture = texture,
            MipLevel = (uint)level,
            Origin = new Origin3D(0, (uint)(start * block), (uint)face),
            Aspect = TextureAspect.All,
        };
        var layout = new TexelCopyBufferLayout { BytesPerRow = (uint)pitch, RowsPerImage = (uint)count };
        var size = new Extent3D(width, (uint)(count * block), 1);
        ReadOnlySpan<byte> data = source.GetEncodedSlice(level, 0, face).Slice(start * pitch, count * pitch);
        queue.WriteTexture(copy, data, layout, size);
        return (data.Length, first + count == total * faces, count);
    }

    /// <summary>The bytes a row of a game texture's mip level holds (a row of blocks, for a compressed format).</summary>
    private static int RowBytes(GameTexture source, int level) => source.GetSubresource(level).Mip.RowPitch;

    /// <summary>A written texture, kept for the scene (or the world) and viewed.</summary>
    private TextureView Register(GameTexture source, HikariHopper.Gpu.Texture texture)
    {
        uploaded.Add(texture);
        uploadedOf[source] = texture;
        return ShapedView(texture);
    }
}
