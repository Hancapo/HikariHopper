using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The main window: 1360x840, at least 940x580, centred on the screen's working area. It is a native window whose
/// client area covers its frame (Avalonia's extended client area), with the shared <see cref="Controls.WindowTitleBar"/>
/// as a 38px title row that carries the explorer's menus; Windows keeps the frame's resizing, rounded corners, snap
/// layouts, taskbar minimizing and animations.
/// </summary>
/// <remarks>
/// The explorer screen goes into the <c>ExplorerScreenHost</c> placeholder <see cref="ContentControl"/>
/// (an x:Name field: set its <see cref="ContentControl.Content"/>) and its menu bar into
/// <c>TitleBar.LeadingContent</c>. Set <see cref="Window.Title"/>
/// with <see cref="TitleFor"/> (QML: <c>tabs.activeBridge.hasWorkspace ? "HikariHopper — %1" : …</c>).
/// Each tab's texture editor is a modal over this window's content (see <see cref="TextureEditor"/>).
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ModalOverlay.StateChanged += OnModalStateChanged;
        Closed += (_, _) => ModalOverlay.StateChanged -= OnModalStateChanged;
    }

    /// <summary>
    /// Asked whenever the window is about to close (the close button, Alt+F4, File › Exit): false keeps it open, as
    /// while unsaved files are asked about. Unset, it simply closes.
    /// </summary>
    public Func<bool>? AskBeforeClosing { get; set; }

    /// <summary>Closes the window without asking: what was unsaved was saved or let go.</summary>
    public void CloseWithoutAsking()
    {
        AskBeforeClosing = null;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!e.Cancel && AskBeforeClosing?.Invoke() == false)
            e.Cancel = true;
        base.OnClosing(e);
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
        hasWorkspace ? $"HikariHopper — {tabTitle}" : "HikariHopper — Modding Toolkit";
}
