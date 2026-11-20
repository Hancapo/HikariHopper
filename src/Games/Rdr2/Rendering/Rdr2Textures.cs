using System.Runtime.CompilerServices;
using FiveFury.Textures;
using HikariHopper.Textures;
using RedTexture = RedFury.Textures.Texture;
using RedYtd = RedFury.Ytd.Ytd;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's textures in the viewer's texture model, FiveFury's (name, size, DXGI format, mips, a linear
/// mip chain), which they convert to as they are: the two libraries' formats are both DXGI's. Each is converted once,
/// so every material drawing it shares one texture, and one upload of it.
/// </summary>
internal static class Rdr2Textures
{
    private static readonly ConditionalWeakTable<RedTexture, Texture?> Converted = [];

    /// <summary>A texture as the viewer draws it; null for a volume or array texture, which it doesn't.</summary>
    public static Texture? Of(RedTexture texture) => Converted.GetValue(texture, static texture => texture.Depth != 1 ? null
        : new Texture(texture.Name, texture.Width, texture.Height, (CommonFury.Textures.TextureFormat)(uint)texture.Format,
            texture.Mips.Count, texture.Data.Span));

    /// <summary>A standalone RDR2 texture dictionary's textures as one the viewer reads: what it can draw of them.</summary>
    public static TextureDictionary Dictionary(ReadOnlySpan<byte> data)
    {
        var dictionary = new TextureDictionary();
        foreach (RedTexture texture in RedYtd.Read(data).Textures)
            if (Of(texture) is { } converted)
                dictionary.Textures.Add(converted);
        return dictionary;
    }

    /// <summary>A standalone texture dictionary of either game, as the viewer reads it.</summary>
    public static TextureDictionary Parse(byte[] data) =>
        Rdr2TextureSource.IsRdr2(data) ? Dictionary(data) : FiveFury.Ytd.Ytd.Read(data).Dictionary;
}
