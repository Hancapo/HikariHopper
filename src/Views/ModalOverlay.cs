using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// A Qt Quick Controls modal <c>Dialog</c> parented to <c>Overlay.overlay</c>: the dialog sits centred
/// (<c>Math.round((parent.width - width) / 2)</c>) over a full-window <c>Overlay.modal</c> dim that
/// swallows pointer input, takes keyboard focus while open, closes on Escape
/// (<c>closePolicy: Popup.CloseOnEscape</c>) and gives focus back to whatever had it before.
/// </summary>
public sealed class ModalOverlay : Panel
{
    private static readonly ConditionalWeakTable<TopLevel, List<ModalOverlay>> OpenOverlays = new();

    private readonly Control dialog;
    private readonly TopLevel host;
    private readonly IInputElement? previousFocus;
    private OverlayLayer? layer;

    private ModalOverlay(TopLevel host, Control dialog, IBrush dim)
    {
        this.host = host;
        this.dialog = dialog;
        previousFocus = host.FocusManager?.GetFocusedElement();
        Background = dim;
        Focusable = true;
        dialog.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        dialog.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Children.Add(dialog);
    }

    /// <summary>Raised once the overlay is gone (QML <c>closed</c>).</summary>
    public event EventHandler? Closed;

    public bool IsShown { get; private set; }

    /// <summary>Whether a modal overlay currently blocks <paramref name="host"/>.</summary>
    public static bool IsOpen(TopLevel host) => OpenOverlays.TryGetValue(host, out var list) && list.Count > 0;

    /// <summary>Opens <paramref name="dialog"/> over the window that contains <paramref name="anchor"/>.</summary>
    public static ModalOverlay? Show(Control anchor, Control dialog)
    {
        if (TopLevel.GetTopLevel(anchor) is not { } host || OverlayLayer.GetOverlayLayer(anchor) is not { } layer)
            return null;
        var overlay = new ModalOverlay(host, dialog, Ui.Theme.MarqueeFill) { layer = layer };
        layer.Children.Add(overlay);
        overlay.IsShown = true;
        OpenOverlays.GetOrCreateValue(host).Add(overlay);
        overlay.Focus();
        return overlay;
    }

    public void Close()
    {
        if (!IsShown)
            return;
        IsShown = false;
        if (OpenOverlays.TryGetValue(host, out var list))
            list.Remove(this);
        layer?.Children.Remove(this);
        if (previousFocus is InputElement element && TopLevel.GetTopLevel(element) == host)
            element.Focus();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        dialog.Measure(Size.Infinity);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = dialog.Width;
        double height = dialog.Height;
        double x = Math.Floor((finalSize.Width - width) / 2 + 0.5);
        double y = Math.Floor((finalSize.Height - height) / 2 + 0.5);
        dialog.Arrange(new Rect(x, y, width, height));
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        if (e.Key != Key.Escape)
            return;
        Close();
        e.Handled = true;
    }
}
