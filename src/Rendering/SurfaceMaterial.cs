using System.Numerics;
using System.Text.Json;
using CommonFury.Textures;
using FiveFury.Drawables;
using FiveFury.Textures;

namespace HikariHopper.Rendering;

/// <summary>How a surface's pixels combine with what is behind them.</summary>
internal enum AlphaMode
{
    /// <summary>Solid.</summary>
    Opaque,
    /// <summary>Solid where the diffuse map's alpha passes the cutoff, a hole elsewhere.</summary>
    Mask,
    /// <summary>Blended over what is behind it by the diffuse map's alpha.</summary>
    Blend,
}

/// <summary>
/// A GTA V material made ready for surface.wgsl, or terrain.wgsl for the terrain shaders, fur.wgsl for the fur grass
/// and cable.wgsl for the cables: its textures and the constants its shader reads, from either edition.
/// Which parameter fills which role is in materials.json, by Legacy name; Enhanced materials reach those names through
/// FiveFury's shader catalogue, and a value a material leaves out is its shader's default from the same catalogue.
/// </summary>
internal sealed class SurfaceMaterial
{
    /// <summary>
    /// The bytes of surface.wgsl's Material uniform; terrain.wgsl's Terrain uniform is its first 112, fur.wgsl's Fur its
    /// first 144 and cable.wgsl's Cable its first 64.
    /// </summary>
    public const int UniformBytes = 368;

    /// <summary>The Material uniform, as floats: see surface.wgsl; for terrain, the Terrain uniform of terrain.wgsl.</summary>
    public float[] Uniforms { get; } = new float[UniformBytes / sizeof(float)];

    /// <summary>The shader's name, or its hash when the catalogues don't know it.</summary>
    public string Shader { get; private init; } = "";

    /// <summary>
    /// Whether the shader is one of the families surface.wgsl or terrain.wgsl rebuilds; others are drawn as the closest
    /// they do.
    /// </summary>
    public bool Rebuilt { get; private init; }

    /// <summary>Whether it is a vehicle shader (PS_DeferredVehicleTextured): paint, mesh, tyres, interiors and the rest.</summary>
    public bool Vehicle { get; private init; }

    /// <summary>
    /// Whether it is a terrain shader, drawn with terrain.wgsl: those the shader rebuilds, and the rest of the terrain
    /// shaders (the tint textures' tt, ttn and uber, which no map drawable uses) without what sets them apart.
    /// </summary>
    public bool Terrain { get; private init; }

    /// <summary>
    /// Whether it is a ped shader (PS_DeferredPedTextured): those the shader rebuilds, and the rest (fur, medals) as the
    /// closest of them.
    /// </summary>
    public bool Ped { get; private init; }

    /// <summary>
    /// Whether it is a fur grass shader (grass_fur, grass_fur_mask, grass_fur_tnt), drawn with fur.wgsl: the mesh again
    /// for each of its shells, each cut out where its fur is too short.
    /// </summary>
    public bool Fur { get; private init; }

    /// <summary>
    /// Whether it is the cable shader, drawn with cable.wgsl: a ribbon of no width that the vertex shader widens to face
    /// the camera, blended over what is behind it.
    /// </summary>
    public bool Cable { get; private init; }

    /// <summary>The fur grass's combo height maps (ComboHeightTexFur01, 23, 45, 67), two shells each.</summary>
    public Texture?[] FurHeights { get; private init; } = new Texture?[4];

    /// <summary>grass_fur_mask's mask over its fur, on the second UV set.</summary>
    public Texture? FurMask { get; private init; }

    /// <summary>grass_fur_mask's colour over the diffuse map (DiffuseHfTex), on the third UV set.</summary>
    public Texture? DiffuseHf { get; private init; }

    public AlphaMode AlphaMode { get; private init; }

    /// <summary>
    /// Ped hair, which covers each pixel by its alpha. Enhanced draws it with deferredsubsamplealpha: it drops pixels by
    /// a noise where the alpha is low and lets its TAA average them, with no cutoff. Without TAA, the viewer draws it
    /// twice to the same end: solid where it is all but opaque, then the rest blended over that by its alpha.
    /// </summary>
    public bool HairFringe => Ped && Uniforms[76] == 3 && AlphaMode == AlphaMode.Mask;

    /// <summary>Where hair counts as solid, for its first pass: what is less is blended in its second.</summary>
    internal const float HairSolid = .95f;

    /// <summary>A decal: drawn over the surface it sits on, without writing depth.</summary>
    public bool Decal { get; private init; }

    public Texture? Diffuse { get; private init; }
    public Texture? Normal { get; private init; }
    public Texture? Specular { get; private init; }

    /// <summary>The palette a tinted (_tnt) shader takes its colour from: a row per tint, a column per vertex.</summary>
    public Texture? TintPalette { get; private init; }

    /// <summary>A vehicle's dirt: R and B how much, G how rough it makes the surface.</summary>
    public Texture? Dirt { get; private init; }

    /// <summary>A vehicle paint's colour by viewing angle, for paints with a ramp in place of a colour.</summary>
    public Texture? DiffuseRamp { get; private init; }

    /// <summary>A vehicle's second diffuse layer (diffuseTexture2): its livery or decals, over the paint.</summary>
    public Texture? DiffuseLayer { get; private init; }

    /// <summary>
    /// The _detail shaders' detail map: a small tiling noise (the game's are in mapdetail.ytd) that roughens the diffuse,
    /// the specular and the bumps up close.
    /// </summary>
    public Texture? Detail { get; private init; }

    /// <summary>The _pxm shaders' height map, which their parallax marches.</summary>
    public Texture? Height { get; private init; }

    /// <summary>Hair's noise map, which breaks up its highlights (R, G, B) and its alpha (A).</summary>
    public Texture? HairNoise { get; private init; }

    /// <summary>The ped_hair_spiked_mask shader's mask over the hair's alpha, on the second UV set.</summary>
    public Texture? DiffuseMask { get; private init; }

    /// <summary>A terrain shader's four layers: their diffuse maps, then their bump maps, then their height maps (_pxm).</summary>
    public Texture?[] Layers { get; private init; } = new Texture?[12];

    /// <summary>
    /// The roles (materials.json's: diffuse, layer0…) whose texture the material names but which wasn't found; the
    /// viewer draws a missing colour map as a checkerboard, so it stands out.
    /// </summary>
    public IReadOnlySet<string> MissingRoles { get; private init; } = new HashSet<string>();

    /// <summary>A terrain shader's lookup map, which holds the layer weights on the second UV set (_cm, _2tex_blend).</summary>
    public Texture? Lookup { get; private init; }

    /// <summary>Textures the material names that neither the drawable nor its dictionaries hold.</summary>
    public IReadOnlyList<string> MissingTextures { get; private init; } = [];

    private static readonly JsonDocument Mapping = JsonDocument.Parse(ViewportRenderer.Read("materials.json"));

    private static readonly HashSet<string> Textured = Family("textured");
    private static readonly HashSet<string> Vehicles = Family("vehicle");
    private static readonly HashSet<string> Terrains = Family("terrain");
    private static readonly HashSet<string> ApproximateTerrains = Family("terrain_approximate");
    private static readonly HashSet<string> Peds = Family("ped");
    private static readonly HashSet<string> PedsDiffuse = Family("ped_diffuse");
    private static readonly HashSet<string> PedsHair = Family("ped_hair");
    private static readonly HashSet<string> ApproximatePeds = Family("ped_approximate");
    private static readonly HashSet<string> Furs = Family("fur");
    private static readonly HashSet<string> Cables = Family("cable");

    private static HashSet<string> Family(string name) => Mapping.RootElement.GetProperty("families").GetProperty(name)
        .EnumerateArray().Select(shader => shader.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Makes a material ready to draw, finding its textures with <paramref name="resolve"/>; a vehicle's paint slots in
    /// <paramref name="paint"/>'s colours, the game's grey without.
    /// </summary>
    public static SurfaceMaterial From(DrawableMaterial material, Func<TextureReference, Texture?> resolve, VehiclePaint? paint = null)
    {
        paint ??= VehiclePaint.Unset;
        bool enhanced = material.Gen9Parameters is not null;
        ShaderDefinition? definition = (enhanced ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy).Find(material.ShaderHash)
            ?? ShaderCatalog.Legacy.Find(material.ShaderHash) ?? ShaderCatalog.Enhanced.Find(material.ShaderHash);
        string shader = definition?.Name ?? $"0x{material.ShaderHash.Value:X8}";

        // The material's values by their Legacy names: a Legacy parameter's own, an Enhanced one's alias in the catalogue.
        var values = new Dictionary<string, MaterialParameterValue>(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialParameterValue value in material.GetParameters())
        {
            ShaderParameterDefinition? parameter = definition?.FindParameter(value.NameHash);
            string? name = (enhanced ? parameter?.LegacyName : null) ?? parameter?.Name ?? value.Name;
            if (name is not null)
                values.TryAdd(name, value);
        }

        var missing = new List<string>();
        var missingRoles = new HashSet<string>(StringComparer.Ordinal);
        Texture? Texture(string role)
        {
            foreach (string name in Names("textures", role))
                if (values.TryGetValue(name, out MaterialParameterValue? value) && value.Texture is { } reference)
                {
                    Texture? found = resolve(reference);
                    if (found is null && reference.Name is { } missingName)
                    {
                        missing.Add(missingName);
                        missingRoles.Add(role);
                    }
                    return found;
                }
            return null;
        }
        bool Declares(string group, string role) => Names(group, role).Any(name => definition?.FindParameter(name) is not null || values.ContainsKey(name));
        // The material's own value, else its shader's default, else the fallback.
        float[] Constant(string role, params float[] fallback)
        {
            foreach (string name in Names("constants", role))
            {
                if (values.TryGetValue(name, out MaterialParameterValue? value) && value.Values.Length > 0 && value.Values.Span.ToArray().All(float.IsFinite))
                    return value.Values.ToArray();
                if (definition?.FindParameter(name) is { Defaults.Count: > 0 } parameter)
                    return [.. parameter.Defaults];
            }
            return fallback;
        }

        var result = new SurfaceMaterial
        {
            Shader = shader,
            Rebuilt = Textured.Contains(shader) || Vehicles.Contains(shader) || Terrains.Contains(shader) || Peds.Contains(shader)
                || PedsDiffuse.Contains(shader) || PedsHair.Contains(shader) || Furs.Contains(shader) || Cables.Contains(shader),
            Vehicle = Vehicles.Contains(shader),
            Terrain = Terrains.Contains(shader) || ApproximateTerrains.Contains(shader),
            Ped = Peds.Contains(shader) || PedsDiffuse.Contains(shader) || PedsHair.Contains(shader) || ApproximatePeds.Contains(shader),
            Fur = Furs.Contains(shader),
            Cable = Cables.Contains(shader),
            FurHeights = Furs.Contains(shader) ? [.. new[] { "01", "23", "45", "67" }.Select(pair => Texture($"fur_height{pair}"))] : new Texture?[4],
            FurMask = Furs.Contains(shader) ? Texture("fur_mask") : null,
            DiffuseHf = Furs.Contains(shader) ? Texture("diffuse_hf") : null,
            Layers = [.. new[] { "layer", "layer_bump", "layer_height" }.SelectMany(role => Enumerable.Range(0, 4).Select(i => Texture($"{role}{i}")))],
            Lookup = Texture("lookup"),
            Diffuse = Texture("diffuse"),
            Normal = Texture("normal"),
            Specular = Texture("specular"),
            TintPalette = Texture("tint_palette"),
            Dirt = Texture("dirt"),
            DiffuseRamp = Texture("diffuse_ramp"),
            DiffuseLayer = Texture("diffuse2"),
            // The vehicles' detail shaders are drawn as the rest of their family, without one.
            Detail = Vehicles.Contains(shader) ? null : Texture("detail"),
            Height = Texture("height"),
            HairNoise = PedsHair.Contains(shader) ? Texture("hair_noise") : null,
            DiffuseMask = PedsHair.Contains(shader) ? Texture("diffuse_mask") : null,
            AlphaMode = (material.RenderBucket & 7) switch
            {
                1 => AlphaMode.Blend,
                3 => AlphaMode.Mask,
                _ => Pattern(shader, "alpha_mode") switch { "BLEND" => AlphaMode.Blend, "MASK" => AlphaMode.Mask, _ => AlphaMode.Opaque },
            },
            Decal = (material.RenderBucket & 7) == 2 || Pattern(shader, "render_pass") == "DECAL",
            MissingTextures = missing,
            MissingRoles = missingRoles,
        };

        Span<float> u = result.Uniforms;
        if (result.Terrain)
        {
            TerrainUniforms(u);
            return result;
        }
        if (result.Fur)
        {
            FurUniforms(u);
            return result;
        }
        if (result.Cable)
        {
            CableUniforms(u);
            return result;
        }

        // PS_DeferredTextured: a shader without specular constants (default, emissive) writes no specular, and a
        // fresnel of 0.98; one with them takes its power and intensity from them, scaled by a specular map if it has one.
        bool specular = Declares("constants", "specular_intensity");
        u[0] = specular ? Constant("fresnel", .97f)[0] : .98f;
        u[1] = specular ? Constant("specular_falloff", 100)[0] : 0;
        u[2] = specular ? Constant("specular_intensity", .125f)[0] : 0;
        u[3] = Constant("bumpiness", 1)[0];
        float[] mask = Constant("specular_mask", 1, 0, 0);
        for (int i = 0; i < 3; i++)
            u[4 + i] = i < mask.Length ? mask[i] : 0;
        u[7] = Constant("emissive_multiplier", 1)[0];
        u[8] = result.Normal is not null ? 1 : 0;
        u[9] = Declares("textures", "specular") ? 1 : 0;
        // A decal is drawn blended over what it lies on, whatever its own alpha mode, as the renderer draws it.
        u[10] = (float)(result.Decal ? AlphaMode.Blend : result.AlphaMode);
        // A tinted shader's vertex blue picks the palette's column, and a vehicle's is its dirt, so neither is an
        // emissive mask there.
        u[11] = Pattern(shader, "emissive") == "true" && result.TintPalette is null && !result.Vehicle ? 1 : 0;
        // GTA's alpha test passes at about half coverage.
        u[12] = .5f;
        u[13] = result.TintPalette is not null ? 1 : 0;
        u[14] = Constant("tint_palette_selector", 0)[0];
        u[15] = result.Vehicle ? 1 : 0;
        // The specular map is read on the first UV set, untiled, unless a vehicle shader says otherwise.
        u[41] = 1;
        // The _pxm shaders' parallax: heightScale, heightBias and parallaxSelfShadowAmount (the decals' programs cast no
        // self-shadow); globalHeightScale, which the game leaves at one.
        if (Pattern(shader, "parallax") == "true" && result.Height is not null)
        {
            u[68] = 1;
            u[69] = Constant("height_scale", .03f)[0];
            u[70] = Constant("height_bias", .015f)[0];
            u[71] = shader.Contains("decal", StringComparison.OrdinalIgnoreCase) ? 0 : Constant("parallax_self_shadow", .95f)[0];
            u[72] = 1;
        }
        // The decals' PS_DeferredTextured blends all four targets by the diffuse map's alpha times the vertex colour's.
        u[73] = result.Decal ? 1 : 0;
        // Where the specular comes from besides the constants and a specular map: the diffuse map's luminance
        // (emissive_speclum), or an overlay of its colour by SpecDesaturateIntensity and SpecDesaturateExponent (diffspec).
        string? source = Pattern(shader, "specular_source");
        u[56] = source switch { "luminance" => 1, "diffuse" => 2, _ => 0 };
        if (source == "diffuse")
        {
            float[] intensity = Constant("spec_desaturate_intensity", .3f, .6f, .1f, .625f), exponent = Constant("spec_desaturate_exponent", .3f, .6f, .1f, .625f);
            for (int i = 0; i < 4; i++)
            {
                u[60 + i] = i < intensity.Length ? intensity[i] : 0;
                u[64 + i] = i < exponent.Length ? exponent[i] : 0;
            }
        }
        // The _detail shaders: detailSettings (how much the detail darkens, how much it bumps, its tiling), and what
        // masks it: the specular map's alpha, the diffuse map's for the diffspec shaders, or nothing.
        if (result.Detail is not null)
        {
            float[] detail = Constant("detail_settings", 1.35f, 0, 7.5f, 8);
            for (int i = 0; i < 4; i++)
                u[48 + i] = i < detail.Length ? detail[i] : 0;
            u[52] = 1;
            u[53] = Declares("textures", "specular") ? 1 : Pattern(shader, "detail_mask") == "diffuse" ? 2 : 0;
        }
        if (result.Vehicle)
        {
            Vehicle(u);
            VehicleSpecular(u);
        }
        if (result.Ped)
            Ped(u);
        return result;

        // PS_DeferredPedTextured at rest: which of its programs, whether it has the rim light and the bend towards the
        // view, whether the diffuse alpha is a palette index, and hair's highlights, noise and alpha bias. The ped
        // programs cut out at alpha 128 of 255, inclusive; hair is solid only where it is all but opaque, and its second
        // pass blends in the rest.
        void Ped(Span<float> v)
        {
            v[76] = PedsHair.Contains(shader) ? 3 : PedsDiffuse.Contains(shader) ? 2 : 1;
            v[77] = Pattern(shader, "ped_rim") == "false" ? 0 : 1;
            v[78] = Pattern(shader, "palette_alpha") == "true" ? 1 : 0;
            v[79] = result.DiffuseMask is not null ? 1 : 0;
            v[12] = v[76] == 3 && result.AlphaMode == AlphaMode.Mask ? HairSolid : 128.5f / 255;
            if (v[76] != 3)
                return;
            float[] intensity = Constant("aniso_intensity", .1f, .15f), exponent = Constant("aniso_exponent", 16, 32);
            float[] colour = Constant("aniso_colour", .1f, .1f, .1f, 1), scale = Constant("hair_noise_scale", 2, 1, 3, 1);
            v[80] = intensity.ElementAtOrDefault(0);
            v[81] = intensity.ElementAtOrDefault(1);
            v[82] = exponent.ElementAtOrDefault(0);
            v[83] = exponent.ElementAtOrDefault(1);
            for (int i = 0; i < 3; i++)
                v[84 + i] = colour.ElementAtOrDefault(i);
            v[87] = Constant("aniso_alpha_bias", 1)[0];
            for (int i = 0; i < 4; i++)
                v[88 + i] = i < scale.Length ? scale[i] : 1;
        }

        // The terrain shaders' PS_TexturedDeferred: specularIntensityMult (squared by the program), specularFalloffMult and
        // a fixed fresnel of 0.98; the layer weights from the vertex, the lookup map or both; no bumps for the _lod shaders;
        // layers 1 to 3 on the second UV set for _2tex; the bump self-shadow; the _pxm shaders' parallax, by each layer's
        // height scale and bias, and the _spm ones' specular map.
        void TerrainUniforms(Span<float> v)
        {
            v[0] = Constant("specular_intensity", 0)[0];
            v[1] = Constant("specular_falloff", 48)[0];
            v[2] = Constant("bumpiness", 1)[0];
            v[3] = .98f;
            v[4] = Pattern(shader, "terrain_weights") switch { "blend" => 1, "map" => 2, _ => 0 };
            v[5] = Enumerable.Range(0, 4).Any(i => Declares("textures", $"layer_bump{i}")) ? 1 : 0;
            v[6] = result.TintPalette is not null ? 1 : 0;
            v[7] = Constant("tint_palette_selector", 0)[0];
            v[8] = Pattern(shader, "terrain_layers") == "second" ? 1 : 0;
            v[9] = Constant("bump_self_shadow", .3f)[0];
            bool parallax = Pattern(shader, "parallax") == "true";
            v[10] = parallax ? 1 : 0;
            v[11] = parallax ? Constant("parallax_self_shadow", .95f)[0] : 0;
            for (int i = 0; i < 4; i++)
            {
                v[12 + i] = parallax ? Constant($"height_scale{i}", .03f)[0] : 0;
                v[16 + i] = parallax ? Constant($"height_bias{i}", .015f)[0] : 0;
            }
            bool specularMap = Pattern(shader, "terrain_spec_map") == "true";
            v[20] = specularMap ? 1 : 0;
            v[21] = Constant("spec_map_intensity", 1)[0];
            v[22] = Constant("spec_intensity_adjust", 1)[0];
            v[23] = Constant("spec_map_falloff", 48)[0];
            v[24] = Constant("spec_falloff_adjust", 1)[0];
            // globalHeightScale, which the game leaves at one.
            v[25] = 1;
        }

        // fur.wgsl's Fur uniform, PS_FurDeferredTextured's constants: the specular, furLayerParams, furUvScales, which of
        // the three shaders, the tint palette's row, and each shell's clip and shade (furAlphaClip03/47, furShadow03/47).
        void FurUniforms(Span<float> v)
        {
            v[0] = Constant("fresnel", .97f)[0];
            v[1] = Constant("specular_falloff", 100)[0];
            v[2] = Constant("specular_intensity", .125f)[0];
            v[3] = Constant("bumpiness", 1)[0];
            Fill(v[4..7], Constant("specular_mask", 1, 0, 0));
            Fill(v[8..12], Constant("fur_layer", .008f, 0, 0, 1));
            Fill(v[12..16], Constant("fur_uv_scales", 1, 1, 1, 1));
            v[16] = shader.Contains("_mask", StringComparison.OrdinalIgnoreCase) ? 1 : result.TintPalette is not null ? 2 : 0;
            v[17] = Constant("tint_palette_selector", 0)[0];
            Fill(v[20..24], Constant("fur_clip03", 0, 10 / 255f, 10 / 255f, 15 / 255f));
            Fill(v[24..28], Constant("fur_clip47", 20 / 255f, 25 / 255f, 30 / 255f, 34 / 255f));
            Fill(v[28..32], Constant("fur_shade03", 90 / 255f, 118 / 255f, 148 / 255f, 168 / 255f));
            Fill(v[32..36], Constant("fur_shade47", 200 / 255f, 220 / 255f, 238 / 255f, 1));
        }

        // cable.wgsl's Cable uniform: shader_radiusScale, shader_fadeExponent, shader_cableEmissive and the game's alpha
        // test of 0.1 (it sets it on each draw, over the material's), then the two colours and the ambient scales.
        void CableUniforms(Span<float> v)
        {
            v[0] = Constant("cable_radius_scale", 1)[0];
            v[1] = Constant("cable_fade_exponent", 1)[0];
            v[2] = Constant("cable_emissive", 0)[0];
            v[3] = .1f;
            Fill(v[4..7], Constant("cable_diffuse", 0, 0, 0));
            Fill(v[8..11], Constant("cable_diffuse2", 0, 0, 0));
            Fill(v[12..14], Constant("cable_ambient", 0, 0));
        }

        // What the shader's program writes to the G-buffer's specular (materials.json's vehicle_specular).
        void VehicleSpecular(Span<float> v)
        {
            JsonElement table = Mapping.RootElement.GetProperty("vehicle_specular");
            JsonElement own = table.TryGetProperty(shader, out JsonElement found) ? found : default;
            JsonElement Setting(string name) => own.ValueKind == JsonValueKind.Object && own.TryGetProperty(name, out JsonElement value)
                ? value : table.GetProperty("default").TryGetProperty(name, out JsonElement fallback) ? fallback : default;
            string? Word(string name) => Setting(name) is { ValueKind: JsonValueKind.String } w ? w.GetString() : null;

            // A paint slot's material is drawn with its colour's metallic setting in place of its own specular, as the
            // game sets it on each draw: specInt, specFalloff and specFresnel for whichever of the three it has.
            CarMetallic? metallic = paint.Metallic(PaintLayer(Constant("paint_colour", 1, 1, 1)));
            float Own(string role, float fallback, float? metal) => Declares("constants", role) && metal is { } value ? value : Constant(role, fallback)[0];
            v[2] = Setting("scale") is { ValueKind: JsonValueKind.Number } scale ? scale.GetSingle() : Own("specular_intensity", .125f, metallic?.Intensity);
            v[1] = Setting("power_scale") is { ValueKind: JsonValueKind.Number } power ? power.GetSingle() : Own("specular_falloff", 100, metallic?.Falloff);
            v[0] = Setting("fresnel").ValueKind == JsonValueKind.Number ? Setting("fresnel").GetSingle() : Own("fresnel", .97f, metallic?.Fresnel);
            if (Setting("bump") is { ValueKind: JsonValueKind.Number } bump)
                v[3] = bump.GetSingle();
            v[36] = Word("map") switch { "masked" => 1, "red" => 2, _ => 0 };
            v[37] = Word("rough") switch { "dirt" => 1, "level" => 2, _ => 0 };
            v[38] = Word("power") switch { "alpha" => 1, "green" => 2, _ => 0 };
            v[39] = Word("fresnel") == "blue" ? 1 : 0;
            v[40] = Setting("uv") is { ValueKind: JsonValueKind.Number } uv ? uv.GetSingle() : 0;
            v[41] = Setting("tiled").ValueKind == JsonValueKind.True ? Constant("specular_tile", 1)[0] : 1;
            // The second diffuse layer, over the paint by its alpha times matDiffuseColor2's w: the paints read it on
            // the second UV set, vehicle_cloth2 on the first.
            v[42] = result.DiffuseLayer is not null ? 1 : 0;
            v[43] = shader.Contains("cloth", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            float[] layer = Constant("diffuse2_colour", 1, 1, 1, 1);
            for (int i = 0; i < 4; i++)
                v[44 + i] = i < layer.Length ? layer[i] : 1;
        }

        // PS_DeferredVehicleTextured: the paint's colour (or ramp), its dirt, and the second specular lobe. The game
        // sets a vehicle's colours and dirt as it drives; the viewer shows a clean vehicle, its paint slots as the game
        // fills them when no vehicle's colours apply.
        void Vehicle(Span<float> v)
        {
            Vector3 colour = PaintColour(Constant("paint_colour", 1, 1, 1), paint);
            v[16] = colour.X;
            v[17] = colour.Y;
            v[18] = colour.Z;
            v[19] = result.DiffuseRamp is not null ? Constant("ramp_enabled", 0)[0] : 0;
            float[] dirt = Constant("dirt_level", 0, 1, 0, 1);
            for (int i = 0; i < 4; i++)
                v[20 + i] = i < dirt.Length ? dirt[i] : 0;
            v[20] = 0;
            float[] dirtColour = Constant("dirt_colour", .23f, .22f, .2f);
            for (int i = 0; i < 3; i++)
                v[24 + i] = i < dirtColour.Length ? dirtColour[i] : 0;
            v[27] = result.Dirt is not null ? 1 : 0;
            // The second lobe's colour is the game's to set as well (its default, 0 0.5 0, is a placeholder): it is
            // taken to be the pearlescent paint's, and only how far its light swings overhead is the material's.
            bool second = Declares("constants", "specular2");
            float[] lobe = Constant("specular2", 0, 0, 0, 0);
            Vector3 pearl = second ? paint.Layer(Pearlescent) : Vector3.Zero;
            v[28] = pearl.X;
            v[29] = pearl.Y;
            v[30] = pearl.Z;
            v[31] = second && lobe.Length > 3 ? lobe[3] : 0;
            v[32] = Pattern(shader, "paint") == "true" ? 1 : 0;
            v[33] = second ? 1 : 0;
            v[34] = result.DiffuseRamp is not null ? 1 : 0;
        }
    }

    private const int Pearlescent = 3;

    // A constant's values into its place, as many as there are room for and the material has, the rest nothing.
    private static void Fill(Span<float> into, float[] values)
    {
        for (int i = 0; i < into.Length; i++)
            into[i] = i < values.Length ? values[i] : 0;
    }

    /// <summary>
    /// A vehicle material's diffuse colour. matDiffuseColor (2, n, n) isn't a colour but one of the vehicle's seven
    /// paint slots, n - 1, which the game fills in: 0 primary, 1 secondary, 2 pearlescent, 3 wheels, 4 left as it is,
    /// 5 interior and 6 dashboard, drawn in <paramref name="paint"/>'s colours (the game's grey for each, the fifth
    /// white, without). Any other value is the colour itself.
    /// </summary>
    internal static Vector3 PaintColour(ReadOnlySpan<float> value, VehiclePaint? paint = null) =>
        PaintLayer(value) is int layer and > 0
            ? (paint ?? VehiclePaint.Unset).Layer(layer)
            : new(value.Length > 0 ? value[0] : 1, value.Length > 1 ? value[1] : 1, value.Length > 2 ? value[2] : 1);

    /// <summary>The paint slot, 1 to 7, a vehicle material's matDiffuseColor (2, n, n) names; 0 for a colour.</summary>
    internal static int PaintLayer(ReadOnlySpan<float> value) =>
        value.Length > 1 && MathF.Abs(value[0] - 2) < .001f && MathF.Round(value[1]) is >= 1 and <= 7 and var layer ? (int)layer : 0;

    private static IEnumerable<string> Names(string group, string role) =>
        Mapping.RootElement.GetProperty(group).TryGetProperty(role, out JsonElement names) ? names.EnumerateArray().Select(n => n.GetString()!) : [];

    /// <summary>A setting for a shader: its own entry in materials.json, else the first pattern its name matches.</summary>
    private static string? Pattern(string shader, string setting)
    {
        if (Mapping.RootElement.GetProperty("shaders").TryGetProperty(shader, out JsonElement own) && own.TryGetProperty(setting, out JsonElement value))
            return Text(value);
        foreach (JsonElement pattern in Mapping.RootElement.GetProperty("patterns").EnumerateArray())
            if (pattern.TryGetProperty("contains", out JsonElement contains) && shader.Contains(contains.GetString()!, StringComparison.OrdinalIgnoreCase)
                && pattern.TryGetProperty(setting, out JsonElement found))
                return Text(found);
        return null;

        static string Text(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ToString().ToLowerInvariant() : value.ToString();
    }
}
