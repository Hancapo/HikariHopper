using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;

namespace HikariHopper.Views;

/// <summary>
/// The startup splash (port of <c>SplashScreen.qml</c>): an always-on-top frameless identity
/// window centred on the screen, naming the startup operation on the left of its bottom strip and
/// the short phase (BOOT, CORE, GAME, UI, WINDOW, ERROR) on the right.
/// </summary>
public partial class SplashScreen : Window
{
    public static readonly StyledProperty<string> StartupMessageProperty =
        AvaloniaProperty.Register<SplashScreen, string>(nameof(StartupMessage), "Starting…");

    public static readonly StyledProperty<string> StartupPhaseProperty =
        AvaloniaProperty.Register<SplashScreen, string>(nameof(StartupPhase), "BOOT");

    public SplashScreen()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }

    public string StartupMessage
    {
        get => GetValue(StartupMessageProperty);
        set => SetValue(StartupMessageProperty, value);
    }

    public string StartupPhase
    {
        get => GetValue(StartupPhaseProperty);
        set => SetValue(StartupPhaseProperty, value);
    }

    /// <summary>x/y = round((Screen.width/height - size) / 2) on the full primary screen, as the QML did.</summary>
    public void CenterOnScreen()
    {
        Screen? screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
            return;
        double scale = screen.Scaling;
        double screenWidth = screen.Bounds.Width / scale;
        double screenHeight = screen.Bounds.Height / scale;
        double x = screen.Bounds.X / scale + Math.Round((screenWidth - Ui.Theme.SplashWidth) / 2, MidpointRounding.AwayFromZero);
        double y = screen.Bounds.Y / scale + Math.Round((screenHeight - Ui.Theme.SplashHeight) / 2, MidpointRounding.AwayFromZero);
        Position = new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }
}

/// <summary>
/// The splash livery: a green strip with a navy core and the blue piping line along its left
/// edge, drawn over the top 376px (the QML <c>Shape</c>).
/// </summary>
public sealed class SplashLivery : Control
{
    private static readonly Geometry Green = Polygon((350, 0), (530, 0), (720, 376), (540, 376));
    private static readonly Geometry Navy = Polygon((394, 0), (486, 0), (676, 376), (584, 376));
    private static readonly Pen Piping = new(Ui.Theme.BorderAccent, 1);

    public SplashLivery()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        context.DrawGeometry(Ui.Theme.Selection, null, Green);
        context.DrawGeometry(Ui.Theme.WindowChrome, null, Navy);
        context.DrawLine(Piping, new Point(350, 0), new Point(540, 376));
    }

    private static Geometry Polygon(params (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext context = geometry.Open();
        context.BeginFigure(new Point(points[0].X, points[0].Y), true);
        foreach ((double x, double y) in points.Skip(1))
            context.LineTo(new Point(x, y));
        context.EndFigure(true);
        return geometry;
    }
}
