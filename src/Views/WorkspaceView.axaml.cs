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
    /// <summary>The 8px gutters around and between the floating panels.</summary>
    public static readonly Thickness PanelMargin = new(Ui.Theme.PanelGutter);

    private readonly ExplorerContextMenu contextMenu = new();
    private ExplorerViewModel? bridge;
    private ExplorerCreateDialog? createDialog;
    private DeleteEntriesDialog? deleteDialog;
    private PropertiesDialog? propertiesDialog;
    private GridLength folderWidth = new(FolderPanel.PreferredWidth);

    public WorkspaceView()
    {
        InitializeComponent();
        Split.ColumnDefinitions[0].MinWidth = FolderPanel.MinimumWidth;
        DropArea.Attach(EntriesArea);
        contextMenu.CreateRequested += (_, request) => OpenCreateDialog(request.Kind, request.SuggestedName, request.SourcePath);
        // Enter or Down in the search query moves to the results.
        Navigation.ResultsFocusRequested += (_, _) => (EntriesView as EntryTable)?.FocusTarget.Focus(Avalonia.Input.NavigationMethod.Tab);
        WindowShortcuts.Register(this, new WindowShortcut("Ctrl+Enter",
            () => Bridge is { IsSearching: true, SelectionCount: 1 } && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox,
            () => Bridge!.OpenSelectedLocation()));
        // F2 renames the one selected item, unless a text field is being typed in.
        WindowShortcuts.Register(this, new WindowShortcut("F2",
            () => Bridge?.CanRenameSelection == true && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox,
            () => OpenCreateDialog("rename", Bridge!.RenameSource, "")));
        // Alt+Enter shows the one selected item's properties, as in Windows Explorer.
        contextMenu.PropertiesRequested += (_, _) => _ = OpenPropertiesDialog();
        WindowShortcuts.Register(this, new WindowShortcut("Alt+Enter",
            () => Bridge?.CanShowProperties == true && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox,
            () => _ = OpenPropertiesDialog()));
    }

    /// <summary>The current entries view (table or grid).</summary>
    public Control? EntriesView => EntriesHost.Content as Control;

    internal ExplorerContextMenu EntryContextMenu => contextMenu;

    internal ExplorerCreateDialog? CreateDialog => createDialog;

    internal DeleteEntriesDialog? DeleteDialog => deleteDialog;

    internal PropertiesDialog? PropertiesSheet => propertiesDialog;

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
        {
            RefreshEntriesView();
        }
        else if (e.PropertyName == nameof(ExplorerViewModel.FoldersVisible))
        {
            RefreshFolders();
        }
        else if (e.PropertyName == nameof(ExplorerViewModel.HasSearchQuery))
        {
            // Results take the whole area, folder panel included, and always as a list: they need its LOCATION.
            // Only once there is a query: the search row open on its own leaves the folders as they were.
            RefreshFolders();
            RefreshEntriesView();
        }
    }

    /// <summary>SplitView hides a hidden item's handle; the panel keeps its width for when it returns.</summary>
    private void RefreshFolders()
    {
        bool visible = Bridge?.FoldersVisible != false && Bridge?.HasSearchQuery != true;
        ColumnDefinition column = Split.ColumnDefinitions[0];
        if (!visible && FoldersFrame.IsVisible)
            folderWidth = column.Width;
        FoldersFrame.IsVisible = visible;
        Handle.IsVisible = visible;
        Split.ColumnDefinitions[1].Width = new GridLength(visible ? Ui.Theme.PanelGutter : 0);
        column.MinWidth = visible ? FolderPanel.MinimumWidth : 0;
        column.Width = visible ? folderWidth : new GridLength(0);
    }

    /// <summary>The Loader: <c>viewMode === "grid" ? gridComponent : listComponent</c>.</summary>
    private void RefreshEntriesView()
    {
        bool grid = Bridge?.ViewMode == "grid" && Bridge?.HasSearchQuery != true;
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
        // Search results have no folder of their own to make things in or paste into.
        if (Bridge is { IsSearching: true, SelectionCount: 0 })
            return;
        Point mapped = source.TranslatePoint(point, this) ?? point;
        contextMenu.Refresh();
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

    /// <summary>Reads the selected item's properties, then shows them in a sheet.</summary>
    public async Task OpenPropertiesDialog()
    {
        if (Bridge is not { } model || propertiesDialog is not null)
            return;
        if (await model.ReadSelectionProperties() is not { } properties || propertiesDialog is not null
            || TopLevel.GetTopLevel(this) is not { } host || ModalOverlay.IsOpen(host))
            return;
        var dialog = new PropertiesDialog(properties);
        propertiesDialog = dialog;
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (propertiesDialog == dialog)
                propertiesDialog = null;
        });
        dialog.Present(this);
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
