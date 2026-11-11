using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HikariHopper.Tests;

/// <summary>Colours read back from what a headless window drew.</summary>
internal static class TestPixels
{
    /// <summary>The colour at (<paramref name="x"/>, <paramref name="y"/>) of a captured frame.</summary>
    internal static Color At(WriteableBitmap frame, int x, int y)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    /// <summary>The colour at (<paramref name="x"/>, <paramref name="y"/>) of what <paramref name="window"/> draws now.</summary>
    internal static Color At(Window window, int x, int y)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        return At(frame, x, y);
    }
}
