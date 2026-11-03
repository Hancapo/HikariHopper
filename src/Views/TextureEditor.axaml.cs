using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The tool dialogs <see cref="TextureEditor.OpenToolDialog"/> can show.</summary>
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
/// The texture editor of one explorer tab, shown as a modal that fills the main window over its
/// blurred explorer (see <see cref="ModalOverlay"/>).
/// </summary>
/// <remarks>
/// <para>It shows itself over <see cref="Anchor"/> when the view model raises
/// <see cref="TextureViewerViewModel.OpenRequested"/>. Closing it (the close mark, Esc, Ctrl+W,
/// File › Close) asks the view model via <see cref="TextureViewerViewModel.RequestClose"/>, which may
/// show the unsaved-changes dialog; <see cref="TextureViewerViewModel.CloseRequested"/> then hides
/// it. Its tab closing removes it for good (<see cref="Destroy"/>).</para>
/// <para>Its shortcuts (F2, Del, Ctrl+C, Ctrl+Z, Ctrl+V, Ctrl+S, Ctrl+Shift+S, Ctrl+E, Ctrl+W, F, 1)
/// take precedence over the focused control, and none fire while a dialog or menu is open. The main
/// window's own shortcuts are blocked while it is up, as under any modal.</para>
/// </remarks>
public partial class TextureEditor : UserControl, IModalContent
{
    private readonly TextureViewerViewModel viewModel;
    private ModalOverlay? overlay;
    private bool closeApproved;
    private TextureToolDialog? toolDialog;
    private TextureUnsavedChangesDialog? unsavedDialog;
    private TextureImportDialog? importDialog;
    private TexturePasteDialog? pasteDialog;

    /// <summary>For the XAML designer only: an editor over an empty view model.</summary>
    public TextureEditor() : this(new TextureViewerViewModel())
    {
    }

    public TextureEditor(TextureViewerViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        // Its shortcuts tunnel down to the focused element, so focus lives inside the editor.
        Focusable = true;
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
        CloseMark.Click += (_, _) => Close();
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

    /// <summary>What the editor shows over: a control in the main window. The main window's content when null.</summary>
    public Control? Anchor { get; set; }

    /// <summary>The document it shows, its state and kind: "name * — Texture Dictionary".</summary>
    public string Title { get; private set; } = "";

    /// <summary>Whether the editor is showing.</summary>
    public bool IsOpen => overlay?.IsShown == true;

    private bool destroying;

    /// <summary>QML <c>closeApproved</c>: the next close hides the editor instead of asking the view model.</summary>
    public bool CloseApproved => closeApproved;

    /// <summary>The open tool dialog (QML <c>toolDialogLoader.item</c>), if any.</summary>
    public TextureToolDialog? ToolDialog => toolDialog;

    public TextureUnsavedChangesDialog? UnsavedDialog => unsavedDialog;

    public TextureImportDialog? ImportDialog => importDialog;

    public TexturePasteDialog? PasteDialog => pasteDialog;

    /// <summary>Whether a modal dialog or a menu is showing; window shortcuts are blocked meanwhile.</summary>
    public bool PopupOpen => DialogLayer.HasDialogs || MenuRow.MenuOpen || Rail.ContextMenuPopup.IsOpen;

    /// <summary>QML <c>present()</c>: shows the editor over the main window, or leaves it up if it is.</summary>
    public void Present()
    {
        if (IsOpen)
            return;
        Control? anchor = Anchor
            ?? ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.ExplorerScreenHost;
        if (anchor is null)
            return;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is not null)
            overlay.Closed += (_, _) => overlay = null;
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Asks to close: hides the editor once the view model has approved (no unsaved changes, or they
    /// were saved or discarded), and otherwise asks the view model, which may ask the user first.
    /// </summary>
    public void Close()
    {
        if (closeApproved || destroying)
            overlay?.Close();
        else if (IsOpen)
            Dispatcher.UIThread.Post(viewModel.RequestClose);
    }

    /// <summary>
    /// Escape peels back one layer at a time: an open menu or context menu closes first, a dialog
    /// handles its own Escape, and only then does the editor itself close.
    /// </summary>
    void IModalContent.RequestClose()
    {
        if (Rail.ContextMenuPopup.IsOpen)
            Rail.ContextMenuPopup.Close();
        else if (MenuRow.MenuOpen)
            MenuRow.CloseMenus();
        else if (!DialogLayer.HasDialogs)
            Close();
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

    /// <summary>Removes the editor for good when its tab is removed (QML's Instantiator destroying the delegate).</summary>
    public void Destroy()
    {
        destroying = true;
        overlay?.Close();
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
            ? "Texture Dictionary"
            : $"{viewModel.SourceName}{(viewModel.Modified && !viewModel.Saving ? " *" : "")} — "
              + (viewModel.IsDrawable ? "Embedded Textures" : "Texture Dictionary");
        TitleText.Text = Title;
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
