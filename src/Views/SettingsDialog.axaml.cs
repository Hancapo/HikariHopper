using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia.Controls;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Settings, shown as a sheet over the main window, a group at a time: Games lists every installation of the
/// games in <see cref="GameCatalog"/>, bound to the shared <see cref="Core.GamePathSettings"/>; Graphics has how the 3D
/// views draw (<see cref="GraphicsSettings"/>), the same in every viewer; Startup has what
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
        BuildGraphics();
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
    internal IReadOnlyList<SettingsGroupRow> Groups => [GamesGroup, GraphicsGroup, StartupGroup, UpdatesGroup];

    /// <summary>The group shown, by its label.</summary>
    internal string Group => Groups.First(group => group.Selected).Label;

    /// <summary>Shows <paramref name="group"/>'s page and marks it in the rail.</summary>
    internal void ShowGroup(SettingsGroupRow group)
    {
        foreach (SettingsGroupRow each in Groups)
            each.Selected = each == group;
        GamesPage.IsVisible = group == GamesGroup;
        GraphicsPage.IsVisible = group == GraphicsGroup;
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

    // The graphics page's switches: each side of Shadows past Off a resolution, each of Texture filtering an anisotropy.
    private void BuildGraphics()
    {
        AntialiasingSwitch.Toggled += (_, _) => GraphicsSettings.Antialiasing = AntialiasingSwitch.IsOn;
        BloomSwitch.Toggled += (_, _) => GraphicsSettings.Bloom = BloomSwitch.IsOn;
        ShadowSwitch.Sides.Add(new Controls.SwitchSide { Text = "Off", Width = 44, Label = "No shadows" });
        foreach ((string name, int resolution) in new[] { "Medium", "High", "Ultra" }.Zip(GraphicsSettings.ShadowResolutions))
            ShadowSwitch.Sides.Add(new Controls.SwitchSide
            {
                Text = name,
                Width = name.Length * 8 + 16,
                Label = $"{name} shadows",
                ToolTip = $"{name}: shadow maps {resolution} texels a side",
            });
        ShadowSwitch.RefreshSides();
        ShadowSwitch.SideRequested += (_, side) =>
        {
            if (side > 0)
                GraphicsSettings.ShadowResolution = GraphicsSettings.ShadowResolutions[side - 1];
            GraphicsSettings.Shadows = side > 0;
            RefreshGraphics();
        };
        foreach (int anisotropy in GraphicsSettings.Anisotropies)
            FilteringSwitch.Sides.Add(new Controls.SwitchSide
            {
                Text = anisotropy == 1 ? "Trilinear" : $"{anisotropy}×",
                Width = anisotropy == 1 ? 72 : 40,
                Label = anisotropy == 1 ? "Trilinear filtering" : $"{anisotropy}× anisotropic filtering",
            });
        FilteringSwitch.RefreshSides();
        FilteringSwitch.SideRequested += (_, side) =>
        {
            GraphicsSettings.Anisotropy = GraphicsSettings.Anisotropies[side];
            RefreshGraphics();
        };
        Avalonia.Automation.AutomationProperties.SetName(AntialiasingSwitch, "Anti-aliasing");
        Avalonia.Automation.AutomationProperties.SetName(BloomSwitch, "Bloom");
        Avalonia.Automation.AutomationProperties.SetName(ShadowSwitch, "Shadows");
        Avalonia.Automation.AutomationProperties.SetName(FilteringSwitch, "Texture filtering");
    }

    private void RefreshGraphics()
    {
        AntialiasingSwitch.IsOn = GraphicsSettings.Antialiasing;
        BloomSwitch.IsOn = GraphicsSettings.Bloom;
        ShadowSwitch.SelectedIndex = GraphicsSettings.Shadows
            ? 1 + IndexOf(GraphicsSettings.ShadowResolutions, GraphicsSettings.ShadowResolution) : 0;
        FilteringSwitch.SelectedIndex = IndexOf(GraphicsSettings.Anisotropies, GraphicsSettings.Anisotropy);

        static int IndexOf(IReadOnlyList<int> values, int value) => Math.Max(0, values.ToList().IndexOf(value));
    }

    private void Refresh()
    {
        GamePaths.Refresh();
        RefreshGraphics();
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
