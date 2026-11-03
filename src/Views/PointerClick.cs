using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace HikariHopper.Views;

/// <summary>
/// QML <c>MouseArea { onClicked }</c> for a plain control: a left press grabs the pointer and the
/// release fires <c>clicked</c> when it lands inside the control again.
/// </summary>
public static class PointerClick
{
    public static void Attach(Control control, Action<PointerReleasedEventArgs> clicked, Func<bool>? enabled = null)
    {
        bool pressed = false;
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || enabled?.Invoke() == false)
                return;
            pressed = true;
            e.Pointer.Capture(control);
            e.Handled = true;
        };
        control.PointerReleased += (_, e) =>
        {
            if (!pressed || e.InitialPressMouseButton != MouseButton.Left)
                return;
            pressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (new Rect(control.Bounds.Size).Contains(e.GetPosition(control)))
                clicked(e);
        };
        control.PointerCaptureLost += (_, _) => pressed = false;
    }
}
