using Avalonia.Controls;
using HikariHopper.Infrastructure;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Opens folders in tabs of their own with a game: the game of the tab they're opened from or dropped on, or, with
/// none to go by, the one chosen in <see cref="GameChoiceDialog"/> (the one game set up, without asking).
/// </summary>
internal static class FolderOpening
{
    /// <summary>The dialog asking for the game, while it's open; for tests.</summary>
    internal static GameChoiceDialog? Asking { get; private set; }

    /// <summary>
    /// Opens <paramref name="folders"/> after the tab at <paramref name="after"/> (-1 for the end) with
    /// <paramref name="with"/>'s game, or, when it has none, the game asked for over <paramref name="anchor"/>.
    /// </summary>
    public static void Open(Control anchor, ExplorerTabsViewModel tabs, IReadOnlyList<string> folders, ExplorerViewModel? with, int after)
    {
        string? game = with is { HasGame: true } ? with.GamePath : null;
        if (tabs.OpenFolders(folders, game, after) is not { } games)
            return;
        string name = folders.Count == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(folders[0])) : $"{folders.Count} folders";
        var dialog = new GameChoiceDialog(name, games);
        dialog.Chosen += (_, root) => tabs.OpenFolders(folders, root, after);
        Asking = dialog;
        dialog.DetachedFromVisualTree += (_, _) =>
        {
            if (Asking == dialog)
                Asking = null;
        };
        dialog.Open(anchor);
    }

    /// <summary>File › Open folder…: a folder picked, then opened after the active tab with its game, or the one asked for.</summary>
    public static async Task Pick(Control anchor, ExplorerTabsViewModel tabs)
    {
        ExplorerViewModel active = tabs.ActiveBridge;
        string start = active.HasFolder ? active.FolderPath : active.GamePath;
        string? folder = await AppServices.Dialogs.PickFolderAsync("Open folder", start.Length > 0 ? start : null);
        if (string.IsNullOrEmpty(folder))
            return;
        Open(anchor, tabs, [folder], active, tabs.Tabs.IndexOf(active));
    }
}
