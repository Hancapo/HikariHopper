using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Infrastructure;
using SkiaSharp;

namespace HikariHopper.Textures;

/// <summary>Image decoding policy shared by texture import, paste and the replacement picker (port of <c>texture_import.py</c>).</summary>
public static class TextureImport
{
    public static IReadOnlySet<string> ImageSuffixes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ".dds", ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp",
    };

    /// <summary>Python <c>IMAGE_FILTER</c>: <c>Texture images (*.dds *.png *.jpg *.jpeg *.bmp *.tga *.webp)</c>.</summary>
    public static FileFilter ImageFileFilter { get; } = new("Texture images",
        ["*.dds", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga", "*.webp"]);

    public static bool IsImagePath(string path) => ImageSuffixes.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads a dropped image as a new texture named after its lower-cased stem.</summary>
    public static Texture ReadTextureImage(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        // Inspect source pixels before lossy compression or resampling; an alpha channel alone does not imply
        // transparency. Keep this stage lossless.
        RgbaImage source = string.Equals(Path.GetExtension(path), ".dds", StringComparison.OrdinalIgnoreCase)
            ? TextureProcessing.Decode(Texture.FromSurface(DdsCodec.Read(path), name))
            : ReadImageFile(path);
        return NormalizeTextureImage(source, name);
    }

    /// <summary>Converts a clipboard image into a new texture named <paramref name="name"/>.</summary>
    public static Texture TextureFromClipboardImage(RgbaImage? image, string name)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0)
            throw new ArgumentException("Clipboard image is empty");
        return NormalizeTextureImage(image, name);
    }

    /// <summary>BC3 when any pixel is translucent, otherwise BC1; nearest power-of-two size of at least 4×4, and a mip
    /// chain that stops at a short edge of 4. Every encoding stage uses maximum quality.</summary>
    public static Texture NormalizeTextureImage(RgbaImage source, string name)
    {
        TextureFormat target = source.HasTransparency ? TextureFormat.BC3Unorm : TextureFormat.BC1Unorm;
        (int width, int height) = TextureDimensions.PowerOfTwoDimensions(source.Width, source.Height, "nearest");
        (width, height) = (Math.Max(4, width), Math.Max(4, height));
        RgbaImage image = source.Resize(width, height, new ImageResizeOptions(ImageFilter.Mitchell, ImageColorSpace.Linear));
        var options = new TextureEncodingOptions
        {
            Format = target,
            Quality = 1,
            GenerateMipmaps = true,
            MinimumMipDimension = TextureDimensions.MipStopSize(width, height, 4),
            MipFilter = ImageFilter.Mitchell,
        };
        return TextureProcessing.Encode(image, options, name);
    }

    /// <summary>Decodes PNG, JPEG, BMP and TGA with FiveFury. WebP, which FiveFury does not read, is decoded by Skia
    /// (already shipped with Avalonia) without alpha premultiplication or color conversion.</summary>
    public static RgbaImage ReadImageFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
            return RgbaImage.Read(path);
        using SKCodec codec = SKCodec.Create(path) ?? throw new InvalidDataException("Unsupported or corrupt WebP image");
        return ReadWebP(codec);
    }

    /// <summary><see cref="ReadImageFile"/> for an image already in memory, such as an entry in an archive;
    /// <paramref name="name"/> says which kind it is.</summary>
    public static RgbaImage ReadImageBytes(byte[] data, string name)
    {
        if (!string.Equals(Path.GetExtension(name), ".webp", StringComparison.OrdinalIgnoreCase))
            return RgbaImage.Read(data);
        using SKData encoded = SKData.CreateCopy(data);
        using SKCodec codec = SKCodec.Create(encoded) ?? throw new InvalidDataException("Unsupported or corrupt WebP image");
        return ReadWebP(codec);
    }

    private static RgbaImage ReadWebP(SKCodec codec)
    {
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        SKCodecResult result = codec.GetPixels(info, out byte[] pixels);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException($"Could not decode WebP image: {result}");
        return new RgbaImage(info.Width, info.Height, pixels);
    }
}
