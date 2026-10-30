using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The frameless main window (port of <c>main.qml</c>): 1360x840, at least 940x580, centred on
/// the screen's working area, with the shared <see cref="Controls.WindowTitleBar"/> showing
/// <see cref="Window.Title"/>, the 1px blue piping on top and invisible resize grips.
/// </summary>
/// <remarks>
/// The explorer screen goes into the <c>ExplorerScreenHost</c> placeholder <see cref="ContentControl"/>
/// (an x:Name field: set its <see cref="ContentControl.Content"/>). Set <see cref="Window.Title"/>
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
    }

    /// <summary>The window title for the active tab, exactly as main.qml computed it.</summary>
    public static string TitleFor(bool hasWorkspace, string tabTitle) =>
        hasWorkspace ? $"HikariHopper — {tabTitle}" : "HikariHopper — RPF Explorer";
}
