using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The File/Edit/View/Go/Help menu bar (port of <c>MenuRow.qml</c>) with its window shortcuts and
/// the Settings, Keyboard shortcuts and About windows. Its DataContext is the
/// <see cref="ExplorerTabsViewModel"/>; the menus act on <see cref="ExplorerTabsViewModel.ActiveBridge"/>.
/// </summary>
public partial class MenuRow : UserControl
{
    // QML: readonly property int popupOpticalOffset: 0
    private const double PopupOpticalOffset = 0;


    private readonly Dictionary<MenuBarButton, RetroMenu> menus = [];
    private readonly MenuBar? bar;
    // The menu open as a window of its own, over a world, and its title.
    private DropdownWindow? window;
    private MenuBarButton? windowTitle;
    private SettingsDialog? settingsDialog;
    private ShortcutsDialog? shortcutsDialog;
    private AboutDialog? aboutDialog;

    public MenuRow()
    {
        InitializeComponent();
        Updates = new UpdateFlow(this, () => Tabs);
        Bind(FileButton, "FileMenu");
        Bind(EditButton, "EditMenu");
        Bind(ViewButton, "ViewMenu");
        Bind(GoButton, "GoMenu");
        Bind(HelpButton, "HelpMenu");
        // A menu bar's clicks, pointing and keys, as the viewers' bars have them.
        bar = MenuBar.Attach(this, [.. menus.Select(pair => new MenuBar.Title(pair.Key, pair.Value,
            () => pair.Value.IsOpen || windowTitle == pair.Key, () => Open(pair.Key), () => Close(pair.Key)))]);

        WindowShortcuts.Register(this,
            new WindowShortcut("Ctrl+T", () => Tabs?.NewTab()),
            new WindowShortcut("Ctrl+O", () => Tabs is { } tabs && ConfiguredGameAvailable(tabs), () => Bridge?.OpenConfiguredGame("")),
            new WindowShortcut("Ctrl+Shift+O", () => _ = Bridge?.OpenArchiveDialog()),
            new WindowShortcut("Ctrl+W", () => Tabs?.CloseActiveTab()),
            new WindowShortcut("Ctrl+C", () => Bridge?.HasSelection == true, () => _ = Bridge?.CopySelection()),
            new WindowShortcut("Ctrl+V", () => Bridge?.HasWorkspace == true, () => _ = Bridge?.Paste()),
            new WindowShortcut("Ctrl+Shift+C", () => Bridge?.HasSelection == true, () => Bridge?.CopySelectedPath()),
            new WindowShortcut("Ctrl+Shift+E", () => Bridge?.CanShowInExplorer == true, () => Bridge?.ShowInExplorer()),
            // A game with a world: its tab's other side.
            new WindowShortcut("F6", () => Bridge?.HasWorld == true, () => Bridge?.ToggleFace()));
    }

    /// <summary>QML <c>searchRequested</c>: Edit › Search current location….</summary>
    public event EventHandler? SearchRequested;

    private ExplorerTabsViewModel? Tabs => DataContext as ExplorerTabsViewModel;

    private ExplorerViewModel? Bridge => Tabs?.ActiveBridge;

    /// <summary>The menu shown under <paramref name="button"/>.</summary>
    internal RetroMenu MenuFor(MenuBarButton button) => menus[button];

    private static bool ConfiguredGameAvailable(ExplorerTabsViewModel tabs) =>
        tabs.GamePathSettings.AnyPathValid;

    private void Bind(MenuBarButton button, string key)
    {
        var menu = (RetroMenu)Resources[key]!;
        menus[button] = menu;
        menu.Opened += (_, _) => button.MenuOpen = true;
        menu.Closed += (_, _) =>
        {
            button.MenuOpen = false;
            bar?.Closed(button);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (RetroMenu menu in menus.Values)
            menu.DataContext = DataContext;
    }

    private void OnMenuButton(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuBarButton button)
            bar?.Toggle(button);
    }

    /// <summary>Opens or closes a title's menu, as a click on the title does.</summary>
    internal void Toggle(MenuBarButton button) => bar?.Toggle(button);

    /// <summary>The menu open as a window of its own, over a world; none when none is.</summary>
    internal DropdownWindow? OpenWindow => window;

    private void Open(MenuBarButton button)
    {
        RetroMenu menu = menus[button];
        // Over a world, a window of its own, as the viewers' menus are: the world's view is a native window, which
        // would cover a menu drawn in the main window, so it would have to hide while the menu is open.
        if (Bridge?.InWorld == true)
        {
            OpenAsWindow(button, menu);
            return;
        }
        // The menu hangs from its title: its frame starts at the title's bottom-left corner and leaves
        // the title's width open on top, so the two read as one surface. The popup is anchored to the
        // window, since a popup anchored to a control ignores a placement rectangle outside it.
        menu.ConnectionWidth = button.Bounds.Width;
        Control target = TopLevel.GetTopLevel(this) ?? (Control)this;
        Point at = button.TranslatePoint(new Point(PopupOpticalOffset, button.Bounds.Height), target) ?? default;
        menu.Open(target, at.X, at.Y);
    }

    private void OpenAsWindow(MenuBarButton button, RetroMenu menu)
    {
        if (DropdownWindow.OpenMenu(button, menu) is not { } shown)
            return;
        window = shown;
        windowTitle = button;
        button.MenuOpen = true;
        // Its window takes the keyboard: its keys go to the bar first, as the main window's do for a menu drawn in it.
        if (bar is not null)
            shown.AddHandler(KeyDownEvent, bar.OnKeyDown, RoutingStrategies.Tunnel);
        // A menu drawn in the main window closes when one of its items is clicked; one in a window of its own is closed
        // here, once the item has done what it does.
        shown.AddHandler(MenuItem.ClickEvent, (_, _) => shown.Close(), RoutingStrategies.Bubble, handledEventsToo: true);
        shown.Closed += (_, _) =>
        {
            button.MenuOpen = false;
            // Another title's menu may have opened in its place meanwhile.
            if (window != shown)
                return;
            window = null;
            windowTitle = null;
            // Taken from it, by a click on its title among others: that click doesn't open it again.
            bar?.Closed(button);
        };
    }

    private void Close(MenuBarButton button)
    {
        if (windowTitle == button)
            window?.Close();
        else
            menus[button].Close();
    }

    private void OnNewTab(object? sender, RoutedEventArgs e) => Tabs?.NewTab();

    private void OnOpenConfiguredGame(object? sender, RoutedEventArgs e) => Bridge?.OpenConfiguredGame("");

    private void OnOpenArchive(object? sender, RoutedEventArgs e) => _ = Bridge?.OpenArchiveDialog();

    private void OnCloseArchive(object? sender, RoutedEventArgs e) => Bridge?.CloseArchive();

    private void OnCloseTab(object? sender, RoutedEventArgs e) => Tabs?.CloseActiveTab();

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        // As the close button does: the window asks about unsaved files first, and the app ends when it has closed.
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Close();
        else if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void OnCopy(object? sender, RoutedEventArgs e) => _ = Bridge?.CopySelection();

    private void OnPaste(object? sender, RoutedEventArgs e) => _ = Bridge?.Paste();

    private void OnCopyName(object? sender, RoutedEventArgs e) => Bridge?.CopySelectedName();

    private void OnCopyPath(object? sender, RoutedEventArgs e) => Bridge?.CopySelectedPath();

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        // Qt.callLater(bridge.requestDeleteSelection): after the menu has closed.
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => bridge?.RequestDeleteSelection());
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => Bridge?.SelectAllEntries();

    private void OnSearch(object? sender, RoutedEventArgs e) => SearchRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettings(object? sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Shows Settings, for the menu and the start page's links.</summary>
    internal void OpenSettings()
    {
        if (Tabs is not { } tabs)
            return;
        settingsDialog ??= new SettingsDialog(tabs) { CheckForUpdatesNow = over => Updates.CheckByHand(over) };
        settingsDialog.Present(this);
    }

    /// <summary>The sheets this menu row opens, once they have been opened.</summary>
    internal SettingsDialog? Settings => settingsDialog;

    /// <summary>The app's updates: Help's check, and the one at startup.</summary>
    internal UpdateFlow Updates { get; }

    private void OnListView(object? sender, RoutedEventArgs e) => Bridge?.SetViewMode("list");

    private void OnGridView(object? sender, RoutedEventArgs e) => Bridge?.SetViewMode("grid");

    // The item's state says the side, so its own update from the tab doesn't flip it back.
    private void OnWorldToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not RetroMenuItem item)
            return;
        if (item.IsChecked)
            Bridge?.ShowWorld();
        else
            Bridge?.ShowFiles();
    }

    private void OnFoldersToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is RetroMenuItem item)
            Bridge?.SetFoldersVisible(item.IsChecked);
    }

    private void OnBack(object? sender, RoutedEventArgs e) => Bridge?.GoBack();

    private void OnForward(object? sender, RoutedEventArgs e) => Bridge?.GoForward();

    private void OnUp(object? sender, RoutedEventArgs e) => Bridge?.GoUp();

    private void OnShortcuts(object? sender, RoutedEventArgs e) => (shortcutsDialog ??= new ShortcutsDialog()).Present(this);

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => _ = Updates.CheckByHand();

    private void OnAbout(object? sender, RoutedEventArgs e) => (aboutDialog ??= new AboutDialog()).Present(this);
}
