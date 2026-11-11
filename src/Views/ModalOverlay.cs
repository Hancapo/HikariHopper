using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HikariHopper.Views;

/// <summary>
/// Content of a <see cref="ModalOverlay"/> that decides for itself how it closes, so Escape can ask
/// it first (a document with unsaved changes, say) instead of the overlay simply going away.
/// </summary>
public interface IModalContent
{
    /// <summary>Asked when Escape is pressed over the modal.</summary>
    void RequestClose();
}

/// <summary>
/// An in-window modal: the dialog sits centred over a dim that swallows pointer input, takes keyboard
/// focus while open, closes on Escape and gives focus back to whatever had it before.
/// </summary>
/// <remarks>
/// The window marks its content with <see cref="IsBackdropProperty"/>. While a modal is open that
/// content blurs and the dim covers only it, so the title bar above stays live: the window can still
/// be moved, minimised or closed (its menus are switched off, see <see cref="StateChanged"/>). The dim
/// fades in and the dialog rises into place. On close it goes as a macOS window does: a picture of it
/// shrinks toward its centre and fades, with the dim, while the blur eases away.
/// </remarks>
public sealed class ModalOverlay : Panel
{
    /// <summary>Marks the part of a window a modal blurs and covers; without one it covers the whole window.</summary>
    public static readonly AttachedProperty<bool> IsBackdropProperty =
        AvaloniaProperty.RegisterAttached<ModalOverlay, Control, bool>("IsBackdrop");

    /// <summary>
    /// Makes a dialog fill the window, <see cref="Inset"/> clear of each edge, instead of keeping its
    /// own size: for a whole tool such as the texture editor.
    /// </summary>
    public static readonly AttachedProperty<bool> FillsWindowProperty =
        AvaloniaProperty.RegisterAttached<ModalOverlay, Control, bool>("FillsWindow");

    /// <summary>How soft the content behind a modal gets.</summary>
    public const double BlurRadius = 14;

    /// <summary>How long an opening dialog takes to rise into place, and the content behind it to blur.</summary>
    internal static TimeSpan OpenDuration { get; set; } = TimeSpan.FromMilliseconds(180);

    /// <summary>How long a closing dialog takes to shrink away, as a macOS window or sheet does.</summary>
    internal static TimeSpan CloseDuration { get; set; } = TimeSpan.FromMilliseconds(220);

    /// <summary>How small a closing dialog gets as it fades.</summary>
    private const double CloseScale = 0.92;
    private static readonly ConditionalWeakTable<TopLevel, List<ModalOverlay>> OpenOverlays = new();

    private readonly Control dialog;
    private readonly TopLevel host;
    private readonly IInputElement? previousFocus;
    private readonly Control? backdrop;
    private OverlayLayer? layer;
    // While closing: the dialog as it last looked, which shrinks and fades in its place.
    private Image? ghost;
    private Rect ghostRect;

    private ModalOverlay(TopLevel host, Control dialog, Control? backdrop)
    {
        this.host = host;
        this.dialog = dialog;
        this.backdrop = backdrop;
        previousFocus = host.FocusManager?.GetFocusedElement();
        Background = Ui.Theme.ModalDim;
        Focusable = true;
        // Tab and Shift+Tab wrap around inside the modal instead of reaching the window behind it.
        // Bubbling, so a dialog inside the modal with a tab order of its own handles Tab first; this
        // still runs before the window's own navigation, which would leave the modal.
        AddHandler(KeyDownEvent, OnTabKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
        dialog.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;  // it fills the slot SizeFor gives it
        dialog.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Children.Add(dialog);
        Transitions = [Fade(OpacityProperty)];
        dialog.Transitions =
        [
            Fade(OpacityProperty),
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = OpenDuration, Easing = new CubicEaseOut() },
        ];
    }

    /// <summary>Raised once the overlay is gone (QML <c>closed</c>).</summary>
    public event EventHandler? Closed;

    /// <summary>Raised whenever a window gains its first modal or loses its last one.</summary>
    public static event EventHandler<TopLevel>? StateChanged;

    /// <summary>An overlay opened or closed over a window, whichever it is: which one is on top may have changed.</summary>
    public static event EventHandler<TopLevel>? StackChanged;

    public bool IsShown { get; private set; }

    /// <summary>The dialog this overlay shows.</summary>
    public Control Dialog => dialog;

    public static bool GetFillsWindow(Control control) => control.GetValue(FillsWindowProperty);

    public static void SetFillsWindow(Control control, bool value) => control.SetValue(FillsWindowProperty, value);

    public static bool GetIsBackdrop(Control control) => control.GetValue(IsBackdropProperty);

    public static void SetIsBackdrop(Control control, bool value) => control.SetValue(IsBackdropProperty, value);

    /// <summary>Whether a modal overlay currently blocks <paramref name="host"/>.</summary>
    public static bool IsOpen(TopLevel host) => OpenOverlays.TryGetValue(host, out var list) && list.Count > 0;

    /// <summary>The window content a modal blurs: the control marked <see cref="IsBackdropProperty"/>.</summary>
    public static Control? BackdropOf(TopLevel host) =>
        host.GetVisualDescendants().OfType<Control>().FirstOrDefault(GetIsBackdrop);

    /// <summary>Opens <paramref name="dialog"/> over the window that contains <paramref name="anchor"/>.</summary>
    public static ModalOverlay? Show(Control anchor, Control dialog)
    {
        if (TopLevel.GetTopLevel(anchor) is not { } host || OverlayLayer.GetOverlayLayer(anchor) is not { } layer)
            return null;
        Control? backdrop = BackdropOf(host);
        // Shown again while its last overlay is still fading out: take it back from that one now.
        (dialog.Parent as ModalOverlay)?.Detach();
        var overlay = new ModalOverlay(host, dialog, backdrop) { layer = layer };
        // Cover the backdrop only, so whatever sits above it (the title bar) keeps working.
        if (backdrop?.TranslatePoint(default, layer) is { } top)
            overlay.Margin = new Thickness(0, Math.Max(0, top.Y), 0, 0);
        overlay.Opacity = 0;
        dialog.Opacity = 0;
        dialog.RenderTransform = Lift(10, 0.97);
        layer.Children.Add(overlay);
        overlay.IsShown = true;
        List<ModalOverlay> open = OpenOverlays.GetOrCreateValue(host);
        open.Add(overlay);
        // In the next frame, so the transitions run from the starting values.
        Dispatcher.UIThread.Post(() =>
        {
            if (!overlay.IsShown)
                return;
            overlay.Opacity = 1;
            dialog.Opacity = 1;
            dialog.RenderTransform = Lift(0, 1);
        }, DispatcherPriority.Render);
        if (open.Count == 1)
        {
            Blur(backdrop, true);
            StateChanged?.Invoke(null, host);
        }
        StackChanged?.Invoke(null, host);
        // The title bar stays usable, so focus can land there (a click on Maximize); the keys still belong
        // to the modal then.
        host.AddHandler(KeyDownEvent, overlay.OnHostKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        overlay.Focus();
        return overlay;
    }

    public void Close()
    {
        if (!IsShown)
            return;
        IsShown = false;
        host.RemoveHandler(KeyDownEvent, OnHostKeyDown);
        bool last = false;
        if (OpenOverlays.TryGetValue(host, out var list))
        {
            list.Remove(this);
            last = list.Count == 0;
        }
        // Stop taking input now. The dialog leaves at once, so what closes it can let go of what it shows; a picture
        // of it shrinks away toward its centre and fades, with the dim, and the overlay leaves once that is done.
        IsHitTestVisible = false;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = CloseDuration, Easing = new QuadraticEaseIn() }];
        LeaveAsPicture();
        Opacity = 0;
        DispatcherTimer.RunOnce(Detach, CloseDuration);
        if (last)
        {
            Blur(backdrop, false);
            StateChanged?.Invoke(null, host);
        }
        StackChanged?.Invoke(null, host);
        if (previousFocus is InputElement element && TopLevel.GetTopLevel(element) == host)
            element.Focus();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves focus to the dialog's next (or, with Shift, previous) tab stop, wrapping at either end, so
    /// keyboard focus never leaves the modal for the window behind it.
    /// </summary>
    private void OnTabKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
            return;
        e.Handled = true;
        MoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>
    /// The keys pressed while focus is outside the modal, on what it leaves usable (the title bar): Tab
    /// comes back into the dialog and Escape closes it, as they would inside it. Only the topmost modal
    /// takes them, and never from a popup (a combo box's list).
    /// </summary>
    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Tab or Key.Escape) || !IsShown || !IsTopmost)
            return;
        if (host.FocusManager?.GetFocusedElement() is Visual focused
            && (focused == this || this.IsVisualAncestorOf(focused) || focused.GetVisualAncestors().Any(ancestor => ancestor is PopupRoot or OverlayPopupHost)))
            return;
        if (e.Key == Key.Tab)
        {
            if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
                return;
            MoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else
        {
            Dismiss();
        }
        e.Handled = true;
    }

    /// <summary>Whether this is the modal on top, the one the keys are for.</summary>
    /// <summary>Whether it is the top one of its window's open overlays.</summary>
    public bool IsTopmost => OpenOverlays.TryGetValue(host, out var list) && list.Count > 0 && list[^1] == this;

    private void MoveFocus(bool back)
    {
        List<InputElement> stops = [.. dialog.GetVisualDescendants().OfType<InputElement>()
            .Where(element => element.Focusable && element.IsTabStop && element.IsEffectivelyEnabled && element.IsEffectivelyVisible)];
        if (stops.Count == 0)
            return;
        int current = host.FocusManager?.GetFocusedElement() is InputElement focused ? stops.IndexOf(focused) : -1;
        int next = current < 0
            ? (back ? stops.Count - 1 : 0)
            : (current + (back ? -1 : 1) + stops.Count) % stops.Count;
        stops[next].Focus(NavigationMethod.Tab);
    }

    /// <summary>Puts a picture of the dialog where it is, and lets the dialog go; the picture then shrinks and fades.</summary>
    private void LeaveAsPicture()
    {
        Rect bounds = dialog.Bounds;
        double scaling = host.RenderScaling;
        if (bounds.Width >= 1 && bounds.Height >= 1 && dialog.Parent == this)
        {
            var picture = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(bounds.Width * scaling), (int)Math.Ceiling(bounds.Height * scaling)),
                new Vector(96 * scaling, 96 * scaling));
            picture.Render(dialog);
            ghost = new Image { Source = picture, Stretch = Stretch.Fill, Opacity = dialog.Opacity, RenderTransform = dialog.RenderTransform };
            ghostRect = bounds;
            ghost.Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = CloseDuration, Easing = new QuadraticEaseIn() },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = CloseDuration, Easing = new CubicEaseOut() },
            ];
            Children.Add(ghost);
            Image shrinking = ghost;
            Dispatcher.UIThread.Post(() =>
            {
                shrinking.Opacity = 0;
                shrinking.RenderTransform = Lift(0, CloseScale);
            }, DispatcherPriority.Render);
        }
        Children.Remove(dialog);
    }

    /// <summary>Leaves the window and lets go of the dialog (and its picture), so it can be shown again.</summary>
    private void Detach()
    {
        if (dialog.Parent == this)
            Children.Remove(dialog);
        if (ghost is not null)
        {
            Children.Remove(ghost);
            IImage? picture = ghost.Source;
            ghost.Source = null;
            (picture as IDisposable)?.Dispose();
            ghost = null;
        }
        layer?.Children.Remove(this);
        layer = null;
    }

    private static DoubleTransition Fade(AvaloniaProperty property) =>
        new() { Property = property, Duration = OpenDuration, Easing = new CubicEaseOut() };

    private static ITransform Lift(double y, double scale)
    {
        TransformOperations.Builder builder = TransformOperations.CreateBuilder(2);
        builder.AppendTranslate(0, y);
        builder.AppendScale(scale, scale);
        return builder.Build();
    }

    /// <summary>Eases the backdrop's blur in or out.</summary>
    private static void Blur(Control? backdrop, bool on)
    {
        if (backdrop is null)
            return;
        backdrop.Transitions ??= [];
        if (!backdrop.Transitions.OfType<EffectTransition>().Any())
            backdrop.Transitions.Add(new EffectTransition { Property = EffectProperty, Duration = OpenDuration, Easing = new CubicEaseOut() });
        backdrop.Effect = new BlurEffect { Radius = on ? BlurRadius : 0 };
    }

    /// <summary>The room any dialog leaves on every side of the window.</summary>
    public const double Inset = 24;

    /// <summary>
    /// How big <paramref name="dialog"/> is in a window of <paramref name="window"/>: a
    /// <see cref="ModalSheet"/> takes its share of the window within its own minimum and maximum, so it
    /// grows and shrinks with the window; any other dialog keeps its own Width and Height. Either way it
    /// stays <see cref="Inset"/> clear of the edges.
    /// </summary>
    public static Size SizeFor(Control dialog, Size window)
    {
        double roomWidth = Math.Max(0, window.Width - 2 * Inset);
        double roomHeight = Math.Max(0, window.Height - 2 * Inset);
        if (GetFillsWindow(dialog))
            return new Size(roomWidth, roomHeight);
        if (dialog is ModalSheet sheet)
        {
            double width = Math.Clamp(window.Width * sheet.WindowShare.Width, sheet.MinSize.Width, sheet.MaxSize.Width);
            double height = Math.Clamp(window.Height * sheet.WindowShare.Height, sheet.MinSize.Height, sheet.MaxSize.Height);
            return new Size(Math.Floor(Math.Min(width, roomWidth)), Math.Floor(Math.Min(height, roomHeight)));
        }
        double fixedWidth = double.IsNaN(dialog.Width) ? dialog.DesiredSize.Width : dialog.Width;
        double fixedHeight = double.IsNaN(dialog.Height) ? dialog.DesiredSize.Height : dialog.Height;
        return new Size(Math.Min(fixedWidth, roomWidth), Math.Min(fixedHeight, roomHeight));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ghost?.Measure(ghostRect.Size);
        if (dialog.Parent != this)
            return availableSize;
        if ((dialog is ModalSheet || GetFillsWindow(dialog)) && !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height))
            dialog.Measure(SizeFor(dialog, availableSize));
        else
            dialog.Measure(Size.Infinity);
        return availableSize;
    }

    /// <summary>The dialog centred at <see cref="SizeFor"/>.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        ghost?.Arrange(ghostRect);
        if (dialog.Parent != this)
            return finalSize;
        (double width, double height) = SizeFor(dialog, finalSize);
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
        Dismiss();
        e.Handled = true;
    }

    /// <summary>Escape: the content decides how it closes, or else the modal closes.</summary>
    private void Dismiss()
    {
        if (dialog is IModalContent content)
            content.RequestClose();
        else
            Close();
    }
}
