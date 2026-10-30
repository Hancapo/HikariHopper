using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The chassis every secondary window wears (port of <c>ToolWindow.qml</c>).
/// </summary>
/// <remarks>
/// These windows are frameless: the platform decoration is dropped and a 24px
/// <see cref="WindowTitleBar"/> showing <see cref="Heading"/> becomes the title bar, so a Settings
/// or About window is built out of the same parts as a docked panel. Content goes in the body,
/// inset by <see cref="BodyMargin"/>; a 44px footer carries <see cref="FooterText"/> and a Close
/// button. Esc closes. Closing hides the window (like closing a QML <c>Window</c>), so it reopens
/// where it was left; show it with <see cref="Window.Show(Window)"/> to centre it on its owner the
/// first time, as Qt centred a window on its transient parent.
/// </remarks>
public class ToolWindow : Window
{
    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<ToolWindow, string>(nameof(Heading), "");

    public static readonly StyledProperty<string> FooterTextProperty =
        AvaloniaProperty.Register<ToolWindow, string>(nameof(FooterText), "");

    public static readonly StyledProperty<double> BodyMarginProperty =
        AvaloniaProperty.Register<ToolWindow, double>(nameof(BodyMargin), 18);

    public static readonly DirectProperty<ToolWindow, Thickness> BodyThicknessProperty =
        AvaloniaProperty.RegisterDirect<ToolWindow, Thickness>(nameof(BodyThickness), o => o.BodyThickness);

    private Thickness bodyThickness = new(18);

    public ToolWindow()
    {
        QtFramelessWindow.Apply(this);
        ShowInTaskbar = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public string Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    public double BodyMargin
    {
        get => GetValue(BodyMarginProperty);
        set => SetValue(BodyMarginProperty, value);
    }

    /// <summary><see cref="BodyMargin"/> on all four sides.</summary>
    public Thickness BodyThickness
    {
        get => bodyThickness;
        private set => SetAndRaise(BodyThicknessProperty, ref bodyThickness, value);
    }

    protected override Type StyleKeyOverride => typeof(ToolWindow);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodyMarginProperty)
            BodyThickness = new Thickness(BodyMargin);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ChromeToolButton>("PART_CloseButton") is { } close)
            close.Click += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown)
            return;
        e.Cancel = true;
        Hide();
    }
}
