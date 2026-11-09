using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FiveFury.Textures;

namespace HikariHopper.ViewModels;

/// <summary>Converts decoded textures into Avalonia bitmaps (replaces Python's <c>TextureImageProvider</c>).</summary>
internal static class TextureBitmaps
{
    public const int ThumbnailSize = 96;

    /// <summary>
    /// A bitmap of <paramref name="image"/>, its RGBA as it is (unpremultiplied): a copy of its rows, with nothing
    /// worked out per pixel, so a large texture shows at once. Safe to call off the UI thread.
    /// </summary>
    public static unsafe WriteableBitmap Create(RgbaImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        ReadOnlySpan<byte> pixels = image.Pixels;
        int rowBytes = image.Width * 4;
        if (buffer.RowBytes == rowBytes)
            pixels[..(rowBytes * image.Height)].CopyTo(new Span<byte>((void*)buffer.Address, rowBytes * image.Height));
        else
            for (int y = 0; y < image.Height; y++)
                pixels.Slice(y * rowBytes, rowBytes).CopyTo(new Span<byte>((void*)(buffer.Address + y * buffer.RowBytes), rowBytes));
        return bitmap;
    }

    /// <summary>Scales to fit 96×96 keeping the aspect ratio, like Qt's <c>KeepAspectRatio</c> smooth scaling.</summary>
    public static RgbaImage Thumbnail(RgbaImage image)
    {
        (int width, int height) = ThumbnailDimensions(image.Width, image.Height);
        return image.Resize(width, height, new ImageResizeOptions(ImageFilter.Triangle, ImageColorSpace.Linear));
    }

    /// <summary>The size a thumbnail of an image this size has: fitted to 96×96, keeping its aspect ratio.</summary>
    public static (int Width, int Height) ThumbnailDimensions(int width, int height)
    {
        long scaledWidth = (long)ThumbnailSize * width / height;
        (long thumbnailWidth, long thumbnailHeight) = scaledWidth <= ThumbnailSize
            ? (scaledWidth, (long)ThumbnailSize)
            : ((long)ThumbnailSize, (long)ThumbnailSize * height / width);
        return ((int)Math.Max(1, thumbnailWidth), (int)Math.Max(1, thumbnailHeight));
    }

    /// <summary>
    /// The smallest mip of a texture this size that still covers its thumbnail: what a thumbnail is made from,
    /// rather than decoding the full texture to shrink it.
    /// </summary>
    public static int ThumbnailMip(int width, int height, int mipCount)
    {
        (int thumbnailWidth, int thumbnailHeight) = ThumbnailDimensions(width, height);
        int mip = 0;
        while (mip + 1 < mipCount && Math.Max(1, width >> (mip + 1)) >= thumbnailWidth && Math.Max(1, height >> (mip + 1)) >= thumbnailHeight)
            mip++;
        return mip;
    }

    /// <summary>Disposes a replaced bitmap after bindings have moved to its successor.</summary>
    public static void DisposeLater(Bitmap? bitmap)
    {
        if (bitmap is not null)
            Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
    }
}
