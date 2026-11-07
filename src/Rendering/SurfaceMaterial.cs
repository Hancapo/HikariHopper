using System.Text.Json;
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
/// A GTA V material made ready for surface.wgsl: its textures and the constants its shader reads, from either edition.
/// Which parameter fills which role is in materials.json, by Legacy name; Enhanced materials reach those names through
/// FiveFury's shader catalogue, and a value a material leaves out is its shader's default from the same catalogue.
/// </summary>
internal sealed class SurfaceMaterial
{
    /// <summary>The bytes of surface.wgsl's Material uniform.</summary>
    public const int UniformBytes = 64;

    /// <summary>The Material uniform, as floats: see surface.wgsl.</summary>
    public float[] Uniforms { get; } = new float[UniformBytes / sizeof(float)];

    /// <summary>The shader's name, or its hash when the catalogues don't know it.</summary>
    public string Shader { get; private init; } = "";

    /// <summary>Whether the shader is one of the families surface.wgsl rebuilds; others are drawn as the closest it does.</summary>
    public bool Rebuilt { get; private init; }

    public AlphaMode AlphaMode { get; private init; }

    /// <summary>A decal: drawn over the surface it sits on, without writing depth.</summary>
    public bool Decal { get; private init; }

    public Texture? Diffuse { get; private init; }
    public Texture? Normal { get; private init; }
    public Texture? Specular { get; private init; }

    /// <summary>The palette a tinted (_tnt) shader takes its colour from: a row per tint, a column per vertex.</summary>
    public Texture? TintPalette { get; private init; }

    /// <summary>Textures the material names that neither the drawable nor its dictionaries hold.</summary>
    public IReadOnlyList<string> MissingTextures { get; private init; } = [];

    private static readonly JsonDocument Mapping = JsonDocument.Parse(ViewportRenderer.Read("materials.json"));

    private static readonly HashSet<string> Textured = Mapping.RootElement.GetProperty("families").GetProperty("textured")
        .EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        Texture? Texture(string role)
        {
            foreach (string name in Names("textures", role))
                if (values.TryGetValue(name, out MaterialParameterValue? value) && value.Texture is { } reference)
                {
                    Texture? found = resolve(reference);
                    if (found is null && reference.Name is { } missingName)
                        missing.Add(missingName);
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
            Rebuilt = Textured.Contains(shader),
            Diffuse = Texture("diffuse"),
            Normal = Texture("normal"),
            Specular = Texture("specular"),
            TintPalette = Texture("tint_palette"),
            AlphaMode = (material.RenderBucket & 7) switch
            {
                1 => AlphaMode.Blend,
                3 => AlphaMode.Mask,
                _ => Pattern(shader, "alpha_mode") switch { "BLEND" => AlphaMode.Blend, "MASK" => AlphaMode.Mask, _ => AlphaMode.Opaque },
            },
            Decal = (material.RenderBucket & 7) == 2 || Pattern(shader, "render_pass") == "DECAL",
            MissingTextures = missing,
        };

        // PS_DeferredTextured: a shader without specular constants (default, emissive) writes no specular, and a
        // fresnel of 0.98; one with them takes its power and intensity from them, scaled by a specular map if it has one.
        Span<float> u = result.Uniforms;
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
        u[10] = (float)result.AlphaMode;
        // A tinted shader's vertex blue picks the palette's column, so it isn't an emissive mask there.
        u[11] = Pattern(shader, "emissive") == "true" && result.TintPalette is null ? 1 : 0;
        // GTA's alpha test passes at about half coverage.
        u[12] = .5f;
        u[13] = result.TintPalette is not null ? 1 : 0;
        u[14] = Constant("tint_palette_selector", 0)[0];
        return result;
    }

    private static IEnumerable<string> Names(string group, string role) =>
        Mapping.RootElement.GetProperty(group).TryGetProperty(role, out JsonElement names) ? names.EnumerateArray().Select(n => n.GetString()!) : [];

    /// <summary>A setting for a shader: its own entry in materials.json, else the first pattern its name matches.</summary>
    private static string? Pattern(string shader, string setting)
    {
        if (Mapping.RootElement.GetProperty("shaders").TryGetProperty(shader, out JsonElement own) && own.TryGetProperty(setting, out JsonElement value))
            return value.ToString();
        foreach (JsonElement pattern in Mapping.RootElement.GetProperty("patterns").EnumerateArray())
            if (pattern.TryGetProperty("contains", out JsonElement contains) && shader.Contains(contains.GetString()!, StringComparison.OrdinalIgnoreCase)
                && pattern.TryGetProperty(setting, out JsonElement found))
                return found.ValueKind is JsonValueKind.True or JsonValueKind.False ? found.ToString().ToLowerInvariant() : found.ToString();
        return null;
    }
}
