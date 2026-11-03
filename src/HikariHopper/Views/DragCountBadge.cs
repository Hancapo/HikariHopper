using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>
/// A small label that follows the cursor while entries are dragged out of the explorer, saying how many folders and files
/// are being extracted. Windows runs the drag itself, so this is a borderless window of its own rather than something
/// drawn under the cursor. It sits below and to the right of the pointer, so it never covers the drop target, and never
/// takes focus.
/// </summary>
internal sealed class DragCountBadge : Window
{
    // Below and to the right of the pointer, clear of Windows' own drag cursor.
    private const double OffsetX = 18, OffsetY = 22;
    private const int WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExNoActivate = 0x08000000;

    private readonly DispatcherTimer follow;

    public DragCountBadge(string text)
    {
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = Label(text);
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWindowStylesCallback(this, (style, exStyle) =>
                (style, exStyle | WsExToolWindow | WsExTopmost | WsExNoActivate));
        follow = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Follow());
    }

    /// <summary>"2 folders, 3 files"-style text for what a drag extracts.</summary>
    public static string Describe(int folders, int files)
    {
        var parts = new List<string>();
        if (folders > 0)
            parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
        if (files > 0)
            parts.Add(files == 1 ? "1 file" : $"{files} files");
        return $"Extracting {string.Join(", ", parts)}";
    }

    /// <summary>The badge's content: a rounded chrome label.</summary>
    public static Control Label(string text) => new Border
    {
        Background = Ui.Theme.ChromeRaised,
        BorderBrush = Ui.Theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = Ui.Theme.ItemCorner,
        Padding = new Thickness(8, 4),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.FontSize,
            Foreground = Ui.Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Shows the badge by the cursor and keeps it there until it is closed.</summary>
    public void ShowAtCursor()
    {
        Follow();
        Show();
        follow.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        follow.Stop();
        base.OnClosed(e);
    }

    private void Follow()
    {
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out CursorPoint point))
            return;
        double scale = DesktopScaling;
        Position = new PixelPoint(point.X + (int)Math.Round(OffsetX * scale), point.Y + (int)Math.Round(OffsetY * scale));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
