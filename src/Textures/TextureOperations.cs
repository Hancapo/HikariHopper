using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.Textures;

/// <summary>Editor transforms on encoded textures, ported from the TexFury calls of <c>texture_viewer.py</c> to
/// <see cref="TextureProcessing"/>. Every operation returns a new texture, or the source itself when nothing changed;
/// sources are never modified.</summary>
public static class TextureOperations
{
    /// <summary>TexFury's default encoder effort for resize and format conversion.</summary>
    public const float DefaultQuality = 0.7f;

    /// <summary>Preview channel modes.</summary>
    public static IReadOnlySet<string> Channels { get; } = new HashSet<string>(StringComparer.Ordinal) { "rgba", "r", "g", "b", "a" };

    private static readonly Dictionary<string, ImageFilter> MipFilters = new(StringComparer.Ordinal)
    {
        ["box"] = ImageFilter.Box,
        ["triangle"] = ImageFilter.Triangle,
        ["cubic-bspline"] = ImageFilter.CubicBSpline,
        ["catmull-rom"] = ImageFilter.CatmullRom,
        ["mitchell"] = ImageFilter.Mitchell,
        ["point"] = ImageFilter.Point,
    };

    /// <summary>Resolves a dialog filter name (<c>mitchell</c>, <c>catmull-rom</c>…), ignoring case.</summary>
    /// <exception cref="ArgumentException">Unknown filter.</exception>
    public static ImageFilter MipFilter(string name) =>
        MipFilters.TryGetValue(name.ToLowerInvariant(), out ImageFilter filter) ? filter
            : throw new ArgumentException($"Unsupported mip filter: {name}");

    /// <summary>Decodes one stored mip to RGBA8; a single channel is shown as opaque grey (port of <c>decode_texture_image</c>).</summary>
    public static RgbaImage DecodeImage(Texture texture, string mode = "rgba", int mip = 0)
    {
        if (!Channels.Contains(mode))
            throw new ArgumentException($"Unsupported texture channel: {mode}");
        if (mip < 0 || mip >= texture.MipCount)
            throw new ArgumentException($"Mip level {mip} is outside the stored texture chain");
        return ChannelView(TextureProcessing.Decode(texture, mip), mode);
    }

    /// <summary>
    /// <paramref name="image"/> as <paramref name="mode"/> shows it: unchanged for <c>rgba</c>, otherwise that one
    /// channel as opaque grey. One pass over memory, so a channel needs no new decode.
    /// </summary>
    public static RgbaImage ChannelView(RgbaImage image, string mode)
    {
        if (mode == "rgba")
            return image;
        int channel = mode switch { "r" => 0, "g" => 1, "b" => 2, "a" => 3, _ => throw new ArgumentException($"Unsupported texture channel: {mode}") };
        ReadOnlySpan<byte> source = image.Pixels;
        var pixels = new byte[source.Length];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte value = source[offset + channel];
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        return new RgbaImage(image.Width, image.Height, pixels);
    }

    /// <summary>Port of <c>_resize_texture</c>: TexFury <c>resize</c> decodes mip 0, resamples with the mip filter and
    /// re-encodes in the same format at quality 0.7. <paramref name="mipStopSize"/> is the native stop value.</summary>
    public static Texture Resize(Texture texture, int width, int height, ImageFilter filter, int mipStopSize, bool generateMipmaps)
    {
        var options = new TextureEncodingOptions
        {
            Format = texture.Format,
            Quality = DefaultQuality,
            GenerateMipmaps = generateMipmaps,
            MinimumMipDimension = Math.Max(1, mipStopSize),
            MipFilter = filter,
            Bc1Alpha = SourceBc1Alpha(texture),
        };
        return TextureProcessing.Resize(texture, width, height, options, new ImageResizeOptions(filter, ColorSpace(texture.Format)));
    }

    /// <summary>Port of <c>_recalculate_mipmaps</c>: regenerates lower mips from the base image. Unlike TexFury, mip 0
    /// keeps its encoded bytes instead of being re-encoded.</summary>
    public static Texture RecalculateMipmaps(Texture texture, ImageFilter filter, int minMipSize) =>
        TextureProcessing.GenerateMipmaps(texture, TextureDimensions.MipStopSize(texture.Width, texture.Height, minMipSize),
            filter, DefaultQuality);

    /// <summary>Port of <c>_change_texture_format</c>: TexFury <c>to_format</c> re-encodes mip 0 into the new format and
    /// regenerates the full chain. An explicit BC1 choice is opaque, as TexFury's <c>BC1</c>.</summary>
    public static Texture ChangeFormat(Texture texture, TextureFormat format, float quality, ImageFilter filter, int minMipSize)
    {
        var options = new TextureEncodingOptions
        {
            Format = format,
            Quality = Math.Clamp(quality, 0, 1),
            GenerateMipmaps = true,
            MinimumMipDimension = TextureDimensions.MipStopSize(texture.Width, texture.Height, minMipSize),
            MipFilter = filter,
        };
        return Adopt(TextureProcessing.Encode(TextureProcessing.Decode(texture), options, texture.Name), texture);
    }

    /// <summary>Port of <c>_resize_to_power_of_two</c>; textures already at the rounded size are returned unchanged.</summary>
    public static Texture ResizeToPowerOfTwo(Texture texture, string mode, ImageFilter filter, int minMipSize, bool generateMipmaps)
    {
        (int width, int height) = TextureDimensions.PowerOfTwoDimensions(texture.Width, texture.Height, mode);
        if (width == texture.Width && height == texture.Height)
            return texture;
        return Resize(texture, width, height, filter, TextureDimensions.MipStopSize(width, height, minMipSize), generateMipmaps);
    }

    /// <summary>Port of <c>_replace_texture_from_image</c>: a DDS keeps its encoded chain when it already matches;
    /// other images are resampled to the texture's size and encoded in its format at quality 0.7.</summary>
    public static Texture ReplaceFromImage(Texture texture, string path, int width, int height, bool generateMipmaps, int mipStopSize)
    {
        Texture replacement;
        if (string.Equals(Path.GetExtension(path), ".dds", StringComparison.OrdinalIgnoreCase))
        {
            replacement = Texture.FromSurface(DdsCodec.Read(path), texture.Name);
            if (replacement.Width != width || replacement.Height != height)
                replacement = Resize(replacement, width, height, ImageFilter.Mitchell, mipStopSize, generateMipmaps);
            if (replacement.Format != texture.Format)
            {
                var options = new TextureEncodingOptions
                {
                    Format = texture.Format,
                    Quality = DefaultQuality,
                    GenerateMipmaps = generateMipmaps,
                    MinimumMipDimension = Math.Max(1, mipStopSize),
                    Bc1Alpha = SourceBc1Alpha(texture.Format, TextureProcessing.Decode(replacement)),
                };
                replacement = TextureProcessing.Encode(TextureProcessing.Decode(replacement), options, texture.Name);
            }
        }
        else
        {
            RgbaImage image = TextureImport.ReadImageFile(path)
                .Resize(width, height, new ImageResizeOptions(ImageFilter.Mitchell, ColorSpace(texture.Format)));
            var options = new TextureEncodingOptions
            {
                Format = texture.Format,
                Quality = DefaultQuality,
                GenerateMipmaps = generateMipmaps,
                MinimumMipDimension = Math.Max(1, mipStopSize),
                Bc1Alpha = SourceBc1Alpha(texture.Format, image),
            };
            replacement = TextureProcessing.Encode(image, options, texture.Name);
        }
        return Adopt(replacement, texture);
    }

    /// <summary>Port of <c>_to_fivefury_texture(texture, source)</c>: a newly created texture, not yet shared, takes the
    /// source's name and usage. Without a source it keeps its own name and the default usage.</summary>
    public static Texture Adopt(Texture created, Texture? source)
    {
        if (source is not null)
        {
            created.Name = source.Name;
            created.Usage = source.Usage;
            created.UsageFlags = source.UsageFlags;
        }
        return created;
    }

    /// <summary>Like <see cref="Adopt"/> for a texture that may be shared: returns it unchanged when it already carries the
    /// source's name and usage, otherwise a copy that does.</summary>
    public static Texture CarryMetadata(Texture result, Texture source)
    {
        if (ReferenceEquals(result, source)
            || result.Name == source.Name && result.Usage == source.Usage && result.UsageFlags == source.UsageFlags)
            return result;
        return Adopt(Copy(result, source.Name), source);
    }

    /// <summary>A renamed copy (Python <c>dataclasses.replace(texture, name=…)</c>) with the same encoded chain and usage.</summary>
    /// <remarks>FiveFury.NET does not expose cloning of retained source descriptor bytes, so a renamed texture is written
    /// with a canonical descriptor. The YDR preservation adapter restores the original descriptor bytes itself.</remarks>
    public static Texture WithName(Texture texture, string name) => Copy(texture, name);

    private static Texture Copy(Texture texture, string? name) =>
        new(name, texture.Width, texture.Height, texture.Format, texture.MipCount, texture.Data, texture.Shape)
        {
            Usage = texture.Usage,
            UsageFlags = texture.UsageFlags,
            ReferenceCount = texture.ReferenceCount,
        };

    private static ImageColorSpace ColorSpace(TextureFormat format) =>
        TextureFormats.IsSrgb(format) ? ImageColorSpace.Srgb : ImageColorSpace.Linear;

    // TexFury distinguished BC1 (opaque) from BC1A; DXGI does not. Keep an existing BC1 texture's punch-through
    // alpha whenever its own pixels are re-encoded, exactly as FiveFury's inferred Resize/GenerateMipmaps do.
    private static Bc1AlphaMode SourceBc1Alpha(Texture texture) =>
        IsBc1(texture.Format) ? SourceBc1Alpha(texture.Format, TextureProcessing.Decode(texture)) : Bc1AlphaMode.Opaque;

    private static Bc1AlphaMode SourceBc1Alpha(TextureFormat format, RgbaImage image) =>
        IsBc1(format) && image.HasTransparency ? Bc1AlphaMode.PunchThrough : Bc1AlphaMode.Opaque;

    private static bool IsBc1(TextureFormat format) => format is TextureFormat.BC1Unorm or TextureFormat.BC1UnormSrgb;
}
