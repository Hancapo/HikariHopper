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
            or nameof(ExplorerViewModel.SelectionCount) or nameof(ExplorerViewModel.SelectedSize))
            Refresh();
    }

    private void Refresh()
    {
        string edition = Bridge?.GameEdition ?? "";
        bool status = StatusWorthShowing(Bridge?.Status ?? "", edition);
        Edition.IsVisible = edition.Length > 0;
        EditionMark.Enhanced = edition == "GTA V Enhanced";
        // Without a game's well the count starts the line, at the bar's own margin.
        Count.Margin = new Avalonia.Thickness(Edition.IsVisible ? 0 : 6, 0, 0, 0);
        StatusDivider.IsVisible = StatusText.IsVisible = status;
        SelectionText.Text = Selection(Bridge?.SelectionCount ?? 0, Bridge?.SelectedSize ?? "");
    }

    /// <summary>"3 selected · 1.2 MB", or "" with nothing selected.</summary>
    public static string Selection(int count, string size) =>
        count == 0 ? "" : size.Length > 0 ? $"{count} selected · {size}" : $"{count} selected";
}
