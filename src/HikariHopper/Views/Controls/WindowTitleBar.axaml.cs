using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Shared application-owned title bar and window controls (port of <c>WindowTitleBar.qml</c>).
/// </summary>
/// <remarks>
/// <see cref="TargetWindow"/> defaults to the window hosting the bar. Moving is delegated to the
/// window manager once a press on the title area moves past the system drag threshold (Qt's
/// <c>DragHandler</c> + <c>startSystemMove()</c>); a double click toggles maximize when
/// <see cref="ShowMaximize"/> is set.
/// </remarks>
public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<Window?> TargetWindowProperty =
        AvaloniaProperty.Register<WindowTitleBar, Window?>(nameof(TargetWindow));

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(TitleText), "HikariHopper");

    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<WindowTitleBar, bool>(nameof(ShowMinimize), true);

    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<WindowTitleBar, bool>(nameof(ShowMaximize), true);

    public static readonly StyledProperty<double> ControlWidthProperty =
        AvaloniaProperty.Register<WindowTitleBar, double>(nameof(ControlWidth), 34);

    public static readonly DirectProperty<WindowTitleBar, bool> MaximizedProperty =
        AvaloniaProperty.RegisterDirect<WindowTitleBar, bool>(nameof(Maximized), o => o.Maximized);

    private Window? observedWindow;
    private IDisposable? windowStateSubscription;
    private PointerPressedEventArgs? dragPress;
    private Point dragOrigin;
    private bool maximized;
    private bool doubleTapPending;

    static WindowTitleBar()
    {
        // QML implicitHeight: a default, so a ToolWindow's 24px header height wins.
        HeightProperty.OverrideDefaultValue<WindowTitleBar>(Ui.Theme.WindowTitleHeight);
    }

    public WindowTitleBar()
    {
        InitializeComponent();
        Panel dragArea = DragArea;
        dragArea.PointerPressed += OnDragPressed;
        dragArea.PointerMoved += OnDragMoved;
        dragArea.PointerReleased += OnDragReleased;
        dragArea.PointerCaptureLost += (_, _) => dragPress = null;
        MinimizeButton.Click += (_, _) => EffectiveWindow?.SetCurrentValue(Window.WindowStateProperty, WindowState.Minimized);
        MaximizeButton.Click += (_, _) => ToggleMaximized();
        CloseButton.Click += (_, _) => EffectiveWindow?.Close();
        UpdateControls();
    }

    public Window? TargetWindow
    {
        get => GetValue(TargetWindowProperty);
        set => SetValue(TargetWindowProperty, value);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public bool ShowMinimize
    {
        get => GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public double ControlWidth
    {
        get => GetValue(ControlWidthProperty);
        set => SetValue(ControlWidthProperty, value);
    }

    /// <summary>True while the target window is maximized.</summary>
    public bool Maximized
    {
        get => maximized;
        private set
        {
            if (SetAndRaise(MaximizedProperty, ref maximized, value))
                UpdateControls();
        }
    }

    private Window? EffectiveWindow => TargetWindow ?? TopLevel.GetTopLevel(this) as Window;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveWindow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        windowStateSubscription?.Dispose();
        windowStateSubscription = null;
        observedWindow = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TargetWindowProperty)
            ObserveWindow();
        if (change.Property == ShowMinimizeProperty || change.Property == ShowMaximizeProperty
            || change.Property == ControlWidthProperty)
            UpdateControls();
    }

    private void ObserveWindow()
    {
        Window? window = EffectiveWindow;
        if (window == observedWindow)
            return;
        windowStateSubscription?.Dispose();
        observedWindow = window;
        windowStateSubscription = window?.GetObservable(Window.WindowStateProperty)
            .Subscribe(new AnonymousObserver<WindowState>(state => Maximized = state == WindowState.Maximized));
        Maximized = window?.WindowState == WindowState.Maximized;
    }

    private void UpdateControls()
    {
        MinimizeButton.IsVisible = ShowMinimize;
        MaximizeButton.IsVisible = ShowMaximize;
        MinimizeButton.Width = ControlWidth;
        MaximizeButton.Width = ControlWidth;
        CloseButton.Width = ControlWidth;
        MaximizeButton.Kind = Maximized ? "restore" : "maximize";
        AutomationProperties.SetName(MaximizeButton, Maximized ? "Restore window" : "Maximize window");
    }

    private void ToggleMaximized()
    {
        Window? window = EffectiveWindow;
        if (window is null)
            return;
        window.WindowState = Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        doubleTapPending = e.ClickCount == 2;
        if (doubleTapPending)
        {
            dragPress = null;
            e.Handled = true;
            return;
        }
        dragPress = e;
        dragOrigin = e.GetPosition(this);
    }

    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (dragPress is null)
            return;
        Point position = e.GetPosition(this);
        double threshold = DragThreshold();
        if (Math.Abs(position.X - dragOrigin.X) > threshold || Math.Abs(position.Y - dragOrigin.Y) > threshold)
        {
            PointerPressedEventArgs press = dragPress;
            dragPress = null;
            EffectiveWindow?.BeginMoveDrag(press);
        }
    }

    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        dragPress = null;
        if (!doubleTapPending)
            return;
        // TapHandler.onDoubleTapped (on the second release): maximize or restore when offered.
        doubleTapPending = false;
        if (ShowMaximize)
            ToggleMaximized();
    }

    /// <summary>Qt's <c>startDragDistance</c> on Windows is the system drag rectangle (SM_CXDRAG).</summary>
    private static double DragThreshold() => OperatingSystem.IsWindows() ? Math.Max(1, GetSystemMetrics(68)) : 4;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
