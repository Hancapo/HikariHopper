using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>
/// A CSV file as the explorer's list shows entries: the first row as the column titles, which stay at the
/// top, then a row per record on the list's rhythm, with row numbers that stay at the left. Columns are as
/// wide as their longest value (within reason), and number columns are right-aligned. One row is current;
/// the find's matches are marked in brass, the current one washed. Drawn a screen at a time, in
/// <see cref="Scroller"/>.
/// </summary>
public sealed class CsvTableView : Control
{
    private const double CellPadding = 12;
    private const int MaxColumnChars = 48;
    private const int SampleRows = 2000;

    private CsvTable? table;
    private double[] widths = [];
    private bool[] numeric = [];
    private double charWidth;
    private int current;
    private IReadOnlyList<(int Row, int Column)> matches = [];
    private int currentMatch = -1;
    private string query = "";

    public CsvTableView()
    {
        Focusable = true;
        charWidth = TextViewerInk.CharWidth(Ui.Theme.FontSize);
    }

    /// <summary>The current row changed.</summary>
    public event EventHandler? CurrentRowChanged;

    public ScrollViewer? Scroller
    {
        get;
        set
        {
            if (field is not null)
                field.ScrollChanged -= OnScrollChanged;
            field = value;
            if (field is not null)
                field.ScrollChanged += OnScrollChanged;
        }
    }

    public CsvTable? Table
    {
        get => table;
        set
        {
            table = value;
            current = value is { Rows.Count: > 1 } ? 1 : 0;
            matches = [];
            currentMatch = -1;
            MeasureColumns();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The current row, from 1 (row 0 is the header).</summary>
    public int CurrentRow => current;

    /// <summary>The records, not counting the header.</summary>
    public int RecordCount => Math.Max(0, (table?.Rows.Count ?? 0) - 1);

    public double RowHeight => Ui.Theme.RowHeight;

    public double HeaderHeight => Ui.Theme.ListHeaderHeight;

    public double NumberWidth => Math.Max(3, (RecordCount + 1).ToString().Length) * charWidth + 24;

    /// <summary>The current row as a line of the file: its fields, joined by the file's separator.</summary>
    public string CurrentRowText(char separator) =>
        table is null || current <= 0 || current >= table.Rows.Count ? "" : string.Join(separator, table.Rows[current].Select(field => Quote(field, separator)));

    /// <summary>The cells holding <paramref name="text"/> (case aside), in reading order; the current one gets the focus.</summary>
    public void SetMatches(string text, IReadOnlyList<(int Row, int Column)> found, int index)
    {
        query = text;
        matches = found;
        currentMatch = index;
        if (index >= 0 && index < found.Count)
        {
            // A match in the titles leaves the current row alone: the titles are always on screen.
            if (found[index].Row == 0)
                RevealColumn(found[index].Column);
            else
                MoveTo(found[index].Row, found[index].Column);
        }
        InvalidateVisual();
    }

    /// <summary>Makes <paramref name="row"/> current, scrolling it (and <paramref name="column"/>) into view.</summary>
    public void MoveTo(int row, int column = -1)
    {
        if (table is null || table.Rows.Count < 2)
            return;
        current = Math.Clamp(row, 1, table.Rows.Count - 1);
        if (Scroller is { } scroller)
        {
            double top = HeaderHeight + (current - 1) * RowHeight;
            double y = scroller.Offset.Y;
            double body = scroller.Viewport.Height - HeaderHeight;
            if (top - HeaderHeight < y)
                y = top - HeaderHeight;
            else if (top + RowHeight - HeaderHeight > y + body)
                y = top + RowHeight - HeaderHeight - body;
            double x = scroller.Offset.X;
            if (column >= 0 && column < widths.Length)
            {
                double left = NumberWidth + widths.Take(column).Sum();
                double right = left + widths[column];
                if (left - NumberWidth < x)
                    x = left - NumberWidth;
                else if (right > x + scroller.Viewport.Width)
                    x = right - scroller.Viewport.Width;
            }
            scroller.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));
        }
        InvalidateVisual();
        CurrentRowChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls sideways so <paramref name="column"/> is on screen.</summary>
    private void RevealColumn(int column)
    {
        if (Scroller is not { } scroller || column < 0 || column >= widths.Length)
            return;
        double left = NumberWidth + widths.Take(column).Sum();
        double right = left + widths[column];
        double x = scroller.Offset.X;
        if (left - NumberWidth < x)
            x = left - NumberWidth;
        else if (right > x + scroller.Viewport.Width)
            x = right - scroller.Viewport.Width;
        scroller.Offset = scroller.Offset.WithX(Math.Max(0, x));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double viewport = Scroller?.Viewport.Width is > 0 and var width ? width : 800;
        // Room past the last row and column for the scroll bars, which float over the table.
        return new Size(Math.Max(viewport, NumberWidth + widths.Sum() + 16 + Ui.Theme.ScrollbarWidth),
            HeaderHeight + RecordCount * RowHeight + 8 + Ui.Theme.ScrollbarWidth);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // A clear fill over the whole view, so a click anywhere on it lands, not just on the values.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (table is null || table.Rows.Count == 0)
            return;
        Vector offset = Scroller?.Offset ?? default;
        Size viewport = Scroller?.Viewport ?? Bounds.Size;
        double size = Ui.Theme.FontSize;
        double numberWidth = NumberWidth;
        // A screen above and below the visible one too, so a scroll never uncovers blank rows before this is drawn.
        int first = Math.Max(1, 1 + (int)Math.Floor((offset.Y - viewport.Height) / RowHeight));
        int last = Math.Min(table.Rows.Count - 1, 1 + (int)Math.Ceiling((offset.Y + 2 * viewport.Height) / RowHeight));
        double textTop = Math.Round((RowHeight - TextViewerInk.Text("0", size, Ui.Theme.Text).Height) / 2);

        for (int row = first; row <= last; row++)
        {
            double top = HeaderHeight + (row - 1) * RowHeight;
            if (row == current)
                context.FillRectangle(Ui.Theme.HoverBg, new Rect(offset.X + 4, top + 1, viewport.Width - 8, RowHeight - 2), (float)Ui.Theme.ItemRadius);
            double left = numberWidth;
            string[] fields = table.Rows[row];
            for (int column = 0; column < widths.Length; column++)
            {
                string value = column < fields.Length ? fields[column] : "";
                if (left + widths[column] >= offset.X - viewport.Width && left <= offset.X + 2 * viewport.Width && value.Length > 0)
                    DrawCell(context, row, column, value, left, top, textTop, size);
                left += widths[column];
            }
        }

        // The row numbers stay at the left, the titles at the top.
        context.FillRectangle(Ui.Theme.PanelBg, new Rect(offset.X, Math.Max(HeaderHeight, offset.Y - viewport.Height), numberWidth, 3 * viewport.Height));
        for (int row = first; row <= last; row++)
        {
            double top = HeaderHeight + (row - 1) * RowHeight;
            if (row == current)
                context.DrawRectangle(Ui.Theme.HoverBg, null, new RoundedRect(new Rect(offset.X + 4, top + 1, numberWidth - 4, RowHeight - 2), new CornerRadius(Ui.Theme.ItemRadius, 0, 0, Ui.Theme.ItemRadius)));
            FormattedText number = TextViewerInk.Text(row.ToString(), Ui.Theme.SmallFontSize, row == current ? Ui.Theme.TextDim : Ui.Theme.TextFaint);
            context.DrawText(number, new Point(offset.X + numberWidth - 12 - number.WidthIncludingTrailingWhitespace, top + Math.Round((RowHeight - number.Height) / 2)));
        }
        context.FillRectangle(Ui.Theme.ListHeaderBg, new Rect(offset.X, offset.Y, viewport.Width, HeaderHeight));
        context.FillRectangle(Ui.Theme.BorderHard, new Rect(offset.X, offset.Y + HeaderHeight - 1, viewport.Width, 1));
        double headerLeft = numberWidth;
        string[] titles = table.Rows[0];
        for (int column = 0; column < widths.Length; column++)
        {
            double x = headerLeft - offset.X;
            if (headerLeft + widths[column] >= offset.X && x <= viewport.Width)
            {
                if (column > 0)
                    context.FillRectangle(Ui.Theme.Guide, new Rect(headerLeft, offset.Y + (HeaderHeight - 14) / 2, 1, 14));
                string title = column < titles.Length ? titles[column].ToUpperInvariant() : "";
                string shownTitle = Fit(title, widths[column]);
                FormattedText text = TextViewerInk.Text(shownTitle, Ui.Theme.SmallFontSize, Ui.Theme.TextDim);
                text.SetFontWeight(FontWeight.Bold);
                // A match in a title is marked as in a cell, the current one washed.
                int at = query.Length > 0 ? shownTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) : -1;
                if (at >= 0)
                {
                    text.SetForegroundBrush(Ui.Theme.Brass, at, query.Length);
                    text.SetTextDecorations(TextViewerInk.Underline, at, query.Length);
                    if (currentMatch >= 0 && currentMatch < matches.Count && matches[currentMatch] == (0, column))
                        context.FillRectangle(TextViewerInk.MatchWash, new Rect(headerLeft + 4, offset.Y + 3, widths[column] - 8, HeaderHeight - 6), 2);
                }
                double textX = numeric[column] ? headerLeft + widths[column] - CellPadding - text.WidthIncludingTrailingWhitespace : headerLeft + CellPadding;
                context.DrawText(text, new Point(textX, offset.Y + Math.Round((HeaderHeight - text.Height) / 2)));
            }
            headerLeft += widths[column];
        }
        // The corner over the row numbers, so titles scrolling left pass under it.
        context.FillRectangle(Ui.Theme.ListHeaderBg, new Rect(offset.X, offset.Y, numberWidth, HeaderHeight - 1));
    }

    private void DrawCell(DrawingContext context, int row, int column, string value, double left, double top, double textTop, double size)
    {
        bool isMatch = query.Length > 0 && value.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool isCurrent = currentMatch >= 0 && currentMatch < matches.Count && matches[currentMatch] == (row, column);
        if (isCurrent)
            context.FillRectangle(TextViewerInk.MatchWash, new Rect(left + 4, top + 3, widths[column] - 8, RowHeight - 6), 2);
        IBrush ink = numeric[column] ? TextViewerInk.Number : column == 0 ? Ui.Theme.Text : Ui.Theme.TextRow;
        string shown = Fit(value, widths[column]);
        FormattedText text = TextViewerInk.Text(shown, size, ink);
        if (isMatch)
        {
            int at = shown.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                text.SetForegroundBrush(Ui.Theme.Brass, at, query.Length);
                text.SetTextDecorations(TextViewerInk.Underline, at, query.Length);
            }
        }
        double x = numeric[column] ? left + widths[column] - CellPadding - text.WidthIncludingTrailingWhitespace : left + CellPadding;
        context.DrawText(text, new Point(x, top + textTop));
    }

    /// <summary>The value cut to the column, with an ellipsis where it doesn't fit.</summary>
    private string Fit(string value, double width)
    {
        // A hair of slack, so a value exactly as long as its column is not cut by rounding.
        int room = Math.Max(1, (int)((width - 2 * CellPadding) / charWidth + 0.01));
        string single = value.ReplaceLineEndings(" ");
        return single.Length <= room ? single : single[..Math.Max(0, room - 1)] + "…";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || table is null)
            return;
        Focus(NavigationMethod.Pointer);
        Point at = e.GetPosition(this);
        double offsetY = Scroller?.Offset.Y ?? 0;
        if (at.Y - offsetY < HeaderHeight)
            return;
        MoveTo(1 + (int)Math.Floor((at.Y - HeaderHeight) / RowHeight));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (table is null || e.Handled)
            return;
        int page = Math.Max(1, (int)(((Scroller?.Viewport.Height ?? 400) - HeaderHeight) / RowHeight) - 1);
        int? next = e.Key switch
        {
            Key.Up => current - 1,
            Key.Down => current + 1,
            Key.PageUp => current - page,
            Key.PageDown => current + page,
            Key.Home when e.KeyModifiers.HasFlag(KeyModifiers.Control) => 1,
            Key.End when e.KeyModifiers.HasFlag(KeyModifiers.Control) => table.Rows.Count - 1,
            _ => null,
        };
        if (next is { } row)
        {
            MoveTo(row);
            e.Handled = true;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    /// <summary>Works out each column's width from its longest value, and which columns are all numbers.</summary>
    private void MeasureColumns()
    {
        if (table is null)
        {
            widths = [];
            numeric = [];
            return;
        }
        int columns = table.Columns;
        var longest = new int[columns];
        var allNumbers = Enumerable.Repeat(true, columns).ToArray();
        var anyValue = new bool[columns];
        for (int row = 0; row < Math.Min(table.Rows.Count, SampleRows); row++)
        {
            string[] fields = table.Rows[row];
            for (int column = 0; column < fields.Length; column++)
            {
                longest[column] = Math.Max(longest[column], Math.Min(MaxColumnChars, fields[column].Length));
                if (row == 0 || fields[column].Length == 0)
                    continue;
                anyValue[column] = true;
                if (!double.TryParse(fields[column], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    allNumbers[column] = false;
            }
        }
        widths = [.. longest.Select(chars => Math.Max(6, chars) * charWidth + 2 * CellPadding)];
        numeric = [.. allNumbers.Select((all, column) => all && anyValue[column])];
    }

    private static string Quote(string field, char separator) =>
        field.Contains(separator) || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
}
