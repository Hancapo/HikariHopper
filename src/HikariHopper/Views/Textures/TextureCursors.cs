using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HikariHopper.Views;

/// <summary>Cursors Qt draws itself on Windows and Avalonia has no standard shape for.</summary>
internal static class TextureCursors
{
    private static Cursor? closedHand;

    /// <summary>Qt <c>ClosedHandCursor</c>: Qt's 32px bitmap with the hot spot in its centre.</summary>
    public static Cursor ClosedHand => closedHand ??= Load();

    private static Cursor Load()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri("avares://HikariHopper/Assets/Cursors/closedhand32.png"));
            return new Cursor(new Bitmap(stream), new PixelPoint(16, 16));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        {
            return new Cursor(StandardCursorType.DragMove);
        }
    }
}
