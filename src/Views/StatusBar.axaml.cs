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

    /// <summary>The status message shown after the lamp, or "" when there is none worth showing.</summary>
    public string VisibleStatus => StatusText.IsVisible ? StatusText.Text ?? "" : "";

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>
    /// The load confirmation only repeats what the lamp already says, so it is not shown; every
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
        if (e.PropertyName is nameof(ExplorerViewModel.Status) or nameof(ExplorerViewModel.GameEdition) or nameof(ExplorerViewModel.IsSearching))
            Refresh();
    }

    private void Refresh()
    {
        string edition = Bridge?.GameEdition ?? "";
        bool status = StatusWorthShowing(Bridge?.Status ?? "", edition);
        Edition.IsVisible = edition.Length > 0;
        // While searching the item count is the tab's, so nothing stands before the lamp to divide from.
        EditionDivider.IsVisible = Edition.IsVisible && Bridge?.IsSearching != true;
        StatusDivider.IsVisible = StatusText.IsVisible = status;
    }
}
