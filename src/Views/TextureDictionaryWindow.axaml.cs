using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The tool dialogs <see cref="TextureDictionaryWindow.OpenToolDialog"/> can show.</summary>
public enum TextureTool
{
    Resize,
    PowerOfTwo,
    Mipmaps,
    Format,
    AlphaRepair,
    Rename,
    Remove,
}

/// <summary>
/// The texture dictionary editor window of one explorer tab (port of <c>TextureDictionaryWindow.qml</c>).
/// </summary>
/// <remarks>
/// <para>A frameless 1250×738 window (at least 1080×620) that shows itself when the view model raises
/// <see cref="TextureViewerViewModel.OpenRequested"/>, owned by <see cref="OwnerWindow"/> or else the
/// application's main window. Closing it (title bar, Ctrl+W, File › Close) asks the view model via
/// <see cref="TextureViewerViewModel.RequestClose"/>, which may show the unsaved-changes dialog;
/// <see cref="TextureViewerViewModel.CloseRequested"/> then hides it. Closing the owner or the
/// application closes it for real.</para>
/// <para>Window shortcuts (F2, Del, Ctrl+C, Ctrl+Z, Ctrl+V, Ctrl+S, Ctrl+Shift+S, Ctrl+E, Ctrl+W, F, 1)
/// take precedence over the focused control, and none fire while a dialog or menu is open, as Qt
/// blocks shortcuts under a modal popup or menu.</para>
/// </remarks>
public partial class TextureDictionaryWindow : Window
{
    private readonly TextureViewerViewModel viewModel;
    private bool closeApproved;
    private TextureToolDialog? toolDialog;
    private TextureUnsavedChangesDialog? unsavedDialog;
    private TextureImportDialog? importDialog;
    private TexturePasteDialog? pasteDialog;

    /// <summary>For the XAML designer only: a window over an empty view model.</summary>
    public TextureDictionaryWindow() : this(new TextureViewerViewModel())
    {
    }

    public TextureDictionaryWindow(TextureViewerViewModel viewModel)
    {
        this.viewModel = viewModel;
        QtFramelessWindow.Apply(this);
        InitializeComponent();
        DataContext = viewModel;
        SplitGrid.ColumnDefinitions[0].Width = new GridLength(Ui.Theme.TextureRailWidth);

        viewModel.PropertyChanged += ViewModelChanged;
        viewModel.OpenRequested += (_, _) =>
        {
            closeApproved = false;
            Present();
        };
        viewModel.UnsavedChangesRequested += (_, _) =>
        {
            Present();
            OpenUnsavedDialog();
        };
        viewModel.CloseRequested += (_, _) =>
        {
            closeApproved = true;
            Close();
        };
        viewModel.ImageImportConfirmationRequested += (_, _) => OpenImportDialog();
        viewModel.PasteImageRequested += (_, _) => OpenPasteDialog();

        MenuRow.CloseRequested += (_, _) => Close();
        Rail.ResizeRequested += (_, _) => OpenToolDialog(TextureTool.Resize);
        Rail.PowerOfTwoRequested += (_, _) => OpenToolDialog(TextureTool.PowerOfTwo);
        Rail.MipmapsRequested += (_, _) => OpenToolDialog(TextureTool.Mipmaps);
        Rail.FormatRequested += (_, _) => OpenToolDialog(TextureTool.Format);
        Rail.AlphaRepairRequested += (_, _) => OpenToolDialog(TextureTool.AlphaRepair);
        Rail.RenameRequested += (_, _) => OpenToolDialog(TextureTool.Rename);
        Rail.RemoveRequested += (_, _) => OpenToolDialog(TextureTool.Remove);
        AddHandler(KeyDownEvent, ShortcutKeyDown, RoutingStrategies.Tunnel);
        RefreshTitle();
        RefreshDropEnabled();
    }

    public TextureViewerViewModel ViewModel => viewModel;

    /// <summary>The window to own this one when it is shown; the application's main window when null.</summary>
    public Window? OwnerWindow { get; set; }

    private bool destroying;

    /// <summary>QML <c>closeApproved</c>: the next close hides the window instead of asking the view model.</summary>
    public bool CloseApproved => closeApproved;

    /// <summary>The open tool dialog (QML <c>toolDialogLoader.item</c>), if any.</summary>
    public TextureToolDialog? ToolDialog => toolDialog;

    public TextureUnsavedChangesDialog? UnsavedDialog => unsavedDialog;

    public TextureImportDialog? ImportDialog => importDialog;

    public TexturePasteDialog? PasteDialog => pasteDialog;

    /// <summary>Whether a modal dialog or a menu is showing; window shortcuts are blocked meanwhile.</summary>
    public bool PopupOpen => DialogLayer.HasDialogs || MenuRow.MenuOpen || Rail.ContextMenuPopup.IsOpen;

    /// <summary>QML <c>present()</c>: shows, raises and activates the window.</summary>
    public void Present()
    {
        if (!IsVisible)
        {
            Window? owner = OwnerWindow
                ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is not null && owner != this && owner.IsVisible)
                Show(owner);
            else
                Show();
        }
        Activate();
    }

    /// <summary>QML <c>openToolDialog(component)</c>: replaces any open tool dialog with a new one.</summary>
    public TextureToolDialog OpenToolDialog(TextureTool tool)
    {
        if (toolDialog is { } previous)
        {
            previous.Closed -= ToolDialogClosed;
            previous.Close();
            toolDialog = null;
        }
        TextureToolDialog dialog = tool switch
        {
            TextureTool.Resize => new TextureResizeDialog(),
            TextureTool.PowerOfTwo => new TextureResizeDialog { PowerOfTwo = true },
            TextureTool.Mipmaps => new TextureMipmapsDialog(),
            TextureTool.Format => new TextureFormatDialog(),
            TextureTool.AlphaRepair => new TextureAlphaRepairDialog(),
            TextureTool.Rename => new TextureRenameDialog(),
            TextureTool.Remove => new TextureRemoveDialog(),
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        dialog.DataContext = viewModel;
        dialog.Closed += ToolDialogClosed;
        toolDialog = dialog;
        RefreshDropEnabled();
        dialog.Open(DialogLayer);
        return dialog;
    }

    /// <summary>QML <c>openUnsavedDialog()</c>.</summary>
    public void OpenUnsavedDialog()
    {
        if (unsavedDialog is { } existing)
        {
            if (!existing.IsOpen)
                existing.Open(DialogLayer);
            return;
        }
        var dialog = new TextureUnsavedChangesDialog { DataContext = viewModel };
        dialog.SaveRequested += (_, _) => _ = viewModel.ResolvePendingChanges("save");
        dialog.DiscardRequested += (_, _) => _ = viewModel.ResolvePendingChanges("discard");
        dialog.Rejected += (_, _) => _ = viewModel.ResolvePendingChanges("cancel");
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (unsavedDialog == dialog && !dialog.IsOpen)
                unsavedDialog = null;
            RefreshDropEnabled();
        });
        unsavedDialog = dialog;
        RefreshDropEnabled();
        dialog.Open(DialogLayer);
    }

    /// <summary>Closes the window for good when its tab is removed (QML's Instantiator destroying the delegate).</summary>
    public void Destroy()
    {
        destroying = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || destroying || e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown
            or WindowCloseReason.OSShutdown)
            return;
        e.Cancel = true;
        if (closeApproved)
            Hide();
        else
            Dispatcher.UIThread.Post(viewModel.RequestClose);
    }

    private void OpenImportDialog()
    {
        if (importDialog is not null)
            return;
        var dialog = new TextureImportDialog { DataContext = viewModel };
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            importDialog = null;
            RefreshDropEnabled();
        });
        importDialog = dialog;
        dialog.Open(DialogLayer);
    }

    private void OpenPasteDialog()
    {
        if (pasteDialog is not null)
            return;
        var dialog = new TexturePasteDialog { DataContext = viewModel };
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            pasteDialog = null;
            RefreshDropEnabled();
        });
        pasteDialog = dialog;
        dialog.Open(DialogLayer);
    }

    private void ToolDialogClosed(object? sender, EventArgs e)
    {
        // QML onClosed: Qt.callLater(() => toolDialogLoader.active = false).
        Dispatcher.UIThread.Post(() =>
        {
            if (toolDialog == sender)
                toolDialog = null;
            RefreshDropEnabled();
        });
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextureViewerViewModel.SourceName) or nameof(TextureViewerViewModel.Modified)
            or nameof(TextureViewerViewModel.Saving) or nameof(TextureViewerViewModel.IsDrawable))
            RefreshTitle();
        if (e.PropertyName == nameof(TextureViewerViewModel.CanImportImages))
            RefreshDropEnabled();
    }

    private void RefreshTitle()
    {
        Title = viewModel.SourceName.Length == 0
            ? "Texture Dictionary — HikariHopper"
            : $"{viewModel.SourceName}{(viewModel.Modified && !viewModel.Saving ? " *" : "")} — "
              + $"{(viewModel.IsDrawable ? "Embedded Textures" : "Texture Dictionary")} — HikariHopper";
    }

    private void RefreshDropEnabled() =>
        DropArea.DropEnabled = viewModel.CanImportImages && toolDialog is null && unsavedDialog is null;

    private void ShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || PopupOpen)
            return;
        if (RunShortcut(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    /// <summary>Runs the window shortcut for a key, if one matches and is enabled; true when it ran.</summary>
    internal bool RunShortcut(Key key, KeyModifiers modifiers)
    {
        bool anyDialog = toolDialog is not null || unsavedDialog is not null || importDialog is not null || pasteDialog is not null;
        const KeyModifiers none = KeyModifiers.None, ctrl = KeyModifiers.Control;
        switch (key, modifiers)
        {
            case (Key.F2, none) when viewModel.SelectedCount == 1 && !viewModel.OperationBusy && pasteDialog is null:
                OpenToolDialog(TextureTool.Rename);
                return true;
            case (Key.Delete, none) when viewModel.CanRemoveSelection && !viewModel.OperationBusy && pasteDialog is null:
                OpenToolDialog(TextureTool.Remove);
                return true;
            case (Key.C, ctrl) when viewModel.SelectedCount == 1 && !viewModel.OperationBusy && !anyDialog:
                viewModel.CopySelectedImage();
                return true;
            case (Key.Z, ctrl) when viewModel.CanUndo && pasteDialog is null:
                viewModel.Undo();
                return true;
            case (Key.V, ctrl) when viewModel.CanPasteImage && !anyDialog:
                _ = viewModel.RequestPasteImage();
                return true;
            case (Key.S, ctrl) when viewModel.HasDocument && !viewModel.OperationBusy && viewModel.CanSaveSource:
                viewModel.SaveYtd();
                return true;
            case (Key.S, ctrl | KeyModifiers.Shift) when viewModel.HasDocument && !viewModel.OperationBusy:
                _ = viewModel.SaveYtdAs();
                return true;
            case (Key.E, ctrl) when viewModel.SelectedIndex >= 0:
                _ = viewModel.ExtractSelected();
                return true;
            case (Key.W, ctrl):
                Close();
                return true;
            case (Key.F, none):
                Preview.FitTexture();
                return true;
            case (Key.D1 or Key.NumPad1, none):
                Preview.ShowActualSize();
                return true;
            default:
                return false;
        }
    }
}
