using Avalonia;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// Draws rectangles the way Qt Quick's scene graph rasterises unantialiased <c>Rectangle</c>s at
/// fractional positions: a pixel is covered when its centre lies inside the rectangle, top and
/// left edges inclusive (so an edge at 8.5 starts at pixel 8). A bordered rectangle draws its
/// border inside the outline and fills the inset rectangle, each snapped on its own.
/// </summary>
internal static class QtRaster
{
    /// <summary>The first pixel whose centre is at or past <paramref name="edge"/>.</summary>
    public static double Snap(double edge) => Math.Ceiling(edge - 0.5);

    /// <summary>The rectangle of pixels a fractional rectangle covers.</summary>
    public static Rect SnapRect(double x, double y, double width, double height)
    {
        double left = Snap(x), top = Snap(y);
        return new Rect(left, top, Math.Max(0, Snap(x + width) - left), Math.Max(0, Snap(y + height) - top));
    }

    /// <summary>JavaScript <c>Math.round</c>: halves round up.</summary>
    public static double JsRound(double value) => Math.Floor(value + 0.5);

    public static void Fill(DrawingContext context, IBrush brush, double x, double y, double width, double height)
    {
        Rect rect = SnapRect(x, y, width, height);
        if (rect.Width > 0 && rect.Height > 0)
            context.FillRectangle(brush, rect);
    }

    /// <summary>A Qt <c>Rectangle</c> with <c>border.width</c> 1: <paramref name="fill"/> inside, the border on the outline.</summary>
    public static void FillBordered(DrawingContext context, IBrush? fill, IBrush border, double x, double y, double width, double height)
    {
        Rect outer = SnapRect(x, y, width, height);
        Rect inner = SnapRect(x + 1, y + 1, width - 2, height - 2);
        if (fill is not null && inner.Width > 0 && inner.Height > 0)
            context.FillRectangle(fill, inner);
        if (outer.Width <= 0 || outer.Height <= 0)
            return;
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            context.FillRectangle(border, outer);
            return;
        }
        context.FillRectangle(border, new Rect(outer.Left, outer.Top, outer.Width, inner.Top - outer.Top));
        context.FillRectangle(border, new Rect(outer.Left, inner.Bottom, outer.Width, outer.Bottom - inner.Bottom));
        context.FillRectangle(border, new Rect(outer.Left, inner.Top, inner.Left - outer.Left, inner.Height));
        context.FillRectangle(border, new Rect(inner.Right, inner.Top, outer.Right - inner.Right, inner.Height));
    }
}
