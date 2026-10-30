using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>
/// Qt Quick <c>Flickable</c> mouse-wheel scrolling for a <see cref="ScrollViewer"/>: each notch
/// flicks the content <c>wheelScrollLines × 24</c> pixels (72 with the Windows default of three
/// lines), decelerating at Flickable's default <c>wheelDeceleration</c> of 15000 px/s², and stops
/// at the bounds. Notches that arrive during a flick in the same direction extend it.
/// </summary>
internal sealed class WheelFlick
{
    private const double Deceleration = 15000;
    private static readonly double NotchDistance = 24 * WheelScrollLines();

    private readonly ScrollViewer viewer;
    private readonly DispatcherTimer timer;
    private double start;
    private double distance;
    private DateTime startTime;

    private WheelFlick(ScrollViewer viewer)
    {
        this.viewer = viewer;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        viewer.AddHandler(InputElement.PointerPressedEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public static WheelFlick Attach(ScrollViewer viewer) => new(viewer);

    /// <summary>Where the current flick ends.</summary>
    public double Target => start + distance;

    private double Maximum => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;
        e.Handled = true;
        double step = -e.Delta.Y * NotchDistance;
        double current = viewer.Offset.Y;
        if ((step > 0 && current >= Maximum) || (step < 0 && current <= 0))
            return;
        bool extend = timer.IsEnabled && Math.Sign(step) == Math.Sign(distance);
        double target = (extend ? Target : current) + step;
        start = current;
        distance = Math.Clamp(target, 0, Maximum) - current;
        startTime = DateTime.UtcNow;
        if (Application.Current?.ApplicationLifetime is null)
        {
            // Headless and test runs have no animation clock: land on the end of the flick.
            viewer.Offset = viewer.Offset.WithY(start + distance);
            return;
        }
        timer.Start();
        Tick();
    }

    private void Tick()
    {
        double velocity = Math.Sqrt(2 * Deceleration * Math.Abs(distance));
        double duration = velocity / Deceleration;
        double elapsed = Math.Min((DateTime.UtcNow - startTime).TotalSeconds, duration);
        double travelled = velocity * elapsed - Deceleration * elapsed * elapsed / 2;
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(start + Math.Sign(distance) * travelled, 0, Maximum));
        if (elapsed >= duration)
            timer.Stop();
    }

    private static int WheelScrollLines()
    {
        if (!OperatingSystem.IsWindows())
            return 3;
        uint lines = 3;
        return SystemParametersInfo(0x0068 /* SPI_GETWHEELSCROLLLINES */, 0, ref lines, 0) && lines is > 0 and < 100 ? (int)lines : 3;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref uint value, uint winIni);
}
