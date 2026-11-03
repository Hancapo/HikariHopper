namespace HikariHopper.Views;

/// <summary>Port of <c>grid_math.js</c>: keyboard targets and marquee hit-testing for the icon grid.</summary>
public static class GridMath
{
    public static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));

    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));

    /// <summary>The index an arrow key moves to: <c>left</c>, <c>right</c>, <c>up</c> or <c>down</c>; -1 when empty.</summary>
    public static int NavigationTarget(int current, int count, int columns, string direction)
    {
        if (count <= 0)
            return -1;
        int start = current >= 0 ? current : 0;
        int delta = direction switch
        {
            "left" => -1,
            "right" => 1,
            "up" => -columns,
            "down" => columns,
            _ => 0,
        };
        return Clamp(start + delta, 0, count - 1);
    }

    /// <summary>Cell indices intersecting the content rectangle <c>left..right × top..bottom</c>.</summary>
    public static List<int> IndicesInRect(int count, int columns, double cellWidth, double cellHeight,
        double leftMargin, double topMargin, double left, double right, double top, double bottom)
    {
        var indices = new List<int>();
        if (count <= 0 || columns <= 0 || right <= leftMargin || bottom <= topMargin)
            return indices;

        int rowCount = (int)Math.Ceiling(count / (double)columns);
        double contentRight = leftMargin + columns * cellWidth;
        double contentBottom = topMargin + rowCount * cellHeight;
        if (left >= contentRight || top >= contentBottom)
            return indices;

        const double epsilon = 0.001;
        int firstColumn = Clamp((int)Math.Floor((Math.Max(left, leftMargin) - leftMargin) / cellWidth), 0, columns - 1);
        int lastColumn = Clamp((int)Math.Floor((Math.Min(right, contentRight) - leftMargin - epsilon) / cellWidth), 0, columns - 1);
        int firstRow = Clamp((int)Math.Floor((Math.Max(top, topMargin) - topMargin) / cellHeight), 0, rowCount - 1);
        int lastRow = Clamp((int)Math.Floor((Math.Min(bottom, contentBottom) - topMargin - epsilon) / cellHeight), 0, rowCount - 1);

        for (int row = firstRow; row <= lastRow; ++row)
        {
            for (int column = firstColumn; column <= lastColumn; ++column)
            {
                int index = row * columns + column;
                if (index < count)
                    indices.Add(index);
            }
        }
        return indices;
    }
}
