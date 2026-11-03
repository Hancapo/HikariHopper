using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The cursor while entries are dragged out of the explorer: a pointer arrow with a badge below and to the right of it, a
/// folder, file or stack icon for what is being extracted with the number of items in a circle at its bottom-right. It is
/// the cursor itself, which Windows draws and moves with the mouse (see <see cref="Infrastructure.ShellFileDrag"/>); the
/// arrow keeps the exact drop point visible.
/// </summary>
internal static class DragCountBadge
{
    // Where the badge sits relative to the arrow's tip, the cursor's hot spot.
    private const double BadgeLeft = 12, BadgeTop = 16;

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

    /// <summary>
    /// The badge's content: the icon on a rounded chrome tile, with the item count in a circle at its bottom-right. Where
    /// nothing can be dropped (<paramref name="blocked"/>) the count turns neutral and the icon dim, the badge staying opaque
    /// so it reads on any background.
    /// </summary>
    public static Control Badge(int folders, int files, bool blocked = false)
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
                        Stroke = blocked ? Ui.Theme.TextDim : Ui.Theme.Text,
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
                    Background = blocked ? Ui.Theme.Border : Ui.Theme.Selection,
                    BorderBrush = Ui.Theme.WindowBg,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(9),
                    Child = new TextBlock
                    {
                        Text = (folders + files).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        FontFamily = Ui.Theme.UiFont,
                        FontSize = Ui.Theme.SmallFontSize,
                        FontWeight = FontWeight.Bold,
                        Foreground = blocked ? Ui.Theme.Text : Ui.Theme.SelectionText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(badge, Describe(folders, files));
        return badge;
    }

    /// <summary>
    /// The drag cursor's visual: the pointer arrow with its tip at the top-left corner and the badge beside it, in its
    /// blocked look where nothing can be dropped.
    /// </summary>
    public static Control CursorVisual(int folders, int files, bool blocked = false)
    {
        Control badge = Badge(folders, files, blocked);
        Canvas.SetLeft(badge, BadgeLeft);
        Canvas.SetTop(badge, BadgeTop);
        var cursor = new Canvas
        {
            Width = BadgeLeft + badge.Width,
            Height = BadgeTop + badge.Height,
            Children =
            {
                badge,
                new Avalonia.Controls.Shapes.Path
                {
                    // The classic pointer, tip at (0.5, 0.5) so its 1px outline lands on whole pixels.
                    Data = Geometry.Parse("M0.5,0.5 L0.5,16.5 L4.5,12.5 L7.5,18.5 L9.7,17.5 L6.8,11.5 L12,11.5 Z"),
                    Fill = Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                },
            },
        };
        // Sub-pixel (ClearType) text needs an opaque background; on a transparent cursor it smears into colour fringes.
        TextOptions.SetTextRenderingMode(cursor, TextRenderingMode.Antialias);
        return cursor;
    }

    /// <summary>
    /// <paramref name="visual"/> rendered at <paramref name="scale"/> as top-down, non-premultiplied BGRA pixels, the form
    /// a Windows cursor takes.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Render(Control visual, double scale)
    {
        visual.Measure(Size.Infinity);
        visual.Arrange(new Rect(visual.DesiredSize));
        int width = Math.Max(1, (int)Math.Ceiling(visual.DesiredSize.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(visual.DesiredSize.Height * scale));
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96 * scale, 96 * scale));
        target.Render(visual);
        var pixels = new byte[width * height * 4];
        GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }
        // Skia renders premultiplied alpha; cursors want it straight.
        for (int index = 0; index < pixels.Length; index += 4)
        {
            byte alpha = pixels[index + 3];
            if (alpha is 0 or 255)
                continue;
            for (int channel = 0; channel < 3; channel++)
                pixels[index + channel] = (byte)Math.Min(255, pixels[index + channel] * 255 / alpha);
        }
        return (pixels, width, height);
    }
}
