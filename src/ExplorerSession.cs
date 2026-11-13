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
    private readonly UnsavedWork unsaved;
    private MainWindow? window;
    private ExplorerViewModel? titleBridge;
    // Whether a close is being asked about: waiting for a save under way, the prompt up, or its files being saved.
    private bool askingToClose;

    public ExplorerSession() : this(new ExplorerTabsViewModel())
    {
    }

    public ExplorerSession(ExplorerTabsViewModel tabs)
    {
        Tabs = tabs;
        unsaved = new UnsavedWork(this);
    }

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

    /// <summary>The tab the text viewer was opened from.</summary>
    public ExplorerViewModel? TextViewerTab { get; private set; }

    /// <summary>The tab the 3D viewer was opened from.</summary>
    public ExplorerViewModel? ModelViewerTab { get; private set; }

    /// <summary>The prompt asking about unsaved files before the app or a tab closes, while it is up.</summary>
    public UnsavedFilesDialog? UnsavedPrompt { get; private set; }

    /// <summary>The last close asked about, done once it closed or was called off; for tests.</summary>
    internal Task Closing { get; private set; } = Task.CompletedTask;

    public void RestoreLastGame() => Tabs.RestoreLastGame();

    public void Attach(MainWindow mainWindow)
    {
        window = mainWindow;
        var screen = new ExplorerScreen { DataContext = Tabs };
        mainWindow.ExplorerScreenHost.Content = screen;
        // A new version is offered once the window is up.
        mainWindow.Opened += (_, _) => _ = screen.Menu.Updates.CheckAtStartup();
        mainWindow.TitleBar.LeadingContent = screen.Menu;
        // Unsaved files are asked about before the window or a tab closes, and hold an update's restart back.
        mainWindow.AskBeforeClosing = AskBeforeClosing;
        Tabs.ConfirmClose = ConfirmTabClose;
        screen.Menu.Updates.Unsaved = unsaved;
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
        ModelViewerTab = sender as ExplorerViewModel;
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(ModelViewer, viewer))
            {
                ModelViewer = null;
                ModelViewerTab = null;
            }
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
        var tab = sender as ExplorerViewModel;
        if (TextViewer is { } open)
            open.CloseThen(() => ShowTextViewer(file, tab));
        else
            ShowTextViewer(file, tab);
    }

    private void ShowTextViewer(TextFile file, ExplorerViewModel? tab)
    {
        var viewer = new TextViewer(file) { Anchor = window?.ExplorerScreenHost };
        TextViewer = viewer;
        TextViewerTab = tab;
        // Let go of it once closed: it holds the whole file.
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(TextViewer, viewer))
            {
                TextViewer = null;
                TextViewerTab = null;
            }
        };
        viewer.Present();
    }

    // ---------------- Closing with files unsaved ----------------

    /// <summary>
    /// The window is about to close: it does, at once, with nothing unsaved; otherwise it stays, the files are asked
    /// about (once a save under way is written), and it closes once they are saved or let go.
    /// </summary>
    private bool AskBeforeClosing()
    {
        if (askingToClose)
            return false;
        if (!unsaved.Busy() && unsaved.Files().Count == 0)
            return true;
        Closing = AskThenClose(null, () => window?.CloseWithoutAsking());
        return false;
    }

    /// <summary>A tab is about to close: as the window, for the files it has unsaved and the viewers it opened.</summary>
    private void ConfirmTabClose(ExplorerViewModel tab, Action close)
    {
        if (askingToClose)
            return;
        Closing = AskThenClose(tab, () =>
        {
            // What the tab opened goes with it, its changes saved or let go by now.
            if (ReferenceEquals(TextViewerTab, tab))
                TextViewer?.CloseWithoutAsking();
            if (ReferenceEquals(ModelViewerTab, tab))
                ModelViewer?.CloseWithoutAsking();
            close();
        });
    }

    /// <summary>
    /// Waits for the saves under way in <paramref name="tab"/> (or anywhere), then asks about what is still unsaved
    /// and runs <paramref name="close"/> once it is saved or let go: at once with nothing unsaved. A save that fails
    /// stops the closing, and its editor is brought forward, where it says why; nothing is let go.
    /// </summary>
    private async Task AskThenClose(ExplorerViewModel? tab, Action close)
    {
        askingToClose = true;
        try
        {
            await unsaved.WhenIdle(tab);
            IReadOnlyList<UnsavedFile> files = unsaved.Files(tab);
            if (files.Count == 0)
            {
                close();
                return;
            }
            if (window?.ExplorerScreenHost is not { } anchor)
                return;
            var prompt = new UnsavedFilesDialog(files, tab is null ? "HikariHopper closes" : "the tab closes");
            UnsavedPrompt = prompt;
            UnsavedChoice choice = await prompt.Ask(anchor);
            if (choice == UnsavedChoice.Cancel)
                return;
            if (choice == UnsavedChoice.Save)
            {
                UnsavedFile? failed = await UnsavedWork.SaveAll(files);
                prompt.Close();
                if (failed is not null)
                {
                    failed.BringForward();
                    return;
                }
            }
            close();
        }
        finally
        {
            UnsavedPrompt = null;
            askingToClose = false;
        }
    }
}
