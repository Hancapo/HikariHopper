using System.ComponentModel;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The explorer status bar (port of <c>StatusBar.qml</c>); its DataContext is the active tab's view model.</summary>
public partial class StatusBar : UserControl
{
    private ExplorerViewModel? bridge;

    public StatusBar()
    {
        InitializeComponent();
        FaceSwitch.Sides.Add(new Controls.SwitchSide { Icon = "folder", Width = 28, IconSize = 13, Label = "Files", ToolTip = "Files  F6" });
        FaceSwitch.Sides.Add(new Controls.SwitchSide { Icon = "globe", Width = 28, IconSize = 13, Label = "World", ToolTip = "World  F6" });
        FaceSwitch.RefreshSides();
        FaceSwitch.SideRequested += (_, index) =>
        {
            if (index == 1)
                Bridge?.ShowWorld();
            else
                Bridge?.ShowFiles();
        };
        Avalonia.Automation.AutomationProperties.SetName(FaceSwitch, "Files or world");
        Refresh();
    }

    /// <summary>The status message shown after the count, or "" when there is none worth showing.</summary>
    public string VisibleStatus => StatusText.IsVisible ? StatusText.Text ?? "" : "";

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>
    /// The load confirmation only repeats what the game's well already says, so it is not shown; every
    /// other message is.
    /// </summary>
    public static bool StatusWorthShowing(string status, string edition) =>
        status.Length > 0 && (edition.Length == 0 || status != $"Loaded {edition}");

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        Refresh();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.Status) or nameof(ExplorerViewModel.GameEdition)
            or nameof(ExplorerViewModel.SelectionCount) or nameof(ExplorerViewModel.SelectedSize) or nameof(ExplorerViewModel.InWorld)
            or nameof(ExplorerViewModel.HasWorld) or nameof(ExplorerViewModel.ShowsItemCount) or nameof(ExplorerViewModel.HasWorkspace))
            Refresh();
    }

    private void Refresh()
    {
        string edition = Bridge?.GameEdition ?? "";
        bool status = StatusWorthShowing(Bridge?.Status ?? "", edition);
        Edition.IsVisible = edition.Length > 0;
        EditionMark.Edition = Bridge?.GameEditionId ?? "";
        // Without a game's well the count starts the line, at the bar's own margin.
        Count.Margin = new Avalonia.Thickness(Edition.IsVisible ? 0 : 6, 0, 0, 0);
        StatusDivider.IsVisible = StatusText.IsVisible = status;
        SelectionText.Text = Selection(Bridge?.SelectionCount ?? 0, Bridge?.SelectedSize ?? "");
        // The tab's sides, for a game with a world; on its World side, what the world shows in the count's place, and
        // nothing of the files'.
        bool world = Bridge?.InWorld == true;
        FaceSwitch.IsVisible = Bridge?.HasWorld == true;
        FaceSwitch.SelectedIndex = world ? 1 : 0;
        ItemCountText.IsVisible = Bridge?.ShowsItemCount == true && !world;
        WorldText.IsVisible = world;
        if (world)
        {
            StatusDivider.IsVisible = StatusText.IsVisible = false;
            SelectionText.Text = "";
        }
    }

    /// <summary>"3 selected · 1.2 MB", or "" with nothing selected.</summary>
    public static string Selection(int count, string size) =>
        count == 0 ? "" : size.Length > 0 ? $"{count} selected · {size}" : $"{count} selected";
}
