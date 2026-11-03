using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The texture window's menu bar (port of <c>TextureViewerMenuRow.qml</c>): File (save, save as,
/// extract all, close) and Edit (undo, remove duplicates, paste). The keyboard shortcuts shown in
/// the menus are handled by <see cref="TextureDictionaryWindow"/>.
/// </summary>
public partial class TextureViewerMenuRow : UserControl
{
    private readonly RetroMenu fileMenu;
    private readonly RetroMenu editMenu;
    private readonly RetroMenuItem undoItem, saveItem, saveAsItem;
    private TextureViewerViewModel? viewModel;

    public TextureViewerMenuRow()
    {
        InitializeComponent();
        fileMenu = (RetroMenu)Resources["FileMenu"]!;
        editMenu = (RetroMenu)Resources["EditMenu"]!;
        undoItem = (RetroMenuItem)editMenu.Items[0]!;
        ((RetroMenuItem)editMenu.Items[2]!).Triggered += (_, _) => viewModel?.RemoveDuplicatesByName();
        ((RetroMenuItem)editMenu.Items[4]!).Triggered += (_, _) => _ = viewModel?.RequestPasteImage();
        undoItem.Triggered += (_, _) => viewModel?.Undo();
        saveItem = (RetroMenuItem)fileMenu.Items[0]!;
        saveAsItem = (RetroMenuItem)fileMenu.Items[1]!;
        saveItem.Triggered += (_, _) => viewModel?.SaveYtd();
        saveAsItem.Triggered += (_, _) => _ = viewModel?.SaveYtdAs();
        ((RetroMenuItem)fileMenu.Items[3]!).Triggered += (_, _) => _ = viewModel?.ExtractAll();
        ((RetroMenuItem)fileMenu.Items[5]!).Triggered += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        fileMenu.Opened += (_, _) => FileButton.MenuOpen = true;
        fileMenu.Closed += (_, _) => FileButton.MenuOpen = false;
        editMenu.Opened += (_, _) => EditButton.MenuOpen = true;
        editMenu.Closed += (_, _) => EditButton.MenuOpen = false;
        FileButton.Click += (_, _) => OpenPopup(fileMenu, FileButton);
        EditButton.Click += (_, _) => OpenPopup(editMenu, EditButton);
        RefreshLabels();
    }

    /// <summary>File › Close (QML <c>viewerWindow.close()</c>).</summary>
    public event EventHandler? CloseRequested;

    public RetroMenu FileMenu => fileMenu;

    public RetroMenu EditMenu => editMenu;

    /// <summary>Whether either menu is showing.</summary>
    public bool MenuOpen => fileMenu.IsOpen || editMenu.IsOpen;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as TextureViewerViewModel;
        fileMenu.DataContext = viewModel;
        editMenu.DataContext = viewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelChanged;
        RefreshLabels();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextureViewerViewModel.UndoLabel) or nameof(TextureViewerViewModel.IsDrawable))
            RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (undoItem is null)
            return;
        string undoLabel = viewModel?.UndoLabel ?? "";
        bool drawable = viewModel?.IsDrawable ?? false;
        undoItem.Text = undoLabel.Length == 0 ? "Undo" : $"Undo {undoLabel}";
        saveItem.Text = drawable ? "Save YDR" : "Save YTD";
        saveAsItem.Text = drawable ? "Save YDR as…" : "Save YTD as…";
    }

    private void OpenPopup(RetroMenu menu, MenuBarButton button)
    {
        Point origin = button.TranslatePoint(default, this) ?? default;
        menu.ConnectionWidth = button.Bounds.Width;
        RetroMenuPlacement.Open(menu, this, new Point(origin.X, Bounds.Height - 1));
    }
}
