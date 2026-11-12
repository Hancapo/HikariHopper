using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Covers the tab while a game loads with the common loading overlay (port of <c>GameLoadingView.qml</c>'s timing):
/// it appears only once the load has run for 200ms, and its train runs only while it shows.
/// </summary>
public partial class GameLoadingView : UserControl
{
    private readonly DispatcherTimer revealTimer;
    private ExplorerViewModel? bridge;
    private bool revealed;

    public GameLoadingView()
    {
        InitializeComponent();
        revealTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, OnRevealTimer);
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
        Overlay.IsRunning = visible;
        AutomationProperties.SetName(this, $"Loading {Bridge?.GameLoadingName}");
    }
}
