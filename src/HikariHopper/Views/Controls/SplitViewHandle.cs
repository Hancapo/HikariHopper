using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The 1px panel divider (port of <c>SplitViewHandle.qml</c>): <c>border</c> at rest, <c>textDim</c>
/// hovered, <c>textRow</c> while dragged, with a <see cref="Theme.SplitHandleHitWidth"/> hit area
/// centred on the line.
/// </summary>
/// <remarks>
/// Put it in a 1px-wide <c>Auto</c> grid column between the panels. Its default width and margins
/// (8px, <c>-4,0,-3,0</c>) let the hit area overhang both neighbours while the column stays 1px,
/// as QML's <c>containmentMask</c> did; keep it above the panels in z-order.
/// </remarks>
public class SplitViewHandle : GridSplitter
{
    private Border? line;
    private bool dragging;

    /// <summary>Qt <c>SplitHandle.pressed</c>.</summary>
    public bool Pressed => dragging;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        line = e.NameScope.Find<Border>("PART_Line");
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            Refresh();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            dragging = true;
            Refresh();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragging = false;
        Refresh();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
        Refresh();
    }

    private void Refresh()
    {
        if (line is null)
            return;
        line.Background = dragging ? Ui.Theme.TextRow : IsPointerOver ? Ui.Theme.TextDim : Ui.Theme.Border;
    }
}
