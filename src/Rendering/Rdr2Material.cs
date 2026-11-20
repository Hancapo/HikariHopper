using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FiveFury.Textures;
using RedMaterial = RedFury.Drawables.Material;
using RedReference = RedFury.Drawables.TextureReference;

namespace HikariHopper.Rendering;

/// <summary>
/// A Red Dead Redemption 2 material made ready for rdr2.wgsl: RedFury's RenderMaterial, copied as it is. Which parameter
/// fills which of its 24 texture slots and which of its uniforms, and what a shader's name implies (alpha mode, decal,
/// vertex colour mode, terrain blend, water), come from RedFury's materials.json (rdr2_materials.json here).
/// </summary>
internal sealed class Rdr2Material
{
    /// <summary>The bytes of rdr2.wgsl's MaterialUniforms.</summary>
    public const int UniformBytes = 560;

    /// <summary>The texture slots, as rdr2.wgsl binds them (slot + 2).</summary>
    public const int Slots = 24;

    /// <summary>The slots holding colours, read through sRGB views as RedFury reads them.</summary>
    public static readonly int[] ColourSlots = [0, 3, 5, 7, 9, 11, 12, 17];

    /// <summary>The slots holding normal maps: a missing one reads flat.</summary>
    public static readonly int[] NormalSlots = [1, 6, 8, 10, 18];

    public byte[] Uniforms { get; } = new byte[UniformBytes];
    public Texture?[] Textures { get; } = new Texture?[Slots];
    public bool[] RequiredTextures { get; } = new bool[Slots];
    public HashSet<string> MissingTextures { get; } = [];
    public bool MissingBase { get; private set; }
    public bool Blend { get; private set; }
    public bool Decal { get; private set; }
    public bool DoubleSided { get; private set; }
    /// <summary>Terrain programs that read COLOR1 in place of COLOR0.</summary>
    public bool TerrainColour1 { get; private set; }
    public bool Water { get; private set; }
    /// <summary>River water, whose COLOR1 (flow, blend, ripple scale) rides in the second and third UV sets.</summary>
    public bool WaterVertexFlow { get; private set; }

    /// <summary>
    /// A cut-out drawn with sub-sampled alpha (rdr2_cutout.wgsl), as the game draws its cut-outs on PC without MSAA:
    /// all but grass, whose programs have their own alpha test.
    /// </summary>
    public bool SubSampled => (MemoryMarshal.Cast<byte, uint>(Uniforms.AsSpan())[19] & SubSampledFlag) != 0;

    // rdr2_cutout.wgsl's RDR2_SUB_SAMPLED and RDR2_TREE_CUTOUT, in flags.w.
    internal const uint SubSampledFlag = 256, TreeCutoutFlag = 512;

    /// <summary>The alpha mode rdr2.wgsl reads: 0 opaque, 1 cut out, 2 blended.</summary>
    public int AlphaMode => (int)(Floats[11] + .5f);

    /// <summary>Whether the colour slot's sRGB view is read for <paramref name="slot"/> (terrain tint not for water).</summary>
    public bool Srgb(int slot) => ColourSlots.Contains(slot) && !(slot == 17 && Water);

    private Span<float> Floats => MemoryMarshal.Cast<byte, float>(Uniforms.AsSpan());

    private static readonly JsonDocument Mapping = JsonDocument.Parse(ViewportRenderer.Read("rdr2_materials.json"));
    private static readonly HashSet<uint> GrassShaders = new[] { "grass_batch", "grass_batch_nobump", "grass_batch_filler",
        "grass_batch_floating_veg", "grass_batch_floating_veg_no_bump", "grass_batch_floating_veg_tess" }
        .Select(name => RedFury.Hashing.Jenkins.Hash(name)).ToHashSet();

    private static Rdr2Material Plain(Vector4 colour)
    {
        var m = new Rdr2Material { DoubleSided = true };
        Span<float> f = m.Floats;
        f[0] = colour.X; f[1] = colour.Y; f[2] = colour.Z; f[3] = colour.W; f[4] = .65f; f[6] = f[7] = 1; f[9] = .5f; f[10] = 1;
        for (int i = 12; i < 16; i++) f[i] = 1;
        f[20] = 1; f[24] = 1; f[26] = 1; f[40] = 1;
        foreach (int offset in new[] { 28, 32, 44, 60, 64, 68, 72, 80, 100, 104 })
            for (int i = 0; i < 4; i++)
                f[offset + i] = 1;
        f[56] = f[57] = 1; f[110] = 1;
        return m;
    }

    /// <summary>A material made ready to draw, its textures found with <paramref name="resolve"/>: RenderMaterial.From.</summary>
    public static Rdr2Material From(RedMaterial material, Func<RedReference, Texture?> resolve)
    {
        Rdr2Material m = Plain(Vector4.One);
        m.DoubleSided = false;
        JsonElement root = Mapping.RootElement;
        JsonElement shader = root.GetProperty("shaders").EnumerateObject()
            .FirstOrDefault(s => string.Equals(s.Name, material.ShaderName, StringComparison.OrdinalIgnoreCase)).Value;
        IEnumerable<string> Candidates(string group, string role)
        {
            JsonElement names;
            if (shader.ValueKind == JsonValueKind.Object && shader.TryGetProperty(group, out JsonElement overrides) && overrides.TryGetProperty(role, out names))
                return names.EnumerateArray().Select(n => n.GetString()!);
            return root.GetProperty("defaults").GetProperty(group).TryGetProperty(role, out names) ? names.EnumerateArray().Select(n => n.GetString()!) : [];
        }
        RedFury.Drawables.ShaderParameter? TextureParameter(string role) =>
            Candidates("textures", role).Select(material.Parameter).FirstOrDefault(p => p?.Texture is not null);
        Texture? Tex(string role)
        {
            if (TextureParameter(role)?.Texture is not { } reference)
                return null;
            Texture? texture = resolve(reference);
            if (texture is null)
                m.MissingTextures.Add(reference.Name.Length > 0 ? reference.Name : $"0x{reference.NameHash:X8}");
            return texture;
        }
        float[] Values(string role, params float[] fallback)
        {
            foreach (string name in Candidates("scalars", role).Concat(Candidates("constants", role)))
                if (material.Parameter(name)?.FloatValues() is { Length: > 0 } values && values.All(float.IsFinite))
                    return values;
            return fallback;
        }
        float Scalar(string role, float fallback) => Values(role, fallback)[0];
        string State(string name, string fallback)
        {
            if (shader.ValueKind == JsonValueKind.Object && shader.TryGetProperty(name, out JsonElement explicitValue))
                return explicitValue.ToString();
            foreach (JsonElement pattern in root.GetProperty("patterns").EnumerateArray())
                if ((pattern.TryGetProperty("contains", out JsonElement contains) && material.ShaderName.Contains(contains.GetString()!, StringComparison.OrdinalIgnoreCase)
                    || pattern.TryGetProperty("suffix", out JsonElement suffix) && material.ShaderName.EndsWith(suffix.GetString()!, StringComparison.OrdinalIgnoreCase))
                    && pattern.TryGetProperty(name, out JsonElement found))
                    return found.ToString();
            return fallback;
        }

        string alpha = (material.RenderBucket & 127) switch { 1 => "BLEND", 3 => "MASK", _ => State("alpha_mode", "OPAQUE") };
        m.Blend = alpha == "BLEND";
        m.Decal = State("render_pass", "OPAQUE") == "DECAL";
        m.DoubleSided = State("double_sided", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        m.Textures[0] = Tex("diffuse");
        m.MissingBase = TextureParameter("diffuse") is not null && m.Textures[0] is null;
        m.RequiredTextures[0] = TextureParameter("diffuse") is not null;
        m.Textures[1] = Tex("normal"); m.Textures[2] = Tex("occlusion"); m.Textures[4] = Tex("control");
        m.Textures[11] = Tex("dirt_diffuse"); m.Textures[12] = Tex("tint_palette"); m.Textures[13] = Tex("material");
        m.Textures[17] = Tex("tint"); m.Textures[18] = Tex("tint_normal"); m.Textures[19] = Tex("height");
        if (material.ShaderName.Contains("emissive", StringComparison.OrdinalIgnoreCase))
            m.Textures[3] = m.Textures[0];
        Span<float> f = m.Floats;
        Span<uint> u = MemoryMarshal.Cast<byte, uint>(m.Uniforms.AsSpan());
        float power = Scalar("specular_power", 0), roughness = Scalar("roughness", power > 0 ? Math.Clamp(MathF.Sqrt(2 / (power + 2)), .08f, 1) : .65f);
        f[4] = Math.Clamp(roughness, .04f, 1); f[7] = Math.Max(0, Scalar("bumpiness", 1)); f[8] = m.Textures[3] is not null ? Scalar("emissive_multiplier", 1) : 0;
        f[11] = alpha == "BLEND" ? 2 : alpha == "MASK" ? 1 : 0;
        if (GrassShaders.Contains(material.ShaderNameHash))
        {
            m.Blend = false; m.DoubleSided = true; f[11] = 1;
            float[]? cutoff = material.Parameter("gAlphaTest")?.FloatValues();
            if (cutoff is { Length: > 0 } && float.IsFinite(cutoff[0]))
                f[9] = Math.Clamp(cutoff[0], .01f, .99f);
            float[]? tint = material.Parameter("gTintensity")?.FloatValues();
            f[136] = tint is { Length: > 0 } && float.IsFinite(tint[0]) ? Math.Clamp(tint[0], 0, 1) : 0;
            f[139] = 1;
        }
        bool heightDecal = State("coverage", "") == "HEIGHT_VERTEX";
        for (int i = 0; i < 4; i++)
        {
            float opacity = Scalar($"decal_gbuffer{i}_opacity", 1);
            f[12 + i] = heightDecal || opacity > 0 ? Math.Clamp(opacity, 0, 1) : 1;
        }
        if (heightDecal)
        {
            float[] filter = Values("decal_height_filter", 0, 0, 1, 0);
            for (int i = 0; i < Math.Min(4, filter.Length); i++)
                f[112 + i] = filter[i];
            f[116] = 1; f[117] = Scalar("terrain_blend_softness", 1);
            f[118] = TerrainUvIndex(Scalar("decal_height_uv_set", 1)); f[119] = Scalar("decal_fake_cutout", 0);
        }
        f[24] = Math.Max(0, Scalar("specular_intensity", 1)); f[25] = power; f[26] = Scalar("fresnel", 1);
        if (f[26] <= 0)
            f[26] = 1;
        f[27] = Scalar("fresnel_tweak", 0); f[36] = Scalar("roughness_tweak", 0); f[37] = Scalar("metallic_tweak", 0); f[38] = Scalar("ambient_occlusion_tweak", 0);
        float[] mask = Values("specular_intensity_mask", 1, 0, 0);
        float total = mask.Take(3).Sum(v => Math.Clamp(v, 0, 1));
        if (mask.Length >= 3 && total > 1e-6)
            for (int i = 0; i < 3; i++)
                f[40 + i] = Math.Clamp(mask[i], 0, 1) / total;
        float[] selector = Values("tint_palette_selector", 0, 0);
        f[21] = selector[0]; f[22] = selector.Length > 1 ? selector[1] : 0;
        // dirt_normal programs have no DirtBlend and always use hard light; standard_dirt ones switch on it.
        f[20] = Math.Max(0, Scalar("dirt_factor", 1)); f[23] = Scalar("dirt_blend", 1) != 0 ? 1 : 0;
        string vertexMode = State("vertex_colour_mode", "NONE");
        u[19] = (m.Textures[4] is not null ? 1u : 0) | (m.Decal ? 2u : 0) | (State("decal_mode", "NONE") == "NORMAL_ONLY" ? 4u : 0)
            | ((vertexMode switch { "RGB" => 1u, "ALPHA" => 2u, "DIRT" => 3u, _ => 0u }) << 3)
            | (m.Textures[11] is not null ? 32u : 0) | (m.Textures[12] is not null ? 64u : 0)
            | (State("material_encoding", "AUTO") == "SPECULAR_ONLY" ? 128u : 0);
        // Cut-outs but grass (grass_batch, grass_imposter_slod) with sub-sampled alpha; the trees' with their own ramp.
        if (f[11] == 1 && !m.Decal && !material.ShaderName.StartsWith("grass", StringComparison.OrdinalIgnoreCase))
            u[19] |= SubSampledFlag | (material.ShaderName.StartsWith("trees", StringComparison.OrdinalIgnoreCase) ? TreeCutoutFlag : 0);
        int count = 1;
        uint normals = 0, materials = 0;
        for (int layer = 0; layer < 4; layer++)
        {
            int diffuse = layer == 0 ? 0 : layer * 2 + 3, normal = layer == 0 ? 1 : layer * 2 + 4;
            m.Textures[diffuse] = Tex($"layer{layer}_diffuse") ?? m.Textures[diffuse];
            m.Textures[normal] = Tex($"layer{layer}_normal") ?? m.Textures[normal];
            m.RequiredTextures[diffuse] |= TextureParameter($"layer{layer}_diffuse") is not null;
            m.Textures[13 + layer] = Tex($"layer{layer}_material_a") ?? m.Textures[13 + layer];
            m.Textures[20 + layer] = Tex($"layer{layer}_material_b");
            if (TextureParameter($"layer{layer}_diffuse") is not null || m.Textures[diffuse] is not null)
                count = layer + 1;
            if (m.Textures[normal] is not null)
                normals |= 1u << layer;
            if (m.Textures[13 + layer] is not null)
                materials |= 1u << layer;
            f[28 + layer] = Scalar($"layer{layer}_bumpiness", f[7]); f[32 + layer] = Scalar($"layer{layer}_fresnel", f[26]); f[44 + layer] = Scalar($"layer{layer}_tint", 1);
        }
        f[10] = count; u[16] = normals; u[17] = (m.Textures[2] is not null ? 1u : 0) | materials << 1; u[18] = m.Textures[3] is not null ? 1u : 0;
        for (int layer = 0; layer < NormalSlots.Length; layer++)
            if (m.Textures[NormalSlots[layer]]?.Format == CommonFury.Textures.TextureFormat.BC5Unorm)
                u[16] |= 1u << (8 + layer);
        if (material.ShaderName.StartsWith("terrain", StringComparison.OrdinalIgnoreCase))
        {
            uint flags = 1;
            string terrainBlend = State("terrain_blend", "");
            bool nativeBlend = terrainBlend is "HEIGHT_VERTEX" or "HEIGHT_VERTEX_2" or "LINEAR_VERTEX";
            m.TerrainColour1 = nativeBlend || terrainBlend == "LOD_VERTEX";
            if (nativeBlend) flags |= 1024;
            if (terrainBlend == "LOD_VERTEX") flags |= 8192;
            if (terrainBlend == "LINEAR_VERTEX") flags |= 2048;
            if (terrainBlend == "HEIGHT_VERTEX_2") flags |= 4096;
            // terrain_lod_* and terrain_slod PS_TexturedDeferred write the bump texture as the world normal and
            // SpecularTexture as G = smoothness, R = f0 scale, with zero metalness.
            if (material.ShaderName.StartsWith("terrain_lod", StringComparison.OrdinalIgnoreCase)
                || material.ShaderName.StartsWith("terrain_slod", StringComparison.OrdinalIgnoreCase)) flags |= 16384;
            string[] tokens = ["hbb", "pxm", "mud", "snow", "quicksand"];
            for (int i = 0; i < tokens.Length; i++)
                if (material.ShaderName.Contains(tokens[i], StringComparison.OrdinalIgnoreCase))
                    flags |= 1u << (i + 2);
            if (material.ShaderName.Contains("spx", StringComparison.OrdinalIgnoreCase) || Scalar("terrain_pxm_enabled", 0) > .5f) flags |= 8;
            if (Scalar("terrain_clamp_lookup", 0) > 0) flags |= 2;
            if (m.Textures[17] is not null) flags |= 128;
            if (m.Textures[18] is not null) flags |= 256;
            if (m.Textures[19] is not null) flags |= 512;
            f[53] = flags; f[48] = TerrainUvIndex(Scalar("terrain_lookup_uv_set", 1));
            string[] roles = ["terrain_uv_scale_bias", "terrain_blend_softness", "terrain_hbb_scale", "terrain_hbb_bias", "terrain_pxm_scale", "terrain_pxm_bias",
                "terrain_tint_intensity", "terrain_tint_blend", "wetness", "porosity", "terrain_fresnel"];
            int[] offsets = [56, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104];
            for (int role = 0; role < roles.Length; role++)
            {
                float[] values = Values(roles[role]);
                for (int i = 0; i < Math.Min(values.Length, 4); i++)
                    f[offsets[role] + i] = values[i];
            }
            float[] sets = Values("terrain_layer_uv_sets"), bump = Values("terrain_bumpiness");
            string uvLayout = State("terrain_uv_layout", "");
            for (int i = 0; i < 4; i++)
            {
                // LOD terrain has two separately authored atlases, without uvSet constants.
                // Explicit vector/per-layer selectors still take precedence over this default.
                float defaultSet = i == 1 && material.ShaderName.StartsWith("terrain_lod_2lyr", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                f[i == 3 ? 52 : 49 + i] = TerrainUvIndex(i < sets.Length ? sets[i] : Scalar($"layer{i}_uv_set", defaultSet));
                // Some compiled programs fix the layer UVs and never read the authored selectors.
                if (uvLayout.Length == 4)
                    f[i == 3 ? 52 : 49 + i] = uvLayout[i] - '1';
                float[] scales = Values($"layer{i}_uv_scale", 1, 1);
                f[60 + i * 2] = scales[0]; f[61 + i * 2] = scales.Length > 1 ? scales[1] : scales[0];
                if (i < bump.Length)
                    f[28 + i] = Math.Max(0, bump[i]);
            }
            f[108] = Scalar("terrain_puddle_height_bias", 0); f[109] = Scalar("terrain_puddle_mask_scale", 0);
            f[110] = Scalar("terrain_height_scale", 1); f[111] = Scalar("terrain_height_bias", 0);
        }
        int waterKind = State("water_kind", "") switch { "STILL" => 1, "RIVER" => 2, "FALL" => 3, _ => 0 };
        if (waterKind != 0)
        {
            m.Water = true; m.Blend = true; m.Decal = false; m.DoubleSided = true; f[11] = 2; u[19] &= ~(SubSampledFlag | TreeCutoutFlag);
            float[] colour = Values("water_colour", .12f, .25f, .19f, .35f);
            for (int i = 0; i < 3; i++)
            {
                float c = i < colour.Length ? Math.Clamp(colour[i], 0, 1) : 0;
                f[120 + i] = c * c;
            }
            f[123] = colour.Length > 3 ? Math.Clamp(colour[3], 0, 1) : .35f;
            f[124] = Math.Clamp(Scalar("water_ripple_power", .15f), 0, 4);
            f[125] = Math.Clamp(Scalar("water_ripple_scale", .7f), 0, 100); f[126] = waterKind;
            m.Textures[4] = Tex("water_flow"); m.Textures[17] = Tex("water_fog");
            m.Textures[2] = Tex("water_foam");
            f[127] = (m.Textures[4] is not null ? 1 : 0) | (m.Textures[17] is not null ? 2 : 0) | (m.Textures[2] is not null ? 4 : 0);
            f[128] = Math.Clamp(Scalar("water_foam_scale", .05f), 0, 100);
            m.WaterVertexFlow = State("water_vertex_flow", "false") == "true";
            f[129] = m.WaterVertexFlow ? 1 : 0;
            float[] opacity = Values("water_opacity_override", 0, 0, 0, 1);
            for (int i = 0; i < 4 && i < opacity.Length; i++)
                f[132 + i] = Math.Clamp(opacity[i], 0, 1);
        }
        return m;
    }

    internal static int TerrainUvIndex(float authored) => float.IsFinite(authored) ? Math.Clamp((int)MathF.Round(authored) - 1, 0, 2) : 0;
}
