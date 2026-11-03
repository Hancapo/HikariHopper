using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The frameless main window (port of <c>main.qml</c>): 1360x840, at least 940x580, centred on
/// the screen's working area, with the shared <see cref="Controls.WindowTitleBar"/> as a 38px
/// title row that carries the explorer's menus, the 1px blue piping (rounded by the window manager
/// on Windows 11) and invisible resize grips.
/// </summary>
/// <remarks>
/// The explorer screen goes into the <c>ExplorerScreenHost</c> placeholder <see cref="ContentControl"/>
/// (an x:Name field: set its <see cref="ContentControl.Content"/>) and its menu bar into
/// <c>TitleBar.LeadingContent</c>. Set <see cref="Window.Title"/>
/// with <see cref="TitleFor"/> (QML: <c>tabs.activeBridge.hasWorkspace ? "HikariHopper — %1" : …</c>).
/// One <c>TextureDictionaryWindow</c> per tab (QML <c>Instantiator</c>) is owned by the texture
/// viewer port and should be created as an owned window of this one.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        QtFramelessWindow.Apply(this);
        InitializeComponent();
        ModalOverlay.StateChanged += OnModalStateChanged;
        Closed += (_, _) => ModalOverlay.StateChanged -= OnModalStateChanged;
    }

    /// <summary>
    /// While a modal is up the title bar stays usable (move, minimise, close), but its menus are
    /// switched off so nothing opens over the dialog.
    /// </summary>
    private void OnModalStateChanged(object? sender, TopLevel host)
    {
        if (host == this && TitleBar.LeadingContent is Control menus)
        {
            bool open = ModalOverlay.IsOpen(this);
            menus.IsEnabled = !open;
            menus.Opacity = open ? 0.45 : 1;
        }
    }

    /// <summary>The window title for the active tab, exactly as main.qml computed it.</summary>
    public static string TitleFor(bool hasWorkspace, string tabTitle) =>
        hasWorkspace ? $"HikariHopper — {tabTitle}" : "HikariHopper — RPF Explorer";
}
