using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The 1px blue piping around a window that owns its frame, painted on top so no panel seam
/// covers it. It steps aside when the window manager already draws it along rounded corners
/// (<see cref="QtFramelessWindow.RoundCorners"/>), so the two never stack.
/// </summary>
public class WindowPiping : Border
{
    /// <summary>Set on a window once DWM rounds it and draws its border.</summary>
    public static readonly AttachedProperty<bool> NativeBorderProperty =
        AvaloniaProperty.RegisterAttached<WindowPiping, Window, bool>("NativeBorder");

    private IDisposable? subscription;

    public WindowPiping()
    {
        BorderBrush = Ui.Theme.BorderAccent;
        BorderThickness = new Thickness(1);
        IsHitTestVisible = false;
    }

    public static bool GetNativeBorder(Window window) => window.GetValue(NativeBorderProperty);

    public static void SetNativeBorder(Window window, bool value) => window.SetValue(NativeBorderProperty, value);

    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
            subscription = window.GetObservable(NativeBorderProperty).Subscribe(new Observer(native => IsVisible = !native));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        subscription?.Dispose();
        subscription = null;
    }

    private sealed class Observer(Action<bool> onNext) : IObserver<bool>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(bool value) => onNext(value);
    }
}
