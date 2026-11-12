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
/// A GTA V material made ready for surface.wgsl, or terrain.wgsl for the terrain shaders: its textures and the
/// constants its shader reads, from either edition.
/// Which parameter fills which role is in materials.json, by Legacy name; Enhanced materials reach those names through
/// FiveFury's shader catalogue, and a value a material leaves out is its shader's default from the same catalogue.
/// </summary>
internal sealed class SurfaceMaterial
{
    /// <summary>The bytes of surface.wgsl's Material uniform; terrain.wgsl's Terrain uniform is its first 112.</summary>
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

    private static HashSet<string> Family(string name) => Mapping.RootElement.GetProperty("families").GetProperty(name)
        .EnumerateArray().Select(shader => shader.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes a material ready to draw, finding its textures with <paramref name="resolve"/>.</summary>
    public static SurfaceMaterial From(DrawableMaterial material, Func<TextureReference, Texture?> resolve)
    {
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
                || PedsDiffuse.Contains(shader) || PedsHair.Contains(shader),
            Vehicle = Vehicles.Contains(shader),
            Terrain = Terrains.Contains(shader) || ApproximateTerrains.Contains(shader),
            Ped = Peds.Contains(shader) || PedsDiffuse.Contains(shader) || PedsHair.Contains(shader) || ApproximatePeds.Contains(shader),
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

        // What the shader's program writes to the G-buffer's specular (materials.json's vehicle_specular).
        void VehicleSpecular(Span<float> v)
        {
            JsonElement table = Mapping.RootElement.GetProperty("vehicle_specular");
            JsonElement own = table.TryGetProperty(shader, out JsonElement found) ? found : default;
            JsonElement Setting(string name) => own.ValueKind == JsonValueKind.Object && own.TryGetProperty(name, out JsonElement value)
                ? value : table.GetProperty("default").TryGetProperty(name, out JsonElement fallback) ? fallback : default;
            float Number(string name, string role, float fallback) => Setting(name) is { ValueKind: JsonValueKind.Number } n ? n.GetSingle() : Constant(role, fallback)[0];
            string? Word(string name) => Setting(name) is { ValueKind: JsonValueKind.String } w ? w.GetString() : null;

            v[2] = Number("scale", "specular_intensity", .125f);
            v[1] = Number("power_scale", "specular_falloff", 100);
            v[0] = Setting("fresnel").ValueKind == JsonValueKind.Number ? Setting("fresnel").GetSingle() : Constant("fresnel", .97f)[0];
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
            float[] paint = Constant("paint_colour", 1, 1, 1);
            Vector3 colour = PaintColour(paint);
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
            Vector3 pearl = second ? Layer(Pearlescent) : Vector3.Zero;
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
    private const int Unpainted = 5;

    /// <summary>
    /// A vehicle material's diffuse colour. matDiffuseColor (2, n, n) isn't a colour but one of the vehicle's seven
    /// paint slots, n - 1, which the game fills in: 0 primary, 1 secondary, 2 pearlescent, 3 wheels, 4 left as it is,
    /// 5 interior and 6 dashboard. Any other value is the colour itself.
    /// </summary>
    internal static Vector3 PaintColour(ReadOnlySpan<float> value)
    {
        Vector3 colour = new(value.Length > 0 ? value[0] : 1, value.Length > 1 ? value[1] : 1, value.Length > 2 ? value[2] : 1);
        return MathF.Abs(colour.X - 2) < .001f && MathF.Round(colour.Y) is >= 1 and <= 7 ? Layer((int)MathF.Round(colour.Y)) : colour;
    }

    /// <summary>
    /// A paint slot's colour with no vehicle's colours to fill it, as the game sets them: grey 0x96 for every slot
    /// but the fifth, which is white (the game greys it only for some vehicles).
    /// </summary>
    private static Vector3 Layer(int layer) => layer == Unpainted ? Vector3.One : new Vector3(150 / 255f);

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
