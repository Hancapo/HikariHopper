using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.Ui;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Covers the tab while a game loads with the common loading overlay (port of <c>GameLoadingView.qml</c>'s timing):
/// it appears only once the load has run for 200ms, and its train and the railway pattern behind it run only while it
/// shows. A load that opens the game fades it out over the explorer, already in place under it (<see cref="FadeDuration"/>);
/// one that fails, or any with motion reduced, takes it away at once.
/// </summary>
public partial class GameLoadingView : UserControl
{
    private readonly DispatcherTimer revealTimer;
    private ExplorerViewModel? bridge;
    private bool revealed;
    // While it fades out over the opened game, and what ends the fade.
    private bool fading;
    private IDisposable? fadeEnd;

    /// <summary>How long it takes to fade out over the opened game; tests shorten it.</summary>
    internal static TimeSpan FadeDuration { get; set; } = TimeSpan.FromMilliseconds(320);

    public GameLoadingView()
    {
        InitializeComponent();
        revealTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, OnRevealTimer);
    }

    /// <summary>QML <c>revealed</c>.</summary>
    public bool Revealed => revealed;

    /// <summary>Whether it is fading out over the opened game.</summary>
    internal bool Fading => fading;

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        // Another tab: what it shows replaces this one's at once, and a fade over the last one's game goes with it, rather
        // than start again over this one's.
        if (fading)
        {
            StopFade();
            Hide();
        }
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        Refresh();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ExplorerViewModel.GameLoading) or nameof(ExplorerViewModel.GameLoadingName)
            or nameof(ExplorerViewModel.GameLoadingStage)))
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
        // A load begun while it fades out over the last game: it is back, opaque and taking clicks, already shown.
        if (loading && fading)
        {
            StopFade();
            revealed = true;
        }
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
        if (visible)
            StopFade();
        // The load opened the game: fade out over it, rather than vanish.
        else if (IsVisible && !fading && !loading && Bridge?.HasWorkspace == true && !Motion.Reduced && FadeDuration > TimeSpan.Zero)
            StartFade();
        // Shown, its train and pattern running, until the fade ends.
        if (fading)
            return;
        // The pattern where the start page's stopped as the load began, so the page doesn't jump, drifting on from
        // there while the view shows; the start page's stands still meanwhile, behind it.
        if (visible && !IsVisible)
            Pattern.Sync();
        IsVisible = visible;
        Overlay.IsRunning = visible;
        Pattern.IsRunning = visible;
        AutomationProperties.SetName(this, $"Loading {Bridge?.GameLoadingName}");
    }

    // Gone, its train and pattern stopped.
    private void Hide()
    {
        IsVisible = false;
        Overlay.IsRunning = false;
        Pattern.IsRunning = false;
    }

    private void StartFade()
    {
        fading = true;
        // Clicks go to the explorer under it from the start.
        IsHitTestVisible = false;
        // From the next frame, once the explorer under it has been laid out and drawn, so what shows through is
        // never empty.
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            EndFade();
            return;
        }
        top.RequestAnimationFrame(_ =>
        {
            if (!fading)
                return;
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = FadeDuration, Easing = new SineEaseInOut() }];
            Opacity = 0;
            fadeEnd = DispatcherTimer.RunOnce(EndFade, FadeDuration);
        });
    }

    private void EndFade()
    {
        // Gone before it is made opaque again, so it doesn't show for a frame, nor fade again.
        IsVisible = false;
        StopFade();
        Refresh();
    }

    // Back as it was before the fade: opaque, taking clicks, without a transition.
    private void StopFade()
    {
        if (!fading)
            return;
        fading = false;
        fadeEnd?.Dispose();
        fadeEnd = null;
        Transitions = null;
        Opacity = 1;
        IsHitTestVisible = true;
    }
}
