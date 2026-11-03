using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// The transparency grid behind texture previews (port of <c>TextureCheckerboard.qml</c>): a
/// <c>chromeBg</c> fill with <c>appBg</c> cells of <see cref="CellSize"/> pixels wherever
/// <c>row + column</c> is odd, counted from the top-left corner and clipped to the bounds.
/// </summary>
public class TextureCheckerboard : Control
{
    public static readonly StyledProperty<int> CellSizeProperty =
        AvaloniaProperty.Register<TextureCheckerboard, int>(nameof(CellSize), 24);

    static TextureCheckerboard()
    {
        AffectsRender<TextureCheckerboard>(CellSizeProperty);
    }

    public int CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        int cell = CellSize;
        context.FillRectangle(Ui.Theme.ChromeBg, bounds);
        if (cell <= 0)
            return;
        using (context.PushClip(bounds))
        {
            var cells = new StreamGeometry();
            using (StreamGeometryContext geometry = cells.Open())
            {
                for (int y = 0, row = 0; y < bounds.Height; y += cell, row++)
                {
                    for (int x = 0, column = 0; x < bounds.Width; x += cell, column++)
                    {
                        if ((row + column) % 2 == 0)
                            continue;
                        geometry.BeginFigure(new Point(x, y), true);
                        geometry.LineTo(new Point(x + cell, y));
                        geometry.LineTo(new Point(x + cell, y + cell));
                        geometry.LineTo(new Point(x, y + cell));
                        geometry.EndFigure(true);
                    }
                }
            }
            context.DrawGeometry(Ui.Theme.AppBg, null, cells);
        }
    }
}
