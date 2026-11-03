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
    private SettingsWindow? settingsWindow;
    private ShortcutsWindow? shortcutsWindow;
    private AboutWindow? aboutWindow;

    public MenuRow()
    {
        InitializeComponent();
        Bind(FileButton, "FileMenu");
        Bind(EditButton, "EditMenu");
        Bind(ViewButton, "ViewMenu");
        Bind(GoButton, "GoMenu");
        Bind(HelpButton, "HelpMenu");

        WindowShortcuts.Register(this,
            new WindowShortcut("Ctrl+T", () => Tabs?.NewTab()),
            new WindowShortcut("Ctrl+O", () => Tabs is { } tabs && ConfiguredGameAvailable(tabs), () => Bridge?.OpenConfiguredGame("")),
            new WindowShortcut("Ctrl+Shift+O", () => _ = Bridge?.OpenArchiveDialog()),
            new WindowShortcut("Ctrl+W", () => Tabs?.CloseActiveTab()),
            new WindowShortcut("Ctrl+C", () => Bridge?.HasSelection == true, () => Bridge?.CopySelectedName()),
            new WindowShortcut("Ctrl+Shift+C", () => Bridge?.HasSelection == true, () => Bridge?.CopySelectedPath()));
    }

    /// <summary>QML <c>searchRequested</c>: Edit › Search current location….</summary>
    public event EventHandler? SearchRequested;

    private ExplorerTabsViewModel? Tabs => DataContext as ExplorerTabsViewModel;

    private ExplorerViewModel? Bridge => Tabs?.ActiveBridge;

    /// <summary>The menu shown under <paramref name="button"/>.</summary>
    internal RetroMenu MenuFor(MenuBarButton button) => menus[button];

    private static bool ConfiguredGameAvailable(ExplorerTabsViewModel tabs) =>
        tabs.GamePathSettings.EnhancedPathValid || tabs.GamePathSettings.LegacyPathValid;

    private void Bind(MenuBarButton button, string key)
    {
        var menu = (RetroMenu)Resources[key]!;
        menus[button] = menu;
        menu.Opened += (_, _) => button.MenuOpen = true;
        menu.Closed += (_, _) => button.MenuOpen = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (RetroMenu menu in menus.Values)
            menu.DataContext = DataContext;
    }

    private void OnMenuButton(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuBarButton button)
            return;
        // The menu hangs from its title: its frame starts at the title's bottom-left corner and leaves
        // the title's width open on top, so the two read as one surface. The popup is anchored to the
        // window, since a popup anchored to a control ignores a placement rectangle outside it.
        RetroMenu menu = menus[button];
        menu.ConnectionWidth = button.Bounds.Width;
        Control target = TopLevel.GetTopLevel(this) ?? (Control)this;
        Point at = button.TranslatePoint(new Point(PopupOpticalOffset, button.Bounds.Height), target) ?? default;
        menu.Open(target, at.X, at.Y);
    }

    private void OnNewTab(object? sender, RoutedEventArgs e) => Tabs?.NewTab();

    private void OnOpenConfiguredGame(object? sender, RoutedEventArgs e) => Bridge?.OpenConfiguredGame("");

    private void OnOpenArchive(object? sender, RoutedEventArgs e) => _ = Bridge?.OpenArchiveDialog();

    private void OnCloseArchive(object? sender, RoutedEventArgs e) => Bridge?.CloseArchive();

    private void OnCloseTab(object? sender, RoutedEventArgs e) => Tabs?.CloseActiveTab();

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        // Qt.quit()
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

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

    /// <summary>Shows the Settings window, for the menu and the start page's links.</summary>
    internal void OpenSettings()
    {
        if (Tabs is not { } tabs)
            return;
        settingsWindow ??= new SettingsWindow(tabs.GamePathSettings);
        ShowToolWindow(settingsWindow);
    }

    private void OnListView(object? sender, RoutedEventArgs e) => Bridge?.SetViewMode("list");

    private void OnGridView(object? sender, RoutedEventArgs e) => Bridge?.SetViewMode("grid");

    private void OnFoldersToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is RetroMenuItem item)
            Bridge?.SetFoldersVisible(item.IsChecked);
    }

    private void OnBack(object? sender, RoutedEventArgs e) => Bridge?.GoBack();

    private void OnForward(object? sender, RoutedEventArgs e) => Bridge?.GoForward();

    private void OnUp(object? sender, RoutedEventArgs e) => Bridge?.GoUp();

    private void OnShortcuts(object? sender, RoutedEventArgs e) => ShowToolWindow(shortcutsWindow ??= new ShortcutsWindow());

    private void OnAbout(object? sender, RoutedEventArgs e) => ShowToolWindow(aboutWindow ??= new AboutWindow());

    private void ShowToolWindow(Window window)
    {
        // QML show() on a window that is already up leaves it as it is.
        if (window.IsVisible)
            return;
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }
}
