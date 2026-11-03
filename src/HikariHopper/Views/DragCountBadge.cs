using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A small badge that follows the cursor while entries are dragged out of the explorer: a folder, file or stack icon for
/// what is being extracted, with the number of items in a circle at its bottom-right. Windows runs the drag itself, so this
/// is a borderless window of its own rather than something drawn under the cursor. It sits below and to the right of the
/// pointer, so it never covers the drop target, and never takes focus.
/// </summary>
internal sealed class DragCountBadge : Window
{
    // Below and to the right of the pointer, clear of Windows' own drag cursor.
    private const double OffsetX = 18, OffsetY = 22;
    private const int WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExNoActivate = 0x08000000;

    private readonly DispatcherTimer follow;

    public DragCountBadge(int folders, int files)
    {
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = Badge(folders, files);
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWindowStylesCallback(this, (style, exStyle) =>
                (style, exStyle | WsExToolWindow | WsExTopmost | WsExNoActivate));
        follow = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Follow());
    }

    /// <summary>"Extracting 2 folders, 3 files": what a drag extracts, as the badge's accessible name.</summary>
    public static string Describe(int folders, int files)
    {
        var parts = new List<string>();
        if (folders > 0)
            parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
        if (files > 0)
            parts.Add(files == 1 ? "1 file" : $"{files} files");
        return $"Extracting {string.Join(", ", parts)}";
    }

    /// <summary>The icon for a drag: a folder or a file when they are all alike, a stack of files when mixed.</summary>
    public static string IconFor(int folders, int files) => files == 0 ? "folder" : folders == 0 ? "file" : "file-stack";

    /// <summary>The badge's content: the icon on a rounded chrome tile, with the item count in a circle at its bottom-right.</summary>
    public static Control Badge(int folders, int files)
    {
        var badge = new Panel
        {
            Width = 44,
            Height = 42,
            Children =
            {
                new Border
                {
                    Width = 36,
                    Height = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = Ui.Theme.ChromeRaised,
                    BorderBrush = Ui.Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Ui.Theme.ControlCorner,
                    Child = new LucideIcon
                    {
                        IconName = IconFor(folders, files),
                        Stroke = Ui.Theme.Text,
                        Width = 20,
                        Height = 20,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
                // A circle for one or two digits, a pill for more.
                new Border
                {
                    MinWidth = 18,
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(5, 0),
                    Background = Ui.Theme.Selection,
                    BorderBrush = Ui.Theme.WindowBg,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(9),
                    Child = new TextBlock
                    {
                        Text = (folders + files).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        FontFamily = Ui.Theme.UiFont,
                        FontSize = Ui.Theme.SmallFontSize,
                        FontWeight = FontWeight.Bold,
                        Foreground = Ui.Theme.SelectionText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(badge, Describe(folders, files));
        return badge;
    }

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
