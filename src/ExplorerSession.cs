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

    /// <summary>The text viewer last opened, while it is showing.</summary>
    public TextViewer? TextViewer { get; private set; }

    /// <summary>The image viewer while one is open.</summary>
    public ImageViewer? ImageViewer { get; private set; }

    /// <summary>The 3D viewer, while it is open.</summary>
    public ModelViewer? ModelViewer { get; private set; }

    public void RestoreLastGame() => Tabs.RestoreLastGame();

    public void Attach(MainWindow mainWindow)
    {
        window = mainWindow;
        var screen = new ExplorerScreen { DataContext = Tabs };
        mainWindow.ExplorerScreenHost.Content = screen;
        // A new version is offered once the window is up.
        mainWindow.Opened += (_, _) => _ = screen.Menu.Updates.CheckAtStartup();
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
            tab.TextFileOpened -= OnTextFileOpened;
            tab.ImageFileOpened -= OnImageFileOpened;
            tab.ModelFileOpened -= OnModelFileOpened;
        }
        foreach (ExplorerViewModel tab in Tabs.Tabs)
            AddTextureEditor(tab);
    }

    private void AddTextureEditor(ExplorerViewModel tab)
    {
        if (textureEditors.ContainsKey(tab))
            return;
        textureEditors[tab] = new TextureEditor(tab.TextureViewer) { Anchor = window?.ExplorerScreenHost };
        tab.TextFileOpened += OnTextFileOpened;
        tab.ImageFileOpened += OnImageFileOpened;
        tab.ModelFileOpened += OnModelFileOpened;
    }

    private void OnModelFileOpened(object? sender, ModelFile file)
    {
        ModelViewer?.Close();
        var viewer = new ModelViewer(file) { Anchor = window?.ExplorerScreenHost };
        ModelViewer = viewer;
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(ModelViewer, viewer))
                ModelViewer = null;
        };
        viewer.Present();
    }

    private void OnImageFileOpened(object? sender, ImageViewerViewModel image)
    {
        ImageViewer?.Close();
        var viewer = new ImageViewer(image) { Anchor = window?.ExplorerScreenHost };
        ImageViewer = viewer;
        // Let go of it once closed: it holds the decoded image.
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(ImageViewer, viewer))
                ImageViewer = null;
        };
        viewer.Present();
    }

    private void OnTextFileOpened(object? sender, TextFile file)
    {
        // The one open goes first; with changes unsaved it asks, and if that is cancelled it stays and this one doesn't open.
        if (TextViewer is { } open)
            open.CloseThen(() => ShowTextViewer(file));
        else
            ShowTextViewer(file);
    }

    private void ShowTextViewer(TextFile file)
    {
        var viewer = new TextViewer(file) { Anchor = window?.ExplorerScreenHost };
        TextViewer = viewer;
        // Let go of it once closed: it holds the whole file.
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(TextViewer, viewer))
                TextViewer = null;
        };
        viewer.Present();
    }
}
