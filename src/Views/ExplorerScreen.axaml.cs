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

    /// <summary>
    /// The File/Edit/View/Go/Help bar. The window shows it in its title row
    /// (<see cref="Controls.WindowTitleBar.LeadingContent"/>); it acts on these tabs.
    /// </summary>
    public MenuRow Menu { get; } = new();

    /// <summary>The page currently shown (a <see cref="WorkspaceView"/> or a <see cref="StartPage"/>).</summary>
    public Control? Page => PageHost.Content as Control;

    private ExplorerTabsViewModel? Tabs => DataContext as ExplorerTabsViewModel;

    private ExplorerViewModel? Bridge => Tabs?.ActiveBridge;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (tabs is not null)
        {
            tabs.PropertyChanged -= OnTabsPropertyChanged;
            tabs.Tabs.CollectionChanged -= OnTabsChanged;
        }
        tabs = Tabs;
        if (tabs is not null)
        {
            tabs.PropertyChanged += OnTabsPropertyChanged;
            tabs.Tabs.CollectionChanged += OnTabsChanged;
        }
        Menu.DataContext = tabs;
        TrackBridge();
    }

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.ActiveBridge))
            TrackBridge();
    }

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshStrip();

    /// <summary>
    /// The tab strip, hidden while the window's only tab is the launcher: there is nothing to switch
    /// to. It comes back with a second tab, and with the workspace a game or archive opens; not while
    /// it loads, which would push the launcher, and then the loading cover, down under it.
    /// </summary>
    public bool StripVisible => tabs is null || bridge is null || tabs.Tabs.Count > 1 || bridge.HasWorkspace;

    private void RefreshStrip()
    {
        Strip.IsVisible = StripVisible;
        PlaceLoadingView();
    }

    /// <summary>
    /// Places the game loading view while it is hidden, so it shows where it will stay: over the strip's row when there
    /// is no strip (a load from the lone launcher), under the strip when there is one, which other tabs keep using.
    /// While it shows, and fades out over the opened game, it keeps its place: the strip and the workspace that come
    /// with the game appear under it, not move it.
    /// </summary>
    private void PlaceLoadingView()
    {
        if (LoadingView.IsVisible)
            return;
        bool overStrip = !Strip.IsVisible;
        Grid.SetRow(LoadingView, overStrip ? 0 : 1);
        Grid.SetRowSpan(LoadingView, overStrip ? 2 : 1);
    }

    private void TrackBridge()
    {
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        RefreshPage();
        RefreshStrip();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.HasWorkspace))
            RefreshPage();
        if (e.PropertyName == nameof(ExplorerViewModel.HasWorkspace))
            RefreshStrip();
    }

    /// <summary>The Loader: the same component keeps its instance and only rebinds to the new tab.</summary>
    private void RefreshPage()
    {
        if (bridge is null)
        {
            Show(null);
            return;
        }
        if (bridge.HasWorkspace)
        {
            if (PageHost.Content is not WorkspaceView workspace)
                Show(workspace = new WorkspaceView());
            workspace.DataContext = bridge;
        }
        else
        {
            if (PageHost.Content is not StartPage start)
            {
                Show(start = new StartPage());
                start.SettingsRequested += (_, _) => Menu.OpenSettings();
            }
            start.DataContext = bridge;
            start.Tabs = tabs;
        }
    }

    /// <summary>
    /// Replaces the page. The old one lets go of its view models, so its views drop their
    /// subscriptions and the bridge does not keep the discarded tree alive.
    /// </summary>
    private void Show(Control? page)
    {
        if (PageHost.Content is Control old)
        {
            old.DataContext = null;
            if (old is StartPage start)
                start.Tabs = null;
        }
        PageHost.Content = page;
        // The start page takes the whole screen, the strip over its top when there is one; a workspace starts under it.
        bool launcher = page is StartPage;
        Grid.SetRow(Pages, launcher ? 0 : 1);
        Grid.SetRowSpan(Pages, launcher ? 2 : 1);
    }
}
