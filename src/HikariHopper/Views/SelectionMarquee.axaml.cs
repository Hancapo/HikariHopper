using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// The rubber band drawn while marquee-selecting (port of <c>SelectionMarquee.qml</c>). Place it
/// with <see cref="Place"/> in its parent <see cref="Canvas"/>.
/// </summary>
public partial class SelectionMarquee : UserControl
{
    public SelectionMarquee()
    {
        InitializeComponent();
        Content = new Art();
    }

    /// <summary>x/y/width/height from the press origin and the pointer, as the QML bound them.</summary>
    public void Place(Point origin, Point pointer)
    {
        Canvas.SetLeft(this, Math.Min(origin.X, pointer.X));
        Canvas.SetTop(this, Math.Min(origin.Y, pointer.Y));
        Width = Math.Abs(pointer.X - origin.X);
        Height = Math.Abs(pointer.Y - origin.Y);
    }

    private sealed class Art : Control
    {
        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            context.FillRectangle(Ui.Theme.MarqueeFill, new Rect(0, 0, width, height));

            // Clipping is intentional here: the hatch must terminate exactly at the rubber-band edge
            // while its geometry changes under direct manipulation.
            double hatchWidth = Math.Max(0, width - 2);
            double hatchHeight = Math.Max(0, height - 2);
            if (hatchWidth > 0 && hatchHeight > 0)
            {
                double lineLength = Math.Ceiling(Math.Sqrt(2) * (hatchWidth + hatchHeight)) + 2;
                int lineCount = (int)Math.Ceiling((hatchWidth + hatchHeight) / 8) + 3;
                using (context.PushClip(new Rect(1, 1, hatchWidth, hatchHeight)))
                using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
                {
                    for (int index = 0; index < lineCount; index++)
                    {
                        // Rectangle { x: index * 8 - height / 2 - 8; width: 1; height: lineLength; rotation: 45 }
                        double x = 1 + index * 8 - hatchHeight * 0.5 - 8;
                        double y = 1 + (hatchHeight - lineLength) * 0.5;
                        var center = new Point(x + 0.5, y + lineLength / 2);
                        Matrix rotation = Matrix.CreateTranslation(-center.X, -center.Y)
                            * Matrix.CreateRotation(Math.PI / 4)
                            * Matrix.CreateTranslation(center.X, center.Y);
                        using (context.PushTransform(rotation))
                            context.FillRectangle(Ui.Theme.MarqueeHatch, new Rect(x, y, 1, lineLength));
                    }
                }
            }

            IBrush border = Ui.Theme.MarqueeBorder;
            if (width >= 1 && height >= 1)
            {
                context.FillRectangle(border, new Rect(0, 0, width, 1));
                if (height > 1)
                    context.FillRectangle(border, new Rect(0, height - 1, width, 1));
                context.FillRectangle(border, new Rect(0, 1, 1, Math.Max(0, height - 2)));
                if (width > 1)
                    context.FillRectangle(border, new Rect(width - 1, 1, 1, Math.Max(0, height - 2)));
            }
        }
    }
}
