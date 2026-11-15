using FiveFury.Textures;
using RedMaterial = RedFury.Drawables.Material;
using RedReference = RedFury.Drawables.TextureReference;

namespace HikariHopper.Rendering;

internal sealed partial class SurfaceMaterial
{
    /// <summary>
    /// For a Red Dead Redemption 2 material, RedFury's (<see cref="Rdr2Material"/>), drawn with rdr2.wgsl; null for GTA V's.
    /// </summary>
    public Rdr2Material? Rdr2 { get; private init; }

    /// <summary>
    /// A Red Dead Redemption 2 material: RedFury's material, drawn with rdr2.wgsl, and, for a GPU that can't bind its
    /// textures, surface.wgsl's plain textured program over its colour and bumps. Textures it names that aren't found
    /// are in <see cref="MissingTextures"/>.
    /// </summary>
    public static SurfaceMaterial FromRdr2(RedMaterial material, Func<RedReference, Texture?> resolve)
    {
        Rdr2Material rdr2 = Rdr2Material.From(material, resolve);
        var result = new SurfaceMaterial
        {
            Shader = material.ShaderName,
            Rebuilt = true,
            Rdr2 = rdr2,
            Diffuse = rdr2.Textures[0],
            Normal = rdr2.Textures[1],
            AlphaMode = rdr2.Blend ? AlphaMode.Blend : rdr2.AlphaMode == 1 ? AlphaMode.Mask : AlphaMode.Opaque,
            Decal = rdr2.Decal,
            MissingTextures = [.. rdr2.MissingTextures.Order(StringComparer.OrdinalIgnoreCase)],
            MissingRoles = rdr2.MissingBase ? new HashSet<string> { "diffuse" } : new HashSet<string>(),
        };

        // The fallback: PS_DeferredTextured with a dull, even specular.
        Span<float> u = result.Uniforms;
        u[0] = .97f;
        u[1] = 24;
        u[2] = .08f;
        u[3] = 1;
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
