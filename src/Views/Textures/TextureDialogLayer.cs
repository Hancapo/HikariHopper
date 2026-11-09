using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The window overlay that hosts <see cref="TextureModalDialog"/>s (Qt Quick's <c>Overlay.overlay</c>):
/// every open dialog gets its own dimmer (the modal sheets' <c>ModalDim</c>) over the whole window, stacked in
/// opening order, and is centred at <c>Math.round((parent.width - width) / 2)</c>.
/// </summary>
public class TextureDialogLayer : Panel
{
    private readonly Dictionary<TextureModalDialog, Border> dimmers = [];

    /// <summary>The dialogs currently shown, oldest first.</summary>
    public IEnumerable<TextureModalDialog> Dialogs => Children.OfType<TextureModalDialog>();

    public bool HasDialogs => dimmers.Count > 0;

    internal void Add(TextureModalDialog dialog)
    {
        var dimmer = new Border { Background = Ui.Theme.ModalDim };
        // The modal background takes every pointer event, so nothing underneath reacts.
        dimmer.PointerPressed += (_, e) => e.Handled = true;
        dimmer.PointerReleased += (_, e) => e.Handled = true;
        dimmer.PointerWheelChanged += (_, e) => e.Handled = true;
        dimmers[dialog] = dimmer;
        Children.Add(dimmer);
        Children.Add(dialog);
    }

    internal void Remove(TextureModalDialog dialog)
    {
        if (dimmers.Remove(dialog, out Border? dimmer))
            Children.Remove(dimmer);
        Children.Remove(dialog);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            if (child is TextureModalDialog dialog)
            {
                Size size = dialog.DesiredSize;
                double width = double.IsNaN(dialog.Width) ? size.Width : dialog.Width;
                double height = double.IsNaN(dialog.Height) ? size.Height : dialog.Height;
                double x = QtRaster.JsRound((finalSize.Width - width) / 2);
                double y = QtRaster.JsRound((finalSize.Height - height) / 2);
                child.Arrange(new Rect(x, y, width, height));
            }
            else
                child.Arrange(new Rect(finalSize));
        }
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
            child.Measure(child is TextureModalDialog ? Size.Infinity : availableSize);
        return default;
    }
}
