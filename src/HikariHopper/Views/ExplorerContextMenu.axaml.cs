using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>A creation the context menu asked for: QML <c>createRequested(kind, suggestedName, sourcePath)</c>.</summary>
public sealed record CreateRequest(string Kind, string SuggestedName, string SourcePath);

/// <summary>The entry/background context menu (port of <c>ExplorerContextMenu.qml</c>); its DataContext is the tab's view model.</summary>
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

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        ExplorerViewModel? bridge = Bridge;
        Dispatcher.UIThread.Post(() => bridge?.RequestDeleteSelection());
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Bridge?.Refresh();
}
