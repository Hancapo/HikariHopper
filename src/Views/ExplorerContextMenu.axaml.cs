using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>A creation the context menu asked for: QML <c>createRequested(kind, suggestedName, sourcePath)</c>.</summary>
public sealed record CreateRequest(string Kind, string SuggestedName, string SourcePath);

/// <summary>
/// The explorer's context menu; its DataContext is the tab's view model. <see cref="Refresh"/> makes it an item's
/// menu (open, copy, delete) when something is selected, or the folder's (new, refresh) when nothing is.
/// </summary>
public partial class ExplorerContextMenu : RetroMenu
{
    /// <summary>The boxed 1 compared against <c>selectionCount</c>.</summary>
    public static readonly object One = 1;

    private bool pressedInside;

    public ExplorerContextMenu()
    {
        InitializeComponent();
        // A Qt Quick Menu with margins 0 slides back inside the window instead of flipping.
        PlacementConstraintAdjustment = Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideX
            | Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideY;
        // The menu opens on a press; Qt menu items only trigger for a press they received, so the
        // release that ends the opening press must not click the item that appeared under it.
        Opened += (_, _) => pressedInside = false;
        AddHandler(PointerPressedEvent, (_, _) => pressedInside = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (!pressedInside)
                e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Use RetroMenu's control theme.</summary>
    protected override Type StyleKeyOverride => typeof(RetroMenu);

    /// <summary>QML <c>createRequested</c>, raised after the menu has closed.</summary>
    public event EventHandler<CreateRequest>? CreateRequested;

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>Whether it is the folder's own menu (nothing selected) rather than an item's.</summary>
    public bool ForFolder { get; private set; }

    /// <summary>Shows what applies to what was right-clicked; called as it opens.</summary>
    public void Refresh()
    {
        ExplorerViewModel? bridge = Bridge;
        int count = bridge?.SelectionCount ?? 0;
        bool busy = bridge?.EntryOperationBusy ?? true;
        ForFolder = count == 0;

        // An item's: opening one the app can open, copying, deleting.
        OpenItem.IsVisible = count == 1;
        OpenItem.IsEnabled = !busy && bridge?.CanOpenSelection == true;
        CopyItem.IsVisible = CopyNameItem.IsVisible = CopyPathItem.IsVisible = DeleteSeparator.IsVisible = DeleteMenuItem.IsVisible = !ForFolder;
        CopyItem.IsEnabled = !busy;
        RenameItem.IsVisible = count == 1;
        RenameItem.IsEnabled = bridge?.CanRenameSelection == true;
        // Extracting: any selection, and an RPF's whole content.
        ExtractSeparator.IsVisible = ExtractItem.IsVisible = !ForFolder;
        ExtractItem.IsEnabled = !busy;
        ExtractContentItem.IsVisible = bridge?.CanExtractArchiveContent == true;
        ExtractContentItem.IsEnabled = !busy;
        CopyNameItem.Text = count > 1 ? "Copy names" : "Copy name";
        CopyPathItem.Text = count > 1 ? "Copy paths" : "Copy path";
        DeleteMenuItem.Text = count > 1 ? $"Delete {count} items" : "Delete";
        DeleteMenuItem.IsEnabled = !busy && bridge?.SelectionDeletable == true;

        // The folder's: making something new in it, and reading it again.
        NewEntryMenu.IsVisible = PasteItem.IsVisible = RefreshSeparator.IsVisible = RefreshItem.IsVisible = ForFolder;
        PasteItem.IsEnabled = bridge?.CanPaste == true;
        NewEntryMenu.IsEnabled = RefreshItem.IsEnabled = !busy;
    }

    /// <summary><c>Qt.callLater(createRequested(...))</c>.</summary>
    private void RequestCreation(string kind, string suggestedName, string sourcePath) =>
        Dispatcher.UIThread.Post(() => CreateRequested?.Invoke(this, new CreateRequest(kind, suggestedName, sourcePath)));

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (Bridge is { } bridge)
            bridge.ActivateEntry(bridge.SelectedIndex);
    }

    private void OnNewFolder(object? sender, RoutedEventArgs e) => RequestCreation("folder", "New folder", "");

    private void OnNewYtd(object? sender, RoutedEventArgs e) => RequestCreation("ytd", "new_texture_dictionary.ytd", "");

    private void OnNewEmptyRpf(object? sender, RoutedEventArgs e) => RequestCreation("empty-rpf", "new_archive.rpf", "");

    private void OnNewRpfFromFolder(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(async () =>
        {
            if (bridge is null)
                return;
            RpfSource source = await bridge.ChooseRpfFolderSource();
            if (source.Path.Length > 0)
                RequestCreation("folder-rpf", source.SuggestedName, source.Path);
        });
    }

    private void OnNewRpfFromZip(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(async () =>
        {
            if (bridge is null)
                return;
            RpfSource source = await bridge.ChooseRpfZipSource();
            if (source.Path.Length > 0)
                RequestCreation("zip-rpf", source.SuggestedName, source.Path);
        });
    }

    private void OnExtract(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => _ = bridge?.ExtractSelection());
    }

    private void OnExtractContent(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => _ = bridge?.ExtractArchiveContent());
    }

    private void OnCopy(object? sender, RoutedEventArgs e) => _ = Bridge?.CopySelection();

    /// <summary>Asks for the rename dialog once the menu has closed.</summary>
    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Bridge is { CanRenameSelection: true } bridge)
            RequestCreation("rename", bridge.RenameSource, "");
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => _ = bridge?.Paste());
    }

    private void OnCopyName(object? sender, RoutedEventArgs e) => Bridge?.CopySelectedName();

    private void OnCopyPath(object? sender, RoutedEventArgs e) => Bridge?.CopySelectedPath();

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => bridge?.RequestDeleteSelection());
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Bridge?.Refresh();
}
