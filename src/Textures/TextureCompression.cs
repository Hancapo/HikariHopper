using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.Textures;

/// <summary>Conservative format optimization for conventional DXT textures only (port of <c>texture_compression.py</c>).</summary>
public static class TextureCompression
{
    /// <summary>Re-encodes a DXT1/3/5 texture as DXT1 when every stored mip is opaque, otherwise as DXT5. Each stored
    /// level is re-encoded independently at maximum quality; nothing is regenerated or resized. Returns the source when
    /// it is not DXT or already has the target format.</summary>
    public static Texture Optimize(Texture texture)
    {
        if (!TextureFormatNames.IsDxt(texture.Format))
            return texture;
        bool transparent = false;
        // A lower authored mip may contain alpha even when the base level is opaque.
        for (int mip = 0; mip < texture.MipCount && !transparent; mip++)
            transparent = TextureProcessing.Decode(texture, mip).HasTransparency;
        TextureFormat target = transparent ? TextureFormat.BC3Unorm : TextureFormat.BC1Unorm;
        if (TextureFormats.IsSrgb(texture.Format))
            target = TextureFormats.Srgb(target);
        if (texture.Format == target)
            return texture;
        return TextureProcessing.ConvertFormat(texture, target, quality: 1, Bc1AlphaMode.Opaque);
    }
}
