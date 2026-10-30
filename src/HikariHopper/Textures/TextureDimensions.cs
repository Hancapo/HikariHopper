namespace HikariHopper.Textures;

/// <summary>Texture sizing policies shared by previews and editing actions (port of <c>texture_dimensions.py</c>).</summary>
public static class TextureDimensions
{
    /// <summary>The mip chain length for the requested dimensions.</summary>
    public static int MipCountForDimensions(int width, int height, int minMipSize) =>
        MipDimensionsForMinimum(width, height, minMipSize).Count;

    /// <summary>Builds a mip chain without letting either dimension fall below the minimum.</summary>
    public static IReadOnlyList<(int Width, int Height)> MipDimensionsForMinimum(int width, int height, int minMipSize)
    {
        int mipWidth = width, mipHeight = height;
        int minimum = Math.Max(1, minMipSize);
        if (mipWidth <= 0 || mipHeight <= 0)
            return [];
        var dimensions = new List<(int, int)> { (mipWidth, mipHeight) };
        while (true)
        {
            int nextWidth = Math.Max(1, mipWidth / 2);
            int nextHeight = Math.Max(1, mipHeight / 2);
            if (Math.Min(nextWidth, nextHeight) < minimum || (nextWidth, nextHeight) == (mipWidth, mipHeight))
                break;
            mipWidth = nextWidth;
            mipHeight = nextHeight;
            dimensions.Add((mipWidth, mipHeight));
        }
        return dimensions;
    }

    /// <summary>Translates a minimum short edge into the native mip stop value: the chain ends once the largest
    /// dimension reaches it (<c>TextureEncodingOptions.MinimumMipDimension</c>, formerly TexFury's <c>min_mip_size</c>).</summary>
    public static int MipStopSize(int width, int height, int minMipSize)
    {
        var dimensions = MipDimensionsForMinimum(width, height, minMipSize);
        if (dimensions.Count == 0)
            return Math.Max(1, minMipSize);
        (int lastWidth, int lastHeight) = dimensions[^1];
        return Math.Max(lastWidth, lastHeight);
    }

    /// <summary>True when both dimensions are powers of two.</summary>
    public static bool IsPowerOfTwo(int width, int height) =>
        width > 0 && height > 0 && (width & (width - 1)) == 0 && (height & (height - 1)) == 0;

    /// <summary>The next power of two at or above <paramref name="value"/>; 1 for values below 2.</summary>
    public static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result <<= 1;
        return result;
    }

    /// <summary>The closest power of two; ties round up.</summary>
    public static int NearestPowerOfTwo(int value)
    {
        if (value <= 1)
            return 1;
        int upper = NextPowerOfTwo(value);
        int lower = upper == value ? upper : upper >> 1;
        return upper - value <= value - lower ? upper : lower;
    }

    /// <summary>Rounds dimensions to powers of two with <c>nearest</c>, <c>down</c> or <c>up</c>.</summary>
    /// <exception cref="ArgumentException">Dimensions outside 1..65535 or an unknown mode.</exception>
    public static (int Width, int Height) PowerOfTwoDimensions(int width, int height, string mode)
    {
        if (!(width is > 0 and <= 0xFFFF && height is > 0 and <= 0xFFFF))
            throw new ArgumentException("Texture dimensions must be between 1 and 65535");
        if (mode == "nearest")
            return (NearestPowerOfTwo(width), NearestPowerOfTwo(height));
        if (mode is not ("down" or "up"))
            throw new ArgumentException($"Unknown power-of-two rounding mode: {mode}");
        (int upperWidth, int upperHeight) = (NextPowerOfTwo(width), NextPowerOfTwo(height));
        if (mode == "up")
            return (upperWidth, upperHeight);
        return (upperWidth == width ? upperWidth : upperWidth / 2, upperHeight == height ? upperHeight : upperHeight / 2);
    }
}
