using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Covers the tab while a game loads (port of <c>GameLoadingView.qml</c>): it appears only once the
/// load has run for 200ms, and its mark shuttles 0→164→0 with InOutCubic, 420ms each way.
/// </summary>
public partial class GameLoadingView : UserControl
{
    private const double TravelMs = 420;
    private const double Travel = 164;

    private readonly DispatcherTimer revealTimer;
    private readonly DispatcherTimer animation;
    private readonly Stopwatch clock = new();
    private ExplorerViewModel? bridge;
    private bool revealed;

    public GameLoadingView()
    {
        InitializeComponent();
        revealTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, OnRevealTimer);
        animation = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnAnimationTick);
    }

    /// <summary>QML <c>revealed</c>.</summary>
    public bool Revealed => revealed;

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        Refresh();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ExplorerViewModel.GameLoading) or nameof(ExplorerViewModel.GameLoadingName)))
            return;
        // onGameLoadingChanged: a finished load hides the view until the next one has run 200ms.
        if (Bridge?.GameLoading != true)
            revealed = false;
        Refresh();
    }

    private void OnRevealTimer(object? sender, EventArgs e)
    {
        revealTimer.Stop();
        revealed = true;
        Refresh();
    }

    private void Refresh()
    {
        bool loading = Bridge?.GameLoading == true;
        // Timer { running: bridge.gameLoading && !revealed }
        if (loading && !revealed)
        {
            if (!revealTimer.IsEnabled)
                revealTimer.Start();
        }
        else
        {
            revealTimer.Stop();
        }
        bool visible = revealed && loading;
        IsVisible = visible;
        AutomationProperties.SetName(this, $"Loading {Bridge?.GameLoadingName}");
        if (visible && !animation.IsEnabled)
        {
            clock.Restart();
            animation.Start();
        }
        else if (!visible && animation.IsEnabled)
        {
            animation.Stop();
            Canvas.SetLeft(ProgressMark, 0);
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double elapsed = clock.Elapsed.TotalMilliseconds % (2 * TravelMs);
        double t = elapsed < TravelMs ? elapsed / TravelMs : 2 - elapsed / TravelMs;
        Canvas.SetLeft(ProgressMark, Travel * InOutCubic(t));
    }

    /// <summary>QEasingCurve::InOutCubic.</summary>
    internal static double InOutCubic(double t)
    {
        t *= 2;
        if (t < 1)
            return 0.5 * t * t * t;
        t -= 2;
        return 0.5 * (t * t * t + 2);
    }
}

/// <summary>
/// The loading view's Column: <c>anchors.centerIn: parent; anchors.verticalCenterOffset: -28;
/// spacing: 9</c>, texts on the horizontal centre and the rail at the left, all placed with Qt's
/// alignWhenCentered rounding.
/// </summary>
public sealed class LoadingColumn : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new Size(width, height + 9 * Math.Max(0, Children.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = Children.Max(child => child.DesiredSize.Width);
        double height = Children.Sum(child => child.DesiredSize.Height) + 9 * Math.Max(0, Children.Count - 1);
        double left = QtCenter.Half(finalSize.Width) - QtCenter.Half(width);
        double y = QtCenter.Half(finalSize.Height) - 28 - QtCenter.Half(height);
        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            Size size = child.DesiredSize;
            // Qt centres on the text's exact implicit width, not the whole-pixel desired width.
            double implicitWidth = child is TextBlock text ? text.TextLayout.WidthIncludingTrailingWhitespace : size.Width;
            double x = index < 2 ? left + Math.Round(QtCenter.Half(width) - QtCenter.Half(implicitWidth)) : left;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            y += size.Height + 9;
        }
        return finalSize;
    }
}
