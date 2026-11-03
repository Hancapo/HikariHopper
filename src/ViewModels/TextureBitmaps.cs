using System.Runtime.InteropServices;
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

    /// <summary>A premultiplied BGRA bitmap of <paramref name="image"/>. Safe to call off the UI thread.</summary>
    public static WriteableBitmap Create(RgbaImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        ReadOnlySpan<byte> pixels = image.Pixels;
        var row = new byte[image.Width * 4];
        for (int y = 0; y < image.Height; y++)
        {
            ReadOnlySpan<byte> source = pixels.Slice(y * row.Length, row.Length);
            for (int x = 0; x < row.Length; x += 4)
            {
                int alpha = source[x + 3];
                row[x] = Premultiply(source[x + 2], alpha);
                row[x + 1] = Premultiply(source[x + 1], alpha);
                row[x + 2] = Premultiply(source[x], alpha);
                row[x + 3] = (byte)alpha;
            }
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        }
        return bitmap;
    }

    /// <summary>Scales to fit 96×96 keeping the aspect ratio, like Qt's <c>KeepAspectRatio</c> smooth scaling.</summary>
    public static RgbaImage Thumbnail(RgbaImage image)
    {
        long scaledWidth = (long)ThumbnailSize * image.Width / image.Height;
        (int width, int height) = scaledWidth <= ThumbnailSize
            ? ((int)scaledWidth, ThumbnailSize)
            : (ThumbnailSize, (int)((long)ThumbnailSize * image.Height / image.Width));
        return image.Resize(Math.Max(1, width), Math.Max(1, height), new ImageResizeOptions(ImageFilter.Triangle, ImageColorSpace.Linear));
    }

    /// <summary>Disposes a replaced bitmap after bindings have moved to its successor.</summary>
    public static void DisposeLater(Bitmap? bitmap)
    {
        if (bitmap is not null)
            Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
    }

    private static byte Premultiply(byte value, int alpha) => (byte)((value * alpha + 127) / 255);
}
