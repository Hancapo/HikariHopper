using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia.Controls;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Settings, shown as a sheet over the main window, a group at a time. Games lists every installation of
/// the games in <see cref="GameCatalog"/>, bound to the shared <see cref="Core.GamePathSettings"/>; Startup
/// has what the app does when it starts, and the start page's recent games. Every change is saved as it is made.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsDialog : ModalSheet
{
    private readonly ExplorerTabsViewModel tabs;

    public SettingsDialog()
        : this(new ExplorerTabsViewModel())
    {
    }

    public SettingsDialog(ExplorerTabsViewModel tabs)
    {
        this.tabs = tabs;
        InitializeComponent();

        BuildGameRows();
        OpenLastGame.IsCheckedChanged += (_, _) => GameSettings.OpenLastGameOnStartup = OpenLastGame.IsChecked == true;
        CheckForUpdates.IsCheckedChanged += (_, _) => Infrastructure.Updates.CheckOnStartup = CheckForUpdates.IsChecked == true;
        ClearRecents.Click += (_, _) => tabs.ClearRecentGames();
        GamesGroup.Chosen += (_, _) => ShowGroup(GamesGroup);
        StartupGroup.Chosen += (_, _) => ShowGroup(StartupGroup);
        GamePaths.PathsChanged += (_, _) => RefreshGameRows();
        tabs.PropertyChanged += OnTabsChanged;
        Refresh();
    }

    public GamePathSettings GamePaths => tabs.GamePathSettings;

    /// <summary>The group shown: "Games" or "Startup".</summary>
    internal string Group => StartupGroup.Selected ? StartupGroup.Label : GamesGroup.Label;

    /// <summary>Shows <paramref name="group"/>'s page and marks it in the rail.</summary>
    internal void ShowGroup(SettingsGroupRow group)
    {
        GamesGroup.Selected = group == GamesGroup;
        StartupGroup.Selected = group == StartupGroup;
        GamesPage.IsVisible = GamesGroup.Selected;
        StartupPage.IsVisible = StartupGroup.Selected;
    }

    /// <summary>The installation rows, in catalogue order.</summary>
    internal IReadOnlyList<GamePathRow> Rows => [.. GameRows.Children.OfType<GamePathRow>()];

    protected override void OnPresented() => Refresh();

    private void OnTabsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.RecentGames))
            RefreshRecents();
    }

    private void BuildGameRows()
    {
        foreach (SupportedGame game in GameCatalog.Games)
        {
            GameRows.Children.Add(new TextBlock
            {
                Text = game.Title.ToUpperInvariant(),
                Margin = new Avalonia.Thickness(10, 8, 10, 2),
                Classes = { "section" },
            });
            foreach (GameEditionInfo edition in game.Editions)
            {
                var row = new GamePathRow
                {
                    Title = edition.Title,
                    Edition = edition.Id,
                    ExpectedExecutable = GameSettings.GameExecutable(edition.Id),
                };
                string id = edition.Id;
                row.PathCommitted += (_, path) => GamePaths.SetPath(id, path);
                row.BrowseRequested += async (_, _) => await GamePaths.Browse(id);
                GameRows.Children.Add(row);
            }
        }
        RefreshGameRows();
    }

    private void Refresh()
    {
        GamePaths.Refresh();
        RefreshGameRows();
        OpenLastGame.IsChecked = GameSettings.OpenLastGameOnStartup;
        CheckForUpdates.IsChecked = Infrastructure.Updates.CheckOnStartup;
        RefreshRecents();
    }

    private void RefreshGameRows()
    {
        foreach (GamePathRow row in Rows)
        {
            row.GamePath = GamePaths.PathFor(row.Edition);
            row.PathValid = GamePaths.IsValid(row.Edition);
        }
    }

    private void RefreshRecents()
    {
        int count = tabs.RecentGames.Count;
        RecentText.Text = count switch
        {
            0 => "No recent games",
            1 => "1 recent game on the start page",
            _ => $"{count} recent games on the start page",
        };
        ClearRecents.IsEnabled = count > 0;
    }
}
