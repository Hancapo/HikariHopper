using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Invisible edge grips which delegate resizing back to the window manager
/// (port of <c>WindowResizeFrame.qml</c>): <see cref="GripWidth"/> px edges and corners, hidden
/// while the window is maximized or full screen. Lay it over the whole window, on top.
/// </summary>
public class WindowResizeFrame : Panel
{
    public static readonly StyledProperty<Window?> TargetWindowProperty =
        AvaloniaProperty.Register<WindowResizeFrame, Window?>(nameof(TargetWindow));

    public static readonly StyledProperty<double> GripWidthProperty =
        AvaloniaProperty.Register<WindowResizeFrame, double>(nameof(GripWidth), 5);

    private static readonly (WindowEdge Edge, StandardCursorType Cursor)[] Grips =
    [
        (WindowEdge.NorthWest, StandardCursorType.TopLeftCorner),
        (WindowEdge.NorthEast, StandardCursorType.TopRightCorner),
        (WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner),
        (WindowEdge.SouthEast, StandardCursorType.BottomRightCorner),
        (WindowEdge.West, StandardCursorType.SizeWestEast),
        (WindowEdge.East, StandardCursorType.SizeWestEast),
        (WindowEdge.North, StandardCursorType.SizeNorthSouth),
        (WindowEdge.South, StandardCursorType.SizeNorthSouth),
    ];

    private IDisposable? windowStateSubscription;

    public WindowResizeFrame()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        foreach ((WindowEdge edge, StandardCursorType cursor) in Grips)
        {
            var grip = new Border { Background = Brushes.Transparent, Cursor = new Cursor(cursor), Tag = edge };
            grip.PointerPressed += OnGripPressed;
            Children.Add(grip);
        }
        LayoutGrips();
    }

    public Window? TargetWindow
    {
        get => GetValue(TargetWindowProperty);
        set => SetValue(TargetWindowProperty, value);
    }

    public double GripWidth
    {
        get => GetValue(GripWidthProperty);
        set => SetValue(GripWidthProperty, value);
    }

    private Window? EffectiveWindow => TargetWindow ?? TopLevel.GetTopLevel(this) as Window;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Observe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        windowStateSubscription?.Dispose();
        windowStateSubscription = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GripWidthProperty)
            LayoutGrips();
        if (change.Property == TargetWindowProperty && VisualRoot is not null)
            Observe();
    }

    private void Observe()
    {
        windowStateSubscription?.Dispose();
        Window? window = EffectiveWindow;
        windowStateSubscription = window?.GetObservable(Window.WindowStateProperty)
            .Subscribe(new StateObserver(state => IsVisible = state is not (WindowState.Maximized or WindowState.FullScreen)));
    }

    private void LayoutGrips()
    {
        double grip = GripWidth;
        foreach (Control child in Children)
        {
            var edge = (WindowEdge)child.Tag!;
            bool west = edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
            bool east = edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
            bool north = edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
            bool south = edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;
            child.HorizontalAlignment = west ? HorizontalAlignment.Left : east ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            child.VerticalAlignment = north ? VerticalAlignment.Top : south ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            child.Width = west || east ? grip : double.NaN;
            child.Height = north || south ? grip : double.NaN;
            // The edge grips stop short of the corners, which own the last GripWidth pixels.
            child.Margin = edge switch
            {
                WindowEdge.West or WindowEdge.East => new Thickness(0, grip, 0, grip),
                WindowEdge.North or WindowEdge.South => new Thickness(grip, 0, grip, 0),
                _ => default,
            };
        }
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: WindowEdge edge } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        EffectiveWindow?.BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private sealed class StateObserver(Action<WindowState> onNext) : IObserver<WindowState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(WindowState value) => onNext(value);
    }
}
