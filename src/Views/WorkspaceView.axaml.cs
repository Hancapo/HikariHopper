using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A tab with a workspace (port of <c>WorkspaceView.qml</c>); its DataContext is the tab's view model.
/// The entries pane is an <see cref="EntryTable"/> or an <see cref="EntryGrid"/> by <see cref="ExplorerViewModel.ShownViewMode"/>.
/// </summary>
public partial class WorkspaceView : UserControl
{
    /// <summary>
    /// The 8px gutters around the floating panels, but for the bottom: the status bar's own height is the gap under
    /// them, so what it shows sits as far from the panels as from the window's edge, and the panels get the room.
    /// </summary>
    public static readonly Thickness PanelMargin = new(Ui.Theme.PanelGutter, Ui.Theme.PanelGutter, Ui.Theme.PanelGutter, 0);

    private readonly ExplorerContextMenu contextMenu = new();
    private ExplorerViewModel? bridge;
    private ExplorerCreateDialog? createDialog;
    private DeleteEntriesDialog? deleteDialog;
    private ImportConflictDialog? importDialog;
    private PropertiesDialog? propertiesDialog;
    private GridLength folderWidth = new(FolderPanel.PreferredWidth);
    private bool foldersShown = true;
    // Counts the folder panel's slides, so a newer one stops an older one's frames.
    private int foldersSlide;

    /// <summary>How long the folder panel takes to slide shut or open.</summary>
    internal static TimeSpan FoldersSlideDuration { get; set; } = TimeSpan.FromMilliseconds(260);

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

    /// <summary>Whether the folder panel is shown, or on its way there (it slides).</summary>
    internal bool ShowsFolders => foldersShown;

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
            bridge.ImportConflictsFound -= OnImportConflictsFound;
        }
        bridge = Bridge;
        if (bridge is not null)
        {
            bridge.PropertyChanged += OnBridgePropertyChanged;
            bridge.DeleteConfirmationRequested += OnDeleteConfirmationRequested;
            bridge.ImportConflictsFound += OnImportConflictsFound;
        }
        contextMenu.DataContext = bridge;
        // Another tab: its folders as they are, at once.
        RefreshFolders(animate: false);
        RefreshEntriesView();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.ShownViewMode))
        {
            RefreshEntriesView();
        }
        else if (e.PropertyName == nameof(ExplorerViewModel.FoldersVisible))
        {
            RefreshFolders();
        }
        else if (e.PropertyName == nameof(ExplorerViewModel.HasSearchQuery))
        {
            // Results take the whole area, folder panel included; only once there is a query: the search row open
            // on its own leaves the folders as they were.
            RefreshFolders();
        }
    }

    /// <summary>
    /// SplitView hides a hidden item's handle; the panel keeps its width for when it returns. It slides shut or open,
    /// the entries taking or giving back its room: the panel keeps its width as it goes and is cut off at its edge,
    /// as a drawer, fading as it closes.
    /// </summary>
    private void RefreshFolders(bool animate = true)
    {
        bool visible = Bridge?.FoldersVisible != false && Bridge?.HasSearchQuery != true;
        if (visible == foldersShown && foldersSlide == 0)
        {
            ApplyFolders(visible);
            return;
        }
        if (visible == foldersShown)
            return;
        ColumnDefinition column = Split.ColumnDefinitions[0];
        ColumnDefinition gutter = Split.ColumnDefinitions[1];
        // The width it comes back at: what it had, unless it is still sliding.
        if (!visible && foldersSlide == 0 && FoldersFrame.IsVisible)
            folderWidth = column.Width;
        foldersShown = visible;
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (!animate || top is null || !IsEffectivelyVisible || FoldersSlideDuration <= TimeSpan.Zero)
        {
            foldersSlide = 0;
            ApplyFolders(visible);
            return;
        }
        // From wherever it is now, so a slide turned round halfway goes back smoothly.
        double fromWidth = FoldersFrame.IsVisible ? FoldersFrame.Bounds.Width : 0;
        double fromGutter = FoldersFrame.IsVisible ? gutter.ActualWidth : 0;
        double fromOpacity = FoldersFrame.IsVisible ? FoldersFrame.Opacity : 0;
        double toWidth = visible ? folderWidth.Value : 0;
        double toGutter = visible ? Ui.Theme.PanelGutter : 0;
        FoldersFrame.IsVisible = true;
        Handle.IsVisible = false;
        column.MinWidth = 0;
        Folders.Width = folderWidth.Value;
        Folders.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        int slide = ++foldersSlide;
        var clock = Stopwatch.StartNew();
        void Step(TimeSpan _)
        {
            if (slide != foldersSlide)
                return;
            double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / FoldersSlideDuration.TotalMilliseconds);
            double eased = progress < 0.5 ? 4 * progress * progress * progress : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
            column.Width = new GridLength(fromWidth + (toWidth - fromWidth) * eased);
            gutter.Width = new GridLength(fromGutter + (toGutter - fromGutter) * eased);
            // Closing, it is gone a little before its room is; opening, it shows as it comes.
            double fade = visible ? eased : Math.Min(1, eased * 1.4);
            FoldersFrame.Opacity = fromOpacity + ((visible ? 1 : 0) - fromOpacity) * fade;
            if (progress < 1)
            {
                top.RequestAnimationFrame(Step);
                return;
            }
            foldersSlide = 0;
            ApplyFolders(visible);
        }
        top.RequestAnimationFrame(Step);
    }

    /// <summary>The folder panel at rest: shown at its width with its handle, or gone with its gutter.</summary>
    private void ApplyFolders(bool visible)
    {
        ColumnDefinition column = Split.ColumnDefinitions[0];
        FoldersFrame.IsVisible = visible;
        FoldersFrame.Opacity = 1;
        Folders.Width = double.NaN;
        Folders.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Handle.IsVisible = visible;
        Split.ColumnDefinitions[1].Width = new GridLength(visible ? Ui.Theme.PanelGutter : 0);
        column.MinWidth = visible ? FolderPanel.MinimumWidth : 0;
        column.Width = visible ? folderWidth : new GridLength(0);
    }

    /// <summary>The Loader: <c>viewMode === "grid" ? gridComponent : listComponent</c>.</summary>
    private void RefreshEntriesView()
    {
        // Results are always the list: they need its LOCATION.
        bool grid = Bridge?.ShownViewMode == "grid";
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
        Control next;
        if (grid)
        {
            var view = new EntryGrid();
            view.ContextMenuRequested += OnContextMenuRequested;
            next = view;
        }
        else
        {
            var view = new EntryTable();
            view.ContextMenuRequested += OnContextMenuRequested;
            next = view;
        }
        // One view morphs into the other: the items glide to their new places.
        EntryViewMorph.Run(EntriesArea, EntriesHost, next, () => EntriesHost.Content = next);
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

    /// <summary>A drop's names are already in the folder: asks whether to replace, skip or keep both.</summary>
    private void OnImportConflictsFound(object? sender, ImportConflicts pending)
    {
        if (Bridge is not { } model || importDialog is not null)
            return;
        var dialog = new ImportConflictDialog(model, pending);
        importDialog = dialog;
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (importDialog == dialog)
                importDialog = null;
        });
        dialog.Open(this);
    }

    /// <summary>The open dialog asking about a drop's names already here, if any.</summary>
    internal ImportConflictDialog? ImportDialog => importDialog;

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
