using System.Collections.Specialized;
using System.ComponentModel;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper;

/// <summary>
/// The startup session (Python: the <c>ExplorerTabs</c> handed to <c>main.qml</c>): builds the
/// <see cref="ExplorerScreen"/> into the main window, keeps the window title on the active tab and
/// keeps one <see cref="TextureEditor"/> per tab (main.qml's <c>Instantiator</c>), shown as a modal over the window.
/// </summary>
public sealed class ExplorerSession : IStartupSession
{
    private readonly Dictionary<ExplorerViewModel, TextureEditor> textureEditors = [];
    private MainWindow? window;
    private ExplorerViewModel? titleBridge;

    public ExplorerSession() : this(new ExplorerTabsViewModel())
    {
    }

    public ExplorerSession(ExplorerTabsViewModel tabs) => Tabs = tabs;

    public ExplorerTabsViewModel Tabs { get; }

    public bool HasWorkspace => Tabs.ActiveBridge.HasWorkspace;

    public string GameName => Tabs.ActiveBridge.GameName;

    /// <summary>The texture editor of each open tab.</summary>
    public IReadOnlyDictionary<ExplorerViewModel, TextureEditor> TextureEditors => textureEditors;

    public void RestoreLastGame() => Tabs.RestoreLastGame();

    public void Attach(MainWindow mainWindow)
    {
        window = mainWindow;
        var screen = new ExplorerScreen { DataContext = Tabs };
        mainWindow.ExplorerScreenHost.Content = screen;
        mainWindow.TitleBar.LeadingContent = screen.Menu;
        Tabs.PropertyChanged += OnTabsPropertyChanged;
        Tabs.Tabs.CollectionChanged += OnTabsChanged;
        foreach (ExplorerViewModel tab in Tabs.Tabs)
            AddTextureEditor(tab);
        TrackTitle();
    }

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.ActiveBridge))
            TrackTitle();
    }

    private void TrackTitle()
    {
        if (titleBridge is not null)
            titleBridge.PropertyChanged -= OnTitleBridgeChanged;
        titleBridge = Tabs.ActiveBridge;
        titleBridge.PropertyChanged += OnTitleBridgeChanged;
        UpdateTitle();
    }

    private void OnTitleBridgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.HasWorkspace) or nameof(ExplorerViewModel.TabTitle))
            UpdateTitle();
    }

    private void UpdateTitle()
    {
        if (window is not null)
            window.Title = MainWindow.TitleFor(Tabs.ActiveBridge.HasWorkspace, Tabs.ActiveBridge.TabTitle);
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ExplorerViewModel tab in textureEditors.Keys.Where(tab => !Tabs.Tabs.Contains(tab)).ToList())
        {
            textureEditors.Remove(tab, out TextureEditor? editor);
            editor?.Destroy();
        }
        foreach (ExplorerViewModel tab in Tabs.Tabs)
            AddTextureEditor(tab);
    }

    private void AddTextureEditor(ExplorerViewModel tab)
    {
        if (!textureEditors.ContainsKey(tab))
            textureEditors[tab] = new TextureEditor(tab.TextureViewer) { Anchor = window?.ExplorerScreenHost };
    }
}
