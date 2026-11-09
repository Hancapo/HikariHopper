using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Tooltips shown as windows of their own, borderless, unactivated and owned by the main window. The app's popups are
/// drawn inside their window, under any native child window there (the 3D viewport); where this is set (it's inherited),
/// a text tooltip is drawn over it instead, in the same look, when the tooltip service would open it.
/// </summary>
public static class WindowToolTip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(WindowToolTip), false, inherits: true);

    // The pointer's offset to the tooltip's corner, as the in-window tooltip's, in device-independent pixels.
    private const double PointerGap = 20;

    private static Window? shown;

    static WindowToolTip() => ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>(OnOpening);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    /// <summary>Whether a tooltip is shown as a window now, and what it says.</summary>
    internal static string? ShownText => (shown?.Content as Border)?.Child is TextBlock text ? text.Text : null;

    /// <summary>What a tooltip says, when it's text (a string, or a tooltip or text block of one); null otherwise.</summary>
    internal static string? TextOf(object? tip) => tip switch
    {
        string text => text,
        TextBlock block => block.Text,
        ToolTip { Content: string text } => text,
        ToolTip { Content: TextBlock block } => block.Text,
        _ => null,
    };

    private static void OnOpening(Control control, CancelRoutedEventArgs e)
    {
        if (!GetIsEnabled(control) || TextOf(ToolTip.GetTip(control)) is not { Length: > 0 } text
            || TopLevel.GetTopLevel(control) is not Window owner)
            return;
        // A control in a window over the main one (the viewport's tools) has its tooltip over the main window too.
        while (owner.Owner is Window above)
            owner = above;
        e.Cancel = true;
        Show(control, owner, text);
    }

    /// <summary>Shows <paramref name="text"/> by the pointer, over <paramref name="control"/>'s window, until the pointer leaves it.</summary>
    internal static void Show(Control control, Window owner, string text)
    {
        Hide();
        var body = new Border
        {
            Child = new TextBlock
            {
                Text = text,
                Foreground = Ui.Theme.Text,
                FontFamily = Ui.Theme.UiFont,
                FontSize = Ui.Theme.FontSize,
                TextWrapping = TextWrapping.Wrap,
            },
            Background = Ui.Theme.ChromeRaised,
            BorderBrush = Ui.Theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = Ui.Theme.ControlCorner,
            Padding = new Thickness(8, 5),
            MaxWidth = 360,
            IsHitTestVisible = false,
        };
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = body,
        };
        // Placed before it shows, from the size its text asks for: below and right of the pointer, above it where the
        // window or the screen ends, and kept inside both, as the in-window tooltips are.
        body.Measure(new Size(body.MaxWidth, double.PositiveInfinity));
        double scale = owner.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(body.DesiredSize.Width * scale), (int)Math.Ceiling(body.DesiredSize.Height * scale));
        PixelPoint pointer = Pointer(control);
        int gap = (int)Math.Round(PointerGap * scale);
        var position = new PixelPoint(pointer.X, pointer.Y + gap);
        var frame = new PixelRect(owner.Position, PixelSize.FromSize(owner.ClientSize, scale));
        if (owner.Screens.ScreenFromPoint(pointer)?.WorkingArea is { } screen)
        {
            PixelRect area = frame.Intersects(screen) ? frame.Intersect(screen) : screen;
            if (position.Y + size.Height > area.Bottom)
                position = position.WithY(pointer.Y - size.Height);
            position = new PixelPoint(Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - size.Width)),
                Math.Max(area.Y, position.Y));
        }
        window.Position = position;

        void Close(object? sender, EventArgs e) => Hide(window);
        control.PointerExited += Close;
        control.AddHandler(InputElement.PointerPressedEvent, Close, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.DetachedFromVisualTree += Close;
        control.PropertyChanged += TipChanged;
        owner.Deactivated += Close;
        window.Closed += (_, _) =>
        {
            control.PointerExited -= Close;
            control.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Close);
            control.DetachedFromVisualTree -= Close;
            control.PropertyChanged -= TipChanged;
            owner.Deactivated -= Close;
            if (shown == window)
                shown = null;
        };
        shown = window;
        window.Show(owner);

        void TipChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ToolTip.TipProperty || e.Property == Visual.IsVisibleProperty)
                Hide(window);
        }
    }

    /// <summary>Closes the tooltip shown as a window, if there is one.</summary>
    internal static void Hide() => Hide(shown);

    private static void Hide(Window? window)
    {
        if (window is null)
            return;
        if (shown == window)
            shown = null;
        if (window.IsVisible)
            window.Close();
    }

    /// <summary>The pointer on the screen, in physical pixels; the control's middle where it can't be read.</summary>
    private static PixelPoint Pointer(Control control)
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var point))
            return new PixelPoint(point.X, point.Y);
        return control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
