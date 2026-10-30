using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A tab with a workspace (port of <c>WorkspaceView.qml</c>); its DataContext is the tab's view model.
/// The entries pane is an <see cref="EntryTable"/> or an <see cref="EntryGrid"/> by <see cref="ExplorerViewModel.ViewMode"/>.
/// </summary>
public partial class WorkspaceView : UserControl
{
    private readonly ExplorerContextMenu contextMenu = new();
    private ExplorerViewModel? bridge;
    private ExplorerCreateDialog? createDialog;
    private DeleteEntriesDialog? deleteDialog;
    private GridLength folderWidth = new(FolderPanel.PreferredWidth);

    public WorkspaceView()
    {
        InitializeComponent();
        Split.ColumnDefinitions[0].MinWidth = FolderPanel.MinimumWidth;
        DropArea.Attach(EntriesArea);
        contextMenu.CreateRequested += (_, request) => OpenCreateDialog(request.Kind, request.SuggestedName, request.SourcePath);
    }

    /// <summary>The current entries view (table or grid).</summary>
    public Control? EntriesView => EntriesHost.Content as Control;

    internal ExplorerContextMenu EntryContextMenu => contextMenu;

    internal ExplorerCreateDialog? CreateDialog => createDialog;

    internal DeleteEntriesDialog? DeleteDialog => deleteDialog;

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>QML <c>focusSearch()</c>.</summary>
    public void FocusSearch() => Navigation.FocusSearch();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
        {
            bridge.PropertyChanged -= OnBridgePropertyChanged;
            bridge.DeleteConfirmationRequested -= OnDeleteConfirmationRequested;
        }
        bridge = Bridge;
        if (bridge is not null)
        {
            bridge.PropertyChanged += OnBridgePropertyChanged;
            bridge.DeleteConfirmationRequested += OnDeleteConfirmationRequested;
        }
        contextMenu.DataContext = bridge;
        RefreshFolders();
        RefreshEntriesView();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.ViewMode))
            RefreshEntriesView();
        else if (e.PropertyName == nameof(ExplorerViewModel.FoldersVisible))
            RefreshFolders();
    }

    /// <summary>SplitView hides a hidden item's handle; the panel keeps its width for when it returns.</summary>
    private void RefreshFolders()
    {
        bool visible = Bridge?.FoldersVisible != false;
        ColumnDefinition column = Split.ColumnDefinitions[0];
        if (!visible && Folders.IsVisible)
            folderWidth = column.Width;
        Folders.IsVisible = visible;
        Handle.IsVisible = visible;
        column.MinWidth = visible ? FolderPanel.MinimumWidth : 0;
        column.Width = visible ? folderWidth : new GridLength(0);
    }

    /// <summary>The Loader: <c>viewMode === "grid" ? gridComponent : listComponent</c>.</summary>
    private void RefreshEntriesView()
    {
        bool grid = Bridge?.ViewMode == "grid";
        if (grid && EntriesHost.Content is EntryGrid || !grid && EntriesHost.Content is EntryTable)
            return;
        switch (EntriesHost.Content)
        {
            case EntryTable table:
                table.ContextMenuRequested -= OnContextMenuRequested;
                break;
            case EntryGrid oldGrid:
                oldGrid.ContextMenuRequested -= OnContextMenuRequested;
                break;
        }
        if (grid)
        {
            var view = new EntryGrid();
            view.ContextMenuRequested += OnContextMenuRequested;
            EntriesHost.Content = view;
        }
        else
        {
            var view = new EntryTable();
            view.ContextMenuRequested += OnContextMenuRequested;
            EntriesHost.Content = view;
        }
    }

    private void OnContextMenuRequested(object? sender, ContextMenuRequest request) =>
        OpenContextMenu(request.Source, request.Point);

    /// <summary>QML <c>openContextMenu(source, localX, localY)</c>.</summary>
    public void OpenContextMenu(Control source, Point point)
    {
        Point mapped = source.TranslatePoint(point, this) ?? point;
        contextMenu.Open(this, mapped.X, mapped.Y);
    }

    /// <summary>QML <c>openCreateDialog(kind, suggestedName, sourcePath)</c>.</summary>
    public void OpenCreateDialog(string kind, string suggestedName, string sourcePath)
    {
        if (Bridge is not { } model || createDialog is not null)
            return;
        var dialog = new ExplorerCreateDialog(model);
        createDialog = dialog;
        // onClosed: Qt.callLater(function() { createDialogLoader.active = false })
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (createDialog == dialog)
                createDialog = null;
        });
        dialog.Begin(this, kind, suggestedName, sourcePath);
    }

    private void OnDeleteConfirmationRequested(object? sender, EventArgs e) => OpenDeleteDialog();

    /// <summary>QML <c>openDeleteDialog()</c>.</summary>
    public void OpenDeleteDialog()
    {
        if (Bridge is not { } model || deleteDialog is not null)
            return;
        var dialog = new DeleteEntriesDialog(model);
        deleteDialog = dialog;
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (deleteDialog == dialog)
                deleteDialog = null;
        });
        dialog.Open(this);
    }
}
