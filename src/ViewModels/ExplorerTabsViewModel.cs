using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>
/// Independent explorer sessions exposed as browser-style tabs: the port of <c>tabs.py::ExplorerTabs</c>.
/// Coordinates writes between tabs that show the same files.
/// </summary>
public sealed class ExplorerTabsViewModel : ObservableObject
{
    private const int MaxRecentGames = 6;

    private static readonly string[] ActiveTabProperties =
        [nameof(ActiveIndex), nameof(ActiveBridge), nameof(ActiveEntryModel), nameof(ActiveTreeModel)];

    private readonly ObservableCollection<ExplorerViewModel> tabs = [];
    private readonly Dictionary<ExplorerViewModel, Action> unsubscribers = [];
    private int activeIndex = -1;

    public ExplorerTabsViewModel() => NewTab();

    /// <summary>The open tabs; each exposes <c>TabTitle</c>, <c>TabActive</c> and <c>TextureViewer</c>.</summary>
    public ObservableCollection<ExplorerViewModel> Tabs => tabs;

    public int ActiveIndex => activeIndex;

    // Clamped: a closing tab leaves the collection just before the index is updated.
    public ExplorerViewModel ActiveBridge => tabs[Math.Clamp(activeIndex, 0, tabs.Count - 1)];

    public GamePathSettings GamePathSettings { get; } = new();

    public ObservableCollection<EntryItem> ActiveEntryModel => ActiveBridge.EntriesModel;

    public ObservableCollection<TreeItem> ActiveTreeModel => ActiveBridge.TreeModel;

    /// <summary>Recently opened installations that still exist, newest first.</summary>
    public IReadOnlyList<RecentGame> RecentGames
    {
        get
        {
            var games = new List<RecentGame>();
            foreach (string path in RecentGamePaths())
            {
                if (!Directory.Exists(path))
                    continue;
                string edition;
                if (File.Exists(Path.Combine(path, "GTA5_Enhanced.exe")))
                    edition = "GTA V Enhanced";
                else if (File.Exists(Path.Combine(path, "GTA5.exe")))
                    edition = "GTA V Legacy";
                else
                    continue;
                games.Add(new RecentGame(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), path, edition));
            }
            return games;
        }
    }

    /// <summary>Whether startup should show the splash while a saved game loads.</summary>
    public static bool StartupSplashRequired() => GameSettings.GameRootToRestore().Length > 0;

    public void RestoreLastGame() => ActiveBridge.RestoreLastGame();

    /// <summary>
    /// Opens <paramref name="path"/> (a path as the address bar takes it) in a new tab on <paramref name="source"/>'s
    /// game or RPF, and shows that tab; <paramref name="source"/> stays as it is.
    /// </summary>
    private void OpenInNewTab(ExplorerViewModel source, string path)
    {
        string game = source.HasGame ? source.GamePath : "";
        string archive = source.HasGame ? "" : source.Provider.ArchivePath;
        int row = NewTab();
        ExplorerViewModel tab = tabs[row];
        if (game.Length > 0)
            tab.OpenGame(game);
        else if (archive.Length > 0)
            tab.OpenArchive(archive);
        tab.GoToAddress(path);
    }

    /// <summary>Opens and activates an empty tab; returns its index.</summary>
    public int NewTab()
    {
        int row = tabs.Count;
        var bridge = new ExplorerViewModel();
        PropertyChangedEventHandler propertyChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ExplorerViewModel.TabTitle))
                WorkspaceChanged(bridge);
            else if (e.PropertyName == nameof(ExplorerViewModel.EntryOperationBusy))
                SynchronizeOperationState();
        };
        EventHandler<string> gameOpened = (_, path) =>
        {
            RememberGame(path);
            GamePathSettings.Refresh();
        };
        EventHandler<IReadOnlyList<string>> looseFiles = (_, paths) => PrepareLooseFileDeletion(bridge, paths);
        EventHandler<object> contentChanged = (_, target) => ContentChanged(bridge, target);
        EventHandler<string> archiveWrite = (_, rootPath) => PrepareArchiveWrite(rootPath);
        EventHandler<string> newTab = (_, path) => OpenInNewTab(bridge, path);
        bridge.PropertyChanged += propertyChanged;
        bridge.GameOpened += gameOpened;
        bridge.LooseFilesAboutToBeDeleted += looseFiles;
        bridge.ContentChanged += contentChanged;
        bridge.ArchiveWriteRequested += archiveWrite;
        bridge.OpenInNewTabRequested += newTab;
        unsubscribers[bridge] = () =>
        {
            bridge.PropertyChanged -= propertyChanged;
            bridge.GameOpened -= gameOpened;
            bridge.LooseFilesAboutToBeDeleted -= looseFiles;
            bridge.ContentChanged -= contentChanged;
            bridge.ArchiveWriteRequested -= archiveWrite;
            bridge.OpenInNewTabRequested -= newTab;
        };
        tabs.Add(bridge);
        SynchronizeOperationState();
        ActivateTab(row);
        return row;
    }

    public void ActivateTab(int row)
    {
        if (row < 0 || row >= tabs.Count || row == activeIndex)
            return;
        int previous = activeIndex;
        activeIndex = row;
        if (previous >= 0)
            tabs[previous].TabActive = false;
        tabs[row].TabActive = true;
        RaiseActiveTabChanged();
    }

    /// <summary>Moves the tab at <paramref name="from"/> to <paramref name="to"/>; the active tab stays active.</summary>
    public void MoveTab(int from, int to)
    {
        if (from < 0 || from >= tabs.Count || to < 0 || to >= tabs.Count || from == to)
            return;
        ExplorerViewModel active = ActiveBridge;
        tabs.Move(from, to);
        int index = tabs.IndexOf(active);
        // Only the index changes; the active workspace is the same, so nothing rebinds.
        if (index != activeIndex)
        {
            activeIndex = index;
            OnPropertyChanged(nameof(ActiveIndex));
        }
    }

    /// <summary>
    /// Asked before a tab closes, with what closes it: the view asks about every file the tab has unsaved, in its
    /// texture editor and in the viewers it opened, and closes it once they are saved or let go. Unset, only the
    /// texture document is asked about.
    /// </summary>
    public Action<ExplorerViewModel, Action>? ConfirmClose { get; set; }

    /// <summary>Closes a tab once its unsaved changes are resolved.</summary>
    public void CloseTab(int row)
    {
        if (row < 0 || row >= tabs.Count)
            return;
        ExplorerViewModel bridge = tabs[row];
        // The view waits for a write under way before it closes the tab.
        if (ConfirmClose is { } confirm)
        {
            confirm(bridge, () => CloseTabCore(bridge));
            return;
        }
        if (bridge.EntryOperationBusy && !bridge.TextureViewer.Saving)
            return;
        bridge.TextureViewer.RequestDocumentChange(() => CloseTabCore(bridge));
    }

    private void CloseTabCore(ExplorerViewModel bridge)
    {
        if (!tabs.Contains(bridge) || bridge.EntryOperationBusy)
            return;
        int row = tabs.IndexOf(bridge);
        bridge.TextureViewer.CloseDocument();
        if (tabs.Count == 1)
        {
            bridge.CloseWorkspace();
            return;
        }
        tabs.RemoveAt(row);
        bridge.TextureViewer.Shutdown();
        bridge.Provider.Close();
        if (unsubscribers.Remove(bridge, out Action? unsubscribe))
            unsubscribe();
        bridge.Release();
        SynchronizeOperationState();
        if (activeIndex > row)
            activeIndex--;
        else if (activeIndex == row)
            activeIndex = Math.Min(row, tabs.Count - 1);
        RaiseActiveTabChanged();
        bridge.TabActive = false;
        tabs[activeIndex].TabActive = true;
    }

    public void CloseActiveTab() => CloseTab(activeIndex);

    public void OpenRecentGame(string path) => ActiveBridge.OpenGameAsync(path);

    /// <summary>Forgets the recent games the launcher lists.</summary>
    public void ClearRecentGames()
    {
        AppSettings.SetStringList(GameSettings.RecentGameRootsKey, []);
        OnPropertyChanged(nameof(RecentGames));
    }

    private void RaiseActiveTabChanged()
    {
        foreach (string name in ActiveTabProperties)
            OnPropertyChanged(name);
    }

    private void WorkspaceChanged(ExplorerViewModel bridge)
    {
        int row = tabs.IndexOf(bridge);
        if (row >= 0 && row == activeIndex)
            RaiseActiveTabChanged();
    }

    private void SynchronizeOperationState()
    {
        var busy = tabs.Where(bridge => bridge.LocalEntryOperationBusy).ToHashSet();
        foreach (ExplorerViewModel bridge in tabs.ToList())
            bridge.SetPeerEntryOperationBusy(busy.Count > 0 && !busy.Contains(bridge));
    }

    private void PrepareLooseFileDeletion(ExplorerViewModel source, IReadOnlyList<string> paths)
    {
        foreach (ExplorerViewModel bridge in tabs.ToList())
        {
            if (bridge != source)
                bridge.PrepareLooseFileDeletion(paths);
        }
    }

    private void PrepareArchiveWrite(string rootPath)
    {
        foreach (ExplorerViewModel bridge in tabs)
            bridge.Provider.PrepareArchiveWrite(rootPath);
    }

    private void ContentChanged(ExplorerViewModel source, object target)
    {
        foreach (ExplorerViewModel bridge in tabs.ToList())
        {
            if (bridge != source)
                bridge.RefreshExternalContent(target);
        }
    }

    private void RememberGame(string path)
    {
        var recent = RecentGamePaths().Where(item => item != path).ToList();
        recent.Insert(0, path);
        AppSettings.SetStringList(GameSettings.RecentGameRootsKey, recent.Take(MaxRecentGames));
        OnPropertyChanged(nameof(RecentGames));
    }

    private static IReadOnlyList<string> RecentGamePaths() => AppSettings.GetStringList(GameSettings.RecentGameRootsKey);
}
