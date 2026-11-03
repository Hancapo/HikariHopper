using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// The mip level slider of <see cref="TextureMipBar"/>: Qt Quick's Basic <c>Slider</c> (padding 6,
/// <c>stepSize</c> 1, <c>SnapAlways</c>, live) with the QML background and handle drawn in
/// <see cref="Render"/>.
/// </summary>
/// <remarks>
/// The whole control takes the pointer, not just the track and handle it draws: a press anywhere
/// on it moves the value straight to the snapped pointer position and takes focus, and dragging
/// keeps it there. Left/Right step it, holding the handle pressed while the key is down. <see cref="Moved"/> is QML <c>moved</c>: raised whenever the user changes the
/// position. Like Qt, the wheel does nothing.
/// </remarks>
public class TextureMipSlider : Control
{
    public const double ThumbWidth = 10;
    private const double SliderPadding = 6;

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<TextureMipSlider, int>(nameof(Maximum));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<TextureMipSlider, double>(nameof(Value));

    /// <summary>Number of tick marks (QML <c>Repeater { model: mipCount }</c>).</summary>
    public static readonly StyledProperty<int> TickCountProperty =
        AvaloniaProperty.Register<TextureMipSlider, int>(nameof(TickCount));

    private bool pressed;
    private bool keyPressed;

    static TextureMipSlider()
    {
        FocusableProperty.OverrideDefaultValue<TextureMipSlider>(true);
        AffectsRender<TextureMipSlider>(MaximumProperty, ValueProperty, TickCountProperty, IsFocusedProperty);
    }

    public TextureMipSlider()
    {
        AutomationProperties.SetName(this, "Mip level");
    }

    /// <summary>QML <c>moved</c>, with the rounded value.</summary>
    public event EventHandler<int>? Moved;

    /// <summary>Raised when the slider stops being pressed.</summary>
    public event EventHandler? Released;

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int TickCount
    {
        get => GetValue(TickCountProperty);
        set => SetValue(TickCountProperty, value);
    }

    /// <summary>Qt <c>pressed</c>: held by the pointer or an arrow key.</summary>
    public bool IsSliderPressed => pressed || keyPressed;

    /// <summary>Qt <c>visualPosition</c>.</summary>
    public double VisualPosition => Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;

    private double AvailableWidth => Math.Max(0, Bounds.Width - 2 * SliderPadding);

    /// <summary>Follows the view model unless the slider is pressed (QML <c>Binding { when: !pressed }</c>).</summary>
    public void SyncValue(int value)
    {
        if (!IsSliderPressed)
            Value = value;
    }

    protected override Size MeasureOverride(Size availableSize) => new(240 + 2 * SliderPadding, 24);

    public override void Render(DrawingContext context)
    {
        // Avalonia hit-tests what a control draws; this makes the empty space around the 3px track clickable too.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double available = AvailableWidth;
        double availableHeight = Math.Max(0, Bounds.Height - 2 * SliderPadding);
        double backgroundX = SliderPadding;
        double backgroundY = SliderPadding + QtRaster.JsRound((availableHeight - 20) / 2);
        double track = Math.Max(0, available - ThumbWidth);
        QtRaster.FillBordered(context, Ui.Theme.InsetBg, Ui.Theme.BorderHard,
            backgroundX + ThumbWidth / 2, backgroundY + (20 - 3) / 2.0, track, 3);
        QtRaster.Fill(context, Ui.Theme.TextDim, backgroundX + ThumbWidth / 2, backgroundY + (20 - 1) / 2.0,
            QtRaster.JsRound(track * VisualPosition), 1);
        int ticks = TickCount;
        for (int index = 0; index < ticks; index++)
        {
            double x = QtRaster.JsRound(ThumbWidth / 2 + (ticks > 1 ? index * (available - ThumbWidth) / (ticks - 1) : 0));
            QtRaster.Fill(context, Ui.Theme.Guide, backgroundX + x, backgroundY + (20 - 7) / 2.0, 1, 7);
        }
        double handleX = QtRaster.JsRound(SliderPadding + VisualPosition * (available - ThumbWidth));
        double handleY = SliderPadding + QtRaster.JsRound((availableHeight - 16) / 2);
        QtRaster.FillBordered(context, IsSliderPressed ? Ui.Theme.TextRow : Ui.Theme.ChromeRaised,
            IsFocused ? Ui.Theme.TextDim : Ui.Theme.Border, handleX, handleY, ThumbWidth, 16);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus(NavigationMethod.Pointer);
        pressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        MoveTo(e.GetPosition(this).X);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressed)
            MoveTo(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed)
            return;
        MoveTo(e.GetPosition(this).X);
        pressed = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
        Released?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!pressed)
            return;
        pressed = false;
        InvalidateVisual();
        Released?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right))
            return;
        keyPressed = true;
        double old = Value;
        Value = Math.Clamp(Value + (e.Key == Key.Right ? 1 : -1), 0, Maximum);
        e.Handled = true;
        InvalidateVisual();
        if (Value != old)
            Moved?.Invoke(this, (int)Math.Round(Value));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!keyPressed)
            return;
        keyPressed = false;
        InvalidateVisual();
        Released?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        keyPressed = false;
    }

    /// <summary>QQuickSlider::handleMove/handleRelease with <c>SnapAlways</c>.</summary>
    private void MoveTo(double x)
    {
        double extent = AvailableWidth - ThumbWidth;
        if (extent == 0)
            return;
        double oldPosition = VisualPosition;
        double position = (x - SliderPadding - ThumbWidth / 2) / extent;
        if (Maximum > 0)
            position = Math.Round(position * Maximum, MidpointRounding.AwayFromZero) / Maximum;
        Value = Math.Clamp(position * Maximum, 0, Maximum);
        if (position != oldPosition)
            Moved?.Invoke(this, (int)Math.Round(Value));
    }
}
