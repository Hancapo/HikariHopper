using System.Text.Json;
using FiveFury.Textures;
using RedMaterial = RedFury.Drawables.Material;
using RedReference = RedFury.Drawables.TextureReference;

namespace HikariHopper.Rendering;

internal sealed partial class SurfaceMaterial
{
    // RDR2's shaders, as RedFury's viewer maps them: which parameter fills which role, and the settings their names imply.
    private static readonly JsonDocument Rdr2Mapping = JsonDocument.Parse(ViewportRenderer.Read("rdr2_materials.json"));

    /// <summary>
    /// A Red Dead Redemption 2 material drawn with surface.wgsl's plain textured program, until RDR2's own material
    /// replaces it: its colour, its bumps and how it's cut out or blended, as RedFury's viewer reads them. What sets
    /// RDR2's shaders apart (its PBR maps, layers, tints, terrain blends, water) isn't drawn yet; textures it names that
    /// aren't found are in <see cref="MissingTextures"/>.
    /// </summary>
    public static SurfaceMaterial FromRdr2(RedMaterial material, Func<RedReference, Texture?> resolve)
    {
        string shader = material.ShaderName;
        JsonElement root = Rdr2Mapping.RootElement;
        JsonElement own = root.GetProperty("shaders").EnumerateObject()
            .FirstOrDefault(entry => string.Equals(entry.Name, shader, StringComparison.OrdinalIgnoreCase)).Value;

        IEnumerable<string> Candidates(string group, string role) =>
            own.ValueKind == JsonValueKind.Object && own.TryGetProperty(group, out JsonElement overrides) && overrides.TryGetProperty(role, out JsonElement names)
                ? names.EnumerateArray().Select(name => name.GetString()!)
                : root.GetProperty("defaults").GetProperty(group).TryGetProperty(role, out names) ? names.EnumerateArray().Select(name => name.GetString()!) : [];
        var missing = new List<string>();
        var missingRoles = new HashSet<string>(StringComparer.Ordinal);
        Texture? Find(string role)
        {
            if (Candidates("textures", role).Select(material.Parameter).FirstOrDefault(parameter => parameter?.Texture is not null)?.Texture is not { } reference)
                return null;
            Texture? found = resolve(reference);
            if (found is null)
            {
                missing.Add(reference.Name.Length > 0 ? reference.Name : $"0x{reference.NameHash:X8}");
                missingRoles.Add(role);
            }
            return found;
        }
        float Scalar(string role, float fallback)
        {
            foreach (string name in Candidates("scalars", role).Concat(Candidates("constants", role)))
                if (material.Parameter(name)?.FloatValues() is { Length: > 0 } values && float.IsFinite(values[0]))
                    return values[0];
            return fallback;
        }
        // The shader's own setting, else the first pattern its name matches.
        string State(string name, string fallback)
        {
            if (own.ValueKind == JsonValueKind.Object && own.TryGetProperty(name, out JsonElement value))
                return value.ToString();
            foreach (JsonElement pattern in root.GetProperty("patterns").EnumerateArray())
                if ((pattern.TryGetProperty("contains", out JsonElement contains) && shader.Contains(contains.GetString()!, StringComparison.OrdinalIgnoreCase)
                    || pattern.TryGetProperty("suffix", out JsonElement suffix) && shader.EndsWith(suffix.GetString()!, StringComparison.OrdinalIgnoreCase))
                    && pattern.TryGetProperty(name, out JsonElement found))
                    return found.ToString();
            return fallback;
        }

        string alpha = material.RenderBucket switch { 1 => "BLEND", 3 => "MASK", _ => State("alpha_mode", "OPAQUE") };
        var result = new SurfaceMaterial
        {
            Shader = shader,
            // Drawn as a plain textured surface for now: not RDR2's own shader, which the diagnostics say.
            Rebuilt = false,
            Diffuse = Find("diffuse") ?? Find("layer0_diffuse"),
            Normal = Find("normal") ?? Find("layer0_normal"),
            AlphaMode = alpha switch { "BLEND" => AlphaMode.Blend, "MASK" => AlphaMode.Mask, _ => AlphaMode.Opaque },
            Decal = State("render_pass", "OPAQUE") == "DECAL",
            MissingTextures = missing,
            MissingRoles = missingRoles,
        };

        // surface.wgsl's PS_DeferredTextured with a dull, even specular: RDR2's shaders keep theirs in PBR maps the
        // program doesn't read.
        Span<float> u = result.Uniforms;
        u[0] = .97f;
        u[1] = 24;
        u[2] = .08f;
        u[3] = Math.Max(0, Scalar("bumpiness", 1));
        u[4] = 1;
        u[7] = 1;
        u[8] = result.Normal is not null ? 1 : 0;
        u[10] = (float)(result.Decal ? AlphaMode.Blend : result.AlphaMode);
        u[12] = .5f;
        u[41] = 1;
        u[73] = result.Decal ? 1 : 0;
        return result;
    }
}
