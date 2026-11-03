using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The texture rail's context menu (port of <c>TextureContextMenu.qml</c>). Commands that need a
/// dialog raise the matching event; the others call the view model directly. Set its
/// <see cref="Avalonia.StyledElement.DataContext"/> to the <see cref="TextureViewerViewModel"/>.
/// </summary>
public partial class TextureContextMenu : RetroMenu
{
    public TextureContextMenu()
    {
        InitializeComponent();
        ReplaceItem.Triggered += (_, _) => _ = ViewModel?.ReplaceSelectedFromImage();
        ResizeItem.Triggered += (_, _) => ResizeRequested?.Invoke(this, EventArgs.Empty);
        PowerOfTwoItem.Triggered += (_, _) => PowerOfTwoRequested?.Invoke(this, EventArgs.Empty);
        MipmapsItem.Triggered += (_, _) => MipmapsRequested?.Invoke(this, EventArgs.Empty);
        FormatItem.Triggered += (_, _) => FormatRequested?.Invoke(this, EventArgs.Empty);
        OptimizeItem.Triggered += (_, _) => ViewModel?.OptimizeSelectedCompression();
        AlphaItem.Triggered += (_, _) => AlphaRepairRequested?.Invoke(this, EventArgs.Empty);
        CopyItem.Triggered += (_, _) => ViewModel?.CopySelectedImage();
        PasteItem.Triggered += (_, _) => _ = ViewModel?.RequestPasteImage();
        ExtractItem.Triggered += (_, _) => _ = ViewModel?.ExtractSelected();
        RenameItem.Triggered += (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty);
        RemoveItem.Triggered += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ResizeRequested;

    public event EventHandler? PowerOfTwoRequested;

    public event EventHandler? MipmapsRequested;

    public event EventHandler? FormatRequested;

    public event EventHandler? AlphaRepairRequested;

    public event EventHandler? RenameRequested;

    public event EventHandler? RemoveRequested;

    protected override Type StyleKeyOverride => typeof(RetroMenu);

    private TextureViewerViewModel? ViewModel => DataContext as TextureViewerViewModel;
}
