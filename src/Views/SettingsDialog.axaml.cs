using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia.Controls;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Settings, shown as a sheet over the main window, a group at a time: Games lists every installation of the
/// games in <see cref="GameCatalog"/>, bound to the shared <see cref="Core.GamePathSettings"/>; Startup has what
/// the app does when it starts, and the start page's recent games; Updates has the version, Check now and the
/// check at startup.
/// Every change is saved as it is made.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsDialog : ModalSheet
{
    private readonly ExplorerTabsViewModel tabs;
    private Func<Control, Task>? checkForUpdatesNow;

    public SettingsDialog()
        : this(new ExplorerTabsViewModel())
    {
    }

    public SettingsDialog(ExplorerTabsViewModel tabs)
    {
        this.tabs = tabs;
        InitializeComponent();

        BuildGameRows();
        OpenLastGame.Toggled += (_, _) => GameSettings.OpenLastGameOnStartup = OpenLastGame.IsOn;
        CheckForUpdates.Toggled += (_, _) => Infrastructure.Updates.CheckOnStartup = CheckForUpdates.IsOn;
        ClearRecents.Click += (_, _) => tabs.ClearRecentGames();
        CheckNow.IsEnabled = false;
        CheckNow.Click += async (_, _) =>
        {
            if (CheckForUpdatesNow is { } check)
                await check(this);
            RefreshUpdates();
        };
        foreach (SettingsGroupRow group in Groups)
            group.Chosen += (_, _) => ShowGroup(group);
        GamePaths.PathsChanged += (_, _) => RefreshGameRows();
        tabs.PropertyChanged += OnTabsChanged;
        Refresh();
    }

    public GamePathSettings GamePaths => tabs.GamePathSettings;

    /// <summary>
    /// Checks for updates by hand, over the sheet it is given; the menu row sets it, and Check now is off without it.
    /// </summary>
    internal Func<Control, Task>? CheckForUpdatesNow
    {
        get => checkForUpdatesNow;
        set
        {
            checkForUpdatesNow = value;
            CheckNow.IsEnabled = value is not null;
        }
    }

    /// <summary>The rail's groups, top to bottom.</summary>
    internal IReadOnlyList<SettingsGroupRow> Groups => [GamesGroup, StartupGroup, UpdatesGroup];

    /// <summary>The group shown, by its label.</summary>
    internal string Group => Groups.First(group => group.Selected).Label;

    /// <summary>Shows <paramref name="group"/>'s page and marks it in the rail.</summary>
    internal void ShowGroup(SettingsGroupRow group)
    {
        foreach (SettingsGroupRow each in Groups)
            each.Selected = each == group;
        GamesPage.IsVisible = group == GamesGroup;
        StartupPage.IsVisible = group == StartupGroup;
        UpdatesPage.IsVisible = group == UpdatesGroup;
    }

    /// <summary>The installation rows, in catalogue order.</summary>
    internal IReadOnlyList<GamePathRow> Rows =>
        [.. GameRows.Children.OfType<SettingsCard>().SelectMany(card => card.Rows.OfType<GamePathRow>())];

    protected override void OnPresented() => Refresh();

    private void OnTabsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.RecentGames))
            RefreshRecents();
    }

    private void BuildGameRows()
    {
        // A card per game, under its name.
        foreach (SupportedGame game in GameCatalog.Games)
        {
            GameRows.Children.Add(new TextBlock
            {
                Text = game.Title.ToUpperInvariant(),
                Margin = new Avalonia.Thickness(0, GameRows.Children.Count == 0 ? 2 : 22, 0, 8),
                Classes = { "section" },
            });
            var card = new SettingsCard();
            GameRows.Children.Add(card);
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
                card.Rows.Add(row);
            }
        }
        RefreshGameRows();
    }

    private void Refresh()
    {
        GamePaths.Refresh();
        RefreshGameRows();
        OpenLastGame.IsOn = GameSettings.OpenLastGameOnStartup;
        RefreshRecents();
        RefreshUpdates();
    }

    private void RefreshUpdates()
    {
        CheckForUpdates.IsOn = Infrastructure.Updates.CheckOnStartup;
        VersionRow.Title = $"HikariHopper {StartPage.AppVersion}";
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
        RecentRow.Detail = count switch
        {
            0 => "None",
            1 => "1 game",
            _ => $"{count} games",
        };
        ClearRecents.IsEnabled = count > 0;
    }
}
