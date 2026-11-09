using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Shared application-owned title bar and window controls (port of <c>WindowTitleBar.qml</c>).
/// </summary>
/// <remarks>
/// <see cref="TargetWindow"/> defaults to the window hosting the bar. The bar sits in a window whose client area
/// covers its frame (<see cref="Window.ExtendClientAreaToDecorationsHint"/>), and tells Windows what its parts are
/// (<see cref="WindowDecorationProperties.ElementRole"/>): the title area is the window's caption, so Windows moves,
/// snaps and maximizes the window from it as from any title bar, and the buttons are its minimize, maximize and close
/// buttons, so Windows 11 offers snap layouts on maximize. The buttons still act through their own clicks.
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

    /// <summary>Shows <see cref="TitleText"/>; the explorer hides it because its tab names the document.</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty =
        AvaloniaProperty.Register<WindowTitleBar, bool>(nameof(ShowTitle), true);

    /// <summary>Content placed right after the emblem, such as a menu bar.</summary>
    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<WindowTitleBar, object?>(nameof(LeadingContent));

    /// <summary>12 for the compact bars; the explorer's taller row wears a 16px emblem with rounded corners.</summary>
    public static readonly StyledProperty<double> EmblemSizeProperty =
        AvaloniaProperty.Register<WindowTitleBar, double>(nameof(EmblemSize), 12);

    /// <summary>The hard seams between the window controls and along the bar's edges.</summary>
    public static readonly StyledProperty<bool> SeamsProperty =
        AvaloniaProperty.Register<WindowTitleBar, bool>(nameof(Seams), true);

    public static readonly DirectProperty<WindowTitleBar, bool> MaximizedProperty =
        AvaloniaProperty.RegisterDirect<WindowTitleBar, bool>(nameof(Maximized), o => o.Maximized);

    private Window? observedWindow;
    private IDisposable? windowStateSubscription;
    private bool maximized;

    static WindowTitleBar()
    {
        // QML implicitHeight: a default, so a ToolWindow's 24px header height wins.
        HeightProperty.OverrideDefaultValue<WindowTitleBar>(Ui.Theme.WindowTitleHeight);
    }

    public WindowTitleBar()
    {
        InitializeComponent();
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

    public bool ShowTitle
    {
        get => GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public double EmblemSize
    {
        get => GetValue(EmblemSizeProperty);
        set => SetValue(EmblemSizeProperty, value);
    }

    public bool Seams
    {
        get => GetValue(SeamsProperty);
        set => SetValue(SeamsProperty, value);
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
            || change.Property == ControlWidthProperty || change.Property == ShowTitleProperty
            || change.Property == EmblemSizeProperty || change.Property == SeamsProperty
            || change.Property == LeadingContentProperty)
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
        MinimizeButton.Separated = MaximizeButton.Separated = CloseButton.Separated = Seams;
        TopBevel.IsVisible = BottomSeam.IsVisible = Seams;
        TitleLabel.IsVisible = ShowTitle;
        // The emblem is the app icon; the explorer's taller row wears it larger, 14px before the menus.
        bool large = EmblemSize >= 16;
        Emblem.Width = Emblem.Height = EmblemSize;
        TitleRow.Margin = new Thickness(large ? 12 : 9, 0, 8, 0);
        LeadingHost.Margin = new Thickness(large ? 14 : 8, 0, 0, 0);
        LeadingHost.IsVisible = LeadingContent is not null;
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
