using System.ComponentModel;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The grid's sort header (port of <c>EntryGridHeader.qml</c>); its DataContext is the tab's view model.</summary>
public partial class EntryGridHeader : UserControl
{
    private ExplorerViewModel? bridge;

    public EntryGridHeader() => InitializeComponent();

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

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
        if (e.PropertyName is nameof(ExplorerViewModel.SortColumn) or nameof(ExplorerViewModel.SortAscending))
            Refresh();
    }

    private void Refresh()
    {
        string column = Bridge?.SortColumn ?? "";
        bool ascending = Bridge?.SortAscending ?? true;
        NameHeader.SortActive = column == "name";
        TypeHeader.SortActive = column == "type";
        SizeHeader.SortActive = column == "size";
        NameHeader.SortAscending = TypeHeader.SortAscending = SizeHeader.SortAscending = ascending;
    }

    private void OnSortName(object? sender, EventArgs e) => Bridge?.SortEntries("name");

    private void OnSortType(object? sender, EventArgs e) => Bridge?.SortEntries("type");

    private void OnSortSize(object? sender, EventArgs e) => Bridge?.SortEntries("size");
}
