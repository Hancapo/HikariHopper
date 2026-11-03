using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.Textures;

/// <summary>Storage names shown by the editor. They keep the terminology of the Python reference (<c>BC1</c>,
/// <c>A8R8G8B8</c>, <c>R16_FLOAT</c>…), which <see cref="Core.Formatting.FormatTextureFormat"/> maps to friendly labels.</summary>
public static class TextureFormatNames
{
    /// <summary>Formats offered by the Change format dialog, in menu order.</summary>
    public static IReadOnlyList<string> Editable { get; } = ["BC1", "BC2", "BC3", "BC4", "BC5", "BC7", "A8R8G8B8"];

    /// <summary>Conventional DXT formats that compression optimization may rewrite.</summary>
    public static bool IsDxt(TextureFormat format) => Name(format) is "BC1" or "BC2" or "BC3";

    public static string Name(TextureFormat format) => TextureFormats.Linear(format) switch
    {
        TextureFormat.BC1Unorm => "BC1",
        TextureFormat.BC2Unorm => "BC2",
        TextureFormat.BC3Unorm => "BC3",
        TextureFormat.BC4Unorm or TextureFormat.BC4Snorm => "BC4",
        TextureFormat.BC5Unorm or TextureFormat.BC5Snorm => "BC5",
        TextureFormat.BC6HUf16 or TextureFormat.BC6HSf16 => "BC6H",
        TextureFormat.BC7Unorm => "BC7",
        TextureFormat.B8G8R8A8Unorm => "A8R8G8B8",
        TextureFormat.B8G8R8X8Unorm => "X8R8G8B8",
        TextureFormat.R8G8B8A8Unorm => "R8G8B8A8",
        TextureFormat.B5G6R5Unorm => "B5G6R5",
        TextureFormat.B5G5R5A1Unorm => "B5G5R5A1",
        TextureFormat.R10G10B10A2Unorm => "R10G10B10A2",
        TextureFormat.R8Unorm => "R8",
        TextureFormat.A8Unorm => "A8",
        TextureFormat.R8G8Unorm => "R8G8",
        TextureFormat.R16Float => "R16_FLOAT",
        TextureFormat.R16G16Float => "R16G16_FLOAT",
        TextureFormat.R16G16B16A16Float => "R16G16B16A16_FLOAT",
        TextureFormat.R32Float => "R32_FLOAT",
        TextureFormat.R32G32B32A32Float => "R32G32B32A32_FLOAT",
        TextureFormat.Unknown => "UNKNOWN",
        _ => Enum.IsDefined(format) ? format.ToString().ToUpperInvariant() : ((uint)format).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>The storage format for an editable name, keeping the sRGB tag of <paramref name="source"/> when the
    /// target format has one.</summary>
    public static TextureFormat FromEditable(string name, TextureFormat source)
    {
        TextureFormat linear = name switch
        {
            "BC1" => TextureFormat.BC1Unorm,
            "BC2" => TextureFormat.BC2Unorm,
            "BC3" => TextureFormat.BC3Unorm,
            "BC4" => TextureFormat.BC4Unorm,
            "BC5" => TextureFormat.BC5Unorm,
            "BC7" => TextureFormat.BC7Unorm,
            "A8R8G8B8" => TextureFormat.B8G8R8A8Unorm,
            _ => throw new ArgumentException($"Unsupported texture format: {name}"),
        };
        return TextureFormats.IsSrgb(source) ? TextureFormats.Srgb(linear) : linear;
    }

    /// <summary>Python's <c>TextureUsage</c> member names (<c>DEFAULT</c>, <c>ENVEFF</c>…); undefined values show as numbers.</summary>
    public static string Usage(TextureUsage usage) => usage switch
    {
        TextureUsage.EnvironmentEffect => "ENVEFF",
        _ when Enum.IsDefined(usage) => usage.ToString().ToUpperInvariant(),
        _ => ((byte)usage).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
