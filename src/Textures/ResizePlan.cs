using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.Textures;

/// <summary>What a resize's numbers mean: a size in pixels, or a share of each texture's own size.</summary>
public enum ResizeUnit
{
    Pixels,
    Percent,
}

/// <summary>
/// What a resize asks for: a width and height in pixels, the same for every texture, or in percent, which scales
/// each texture from its own size; then, with <paramref name="PowerOfTwo"/>, each dimension rounded to the nearest
/// power of two.
/// </summary>
public readonly record struct ResizePlan(ResizeUnit Unit, int Width, int Height, bool PowerOfTwo = false)
{
    /// <summary>The size a texture of this size comes out at.</summary>
    public (int Width, int Height) Target(int width, int height)
    {
        (int targetWidth, int targetHeight) = Unit == ResizeUnit.Pixels
            ? (Width, Height)
            : (Scaled(width, Width), Scaled(height, Height));
        return PowerOfTwo && targetWidth > 0 && targetHeight > 0
            ? (TextureDimensions.NearestPowerOfTwo(targetWidth), TextureDimensions.NearestPowerOfTwo(targetHeight))
            : (targetWidth, targetHeight);
    }

    /// <summary>Whether a texture of this size is left as it is: the plan gives it the size it has.</summary>
    public bool Keeps(int width, int height) => Target(width, height) == (width, height);

    /// <summary>A size at a percentage of itself, rounded, and never under one pixel; 0 for no percentage.</summary>
    public static int Scaled(int size, int percent) => percent <= 0 || size <= 0
        ? 0
        : (int)Math.Clamp(Math.Round((double)size * percent / 100, MidpointRounding.AwayFromZero), 1, int.MaxValue);

    /// <summary>What percentage a size is of the original, rounded, and never under one; 100 with no original.</summary>
    public static int PercentOf(int size, int original) => original <= 0
        ? 100
        : (int)Math.Clamp(Math.Round(size * 100.0 / original, MidpointRounding.AwayFromZero), 1, int.MaxValue);

    /// <summary>
    /// How many bytes a texture of this size and format takes with this many mips; -1 for a format or size that has
    /// no tight layout.
    /// </summary>
    public static long EstimatedSize(int width, int height, TextureFormat format, int mipCount)
    {
        try
        {
            return width > 0 && height > 0 ? TextureLayout.DataSize(width, height, format, Math.Max(1, mipCount)) : -1;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>Whether a format stores its pixels in 4 × 4 blocks, as the DXT and BC formats do.</summary>
    public static bool InBlocks(TextureFormat format)
    {
        try
        {
            return TextureFormats.TryUnit(format, out _, out int dimension) && dimension == 4;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
