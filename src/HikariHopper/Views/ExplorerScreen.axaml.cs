using System.ComponentModel;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The explorer interface inside the main window (port of <c>ExplorerScreen.qml</c>). Its DataContext
/// is the <see cref="ExplorerTabsViewModel"/>; the page Loader shows a <see cref="WorkspaceView"/> while
/// the active tab has a workspace and a <see cref="StartPage"/> otherwise.
/// </summary>
public partial class ExplorerScreen : UserControl
{
    private ExplorerTabsViewModel? tabs;
    private ExplorerViewModel? bridge;

    public ExplorerScreen()
    {
        InitializeComponent();
        Menu.SearchRequested += (_, _) => Bridge?.RequestSearchFocus();
    }

    /// <summary>The page currently shown (a <see cref="WorkspaceView"/> or a <see cref="StartPage"/>).</summary>
    public Control? Page => PageHost.Content as Control;

    private ExplorerTabsViewModel? Tabs => DataContext as ExplorerTabsViewModel;

    private ExplorerViewModel? Bridge => Tabs?.ActiveBridge;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (tabs is not null)
            tabs.PropertyChanged -= OnTabsPropertyChanged;
        tabs = Tabs;
        if (tabs is not null)
            tabs.PropertyChanged += OnTabsPropertyChanged;
        TrackBridge();
    }

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.ActiveBridge))
            TrackBridge();
    }

    private void TrackBridge()
    {
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        RefreshPage();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.HasWorkspace))
            RefreshPage();
    }

    /// <summary>The Loader: the same component keeps its instance and only rebinds to the new tab.</summary>
    private void RefreshPage()
    {
        if (bridge is null)
        {
            PageHost.Content = null;
            return;
        }
        if (bridge.HasWorkspace)
        {
            if (PageHost.Content is not WorkspaceView workspace)
            {
                workspace = new WorkspaceView();
                PageHost.Content = workspace;
            }
            workspace.DataContext = bridge;
        }
        else
        {
            if (PageHost.Content is not StartPage start)
            {
                start = new StartPage();
                PageHost.Content = start;
            }
            start.DataContext = bridge;
            start.Tabs = tabs;
        }
    }
}
