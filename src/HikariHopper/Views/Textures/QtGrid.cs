using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HikariHopper.Views;

/// <summary>
/// A <see cref="Grid"/> for ports of QML <c>RowLayout</c>/<c>GridLayout</c>s: children that are
/// vertically centred in their cell land where Qt Quick Layouts put them, at
/// <c>qRound((cell - height) / 2)</c> (halves go down the cell), instead of Avalonia's
/// round-half-to-even.
/// </summary>
public class QtGrid : Grid
{
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Size size = base.ArrangeOverride(arrangeSize);
        IList<RowDefinition> rows = RowDefinitions;
        IList<ColumnDefinition> columns = ColumnDefinitions;
        foreach (Control child in Children)
        {
            if (child.VerticalAlignment != VerticalAlignment.Center || !child.IsVisible)
                continue;
            int row = Math.Min(GetRow(child), Math.Max(0, rows.Count - 1));
            int rowSpan = Math.Max(1, GetRowSpan(child));
            int column = Math.Min(GetColumn(child), Math.Max(0, columns.Count - 1));
            int columnSpan = Math.Max(1, GetColumnSpan(child));
            double top = 0, height = size.Height, left = 0, width = size.Width;
            if (rows.Count > 0)
            {
                top = rows.Take(row).Sum(r => r.ActualHeight);
                height = rows.Skip(row).Take(rowSpan).Sum(r => r.ActualHeight);
            }
            if (columns.Count > 0)
            {
                left = columns.Take(column).Sum(c => c.ActualWidth);
                width = columns.Skip(column).Take(columnSpan).Sum(c => c.ActualWidth);
            }
            double childHeight = child.DesiredSize.Height;
            double y = top + Math.Floor((height - childHeight) / 2 + 0.5);
            child.Arrange(new Rect(left, y, width, childHeight));
        }
        return size;
    }
}
