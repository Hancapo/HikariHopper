using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>A position in a <see cref="TextDocument"/>'s shown lines: a line and a column, both from 0.</summary>
public readonly record struct TextPosition(int Line, int Column) : IComparable<TextPosition>
{
    public int CompareTo(TextPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;
}

/// <summary>A found stretch of text: its line, where it starts and how long it is.</summary>
public readonly record struct TextMatch(int Line, int Column, int Length);

/// <summary>
/// The text itself, drawn a screen at a time so a file of any length scrolls at once: a gutter of line
/// numbers that stays put while the text scrolls sideways, the lines coloured by <see cref="TextHighlighter"/>,
/// the caret's line washed, a selection, and the find's matches. The type is monospaced, so columns,
/// wrapping and hit-testing are exact sums. It sits in <see cref="Scroller"/>, which it sizes itself for.
/// </summary>
public sealed class TextLinesView : Control
{
    private const double BaseFontSize = Ui.Theme.FontSize;
    private const double BaseLineHeight = 20;
    private const double TextPadding = 10;

    private TextDocument? document;
    private byte[] states = [];
    private readonly Dictionary<int, List<TextSpan>> spans = [];
    private IReadOnlyList<TextMatch> matches = [];
    private int currentMatch = -1;
    private bool wrap;
    private double zoom = 1;
    private double charWidth;
    private int maxColumns;
    // With wrapping, the first row of each line, and the row count after the last.
    private int[] rowStarts = [];
    private int columnsPerRow = int.MaxValue;
    private TextPosition anchor, caret;
    private bool selecting;

    public TextLinesView()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ClipToBounds = false;
        UpdateMetrics();
    }

    /// <summary>The caret moved, or the selection changed.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>The scroll viewer this view is the content of, which it draws a screen of.</summary>
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

    public TextDocument? Document
    {
        get => document;
        set
        {
            document = value;
            spans.Clear();
            states = value is null ? [] : TextHighlighter.States(value.Format, value.Lines);
            maxColumns = value is null || value.Lines.Count == 0 ? 0 : value.Lines.Max(line => line.Length);
            anchor = caret = default;
            matches = [];
            currentMatch = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Whether long lines wrap at the view's width, a character at a time.</summary>
    public bool Wrap
    {
        get => wrap;
        set
        {
            if (wrap == value)
                return;
            wrap = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The type's scale: 1 is 12px type on 20px lines.</summary>
    public double Zoom
    {
        get => zoom;
        set
        {
            if (Math.Abs(zoom - value) < 1e-9)
                return;
            zoom = value;
            UpdateMetrics();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double FontSize => Math.Round(BaseFontSize * zoom * 2) / 2;

    public double LineHeight => Math.Round(BaseLineHeight * zoom);

    public TextPosition Caret => caret;

    /// <summary>Where the selection starts and ends, in order; equal when nothing is selected.</summary>
    public (TextPosition Start, TextPosition End) Selection => anchor <= caret ? (anchor, caret) : (caret, anchor);

    public bool HasSelection => anchor != caret;

    /// <summary>The selected text, lines joined by the file's own line ending.</summary>
    public string SelectedText
    {
        get
        {
            if (document is null || !HasSelection)
                return "";
            (TextPosition start, TextPosition end) = Selection;
            string newline = document.LineEnding == "CRLF" ? "\r\n" : document.LineEnding == "CR" ? "\r" : "\n";
            var builder = new System.Text.StringBuilder();
            for (int line = start.Line; line <= end.Line; line++)
            {
                string text = document.Lines[line];
                int from = line == start.Line ? Math.Min(start.Column, text.Length) : 0;
                int to = line == end.Line ? Math.Min(end.Column, text.Length) : text.Length;
                builder.Append(text, from, Math.Max(0, to - from));
                if (line < end.Line)
                    builder.Append(newline);
            }
            return builder.ToString();
        }
    }

    /// <summary>The width of the gutter, which grows with the number of digits in the last line number.</summary>
    public double GutterWidth => (Math.Max(3, ((document?.Lines.Count ?? 0) + 1).ToString().Length) * charWidth) + 26;

    /// <summary>The find's matches, and which one is current (-1 for none).</summary>
    public void SetMatches(IReadOnlyList<TextMatch> found, int current)
    {
        matches = found;
        currentMatch = current;
        if (current >= 0 && current < found.Count)
        {
            TextMatch match = found[current];
            anchor = new TextPosition(match.Line, match.Column);
            caret = new TextPosition(match.Line, match.Column + match.Length);
            BringIntoView(caret, centre: true);
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (document is null)
            return;
        anchor = default;
        int last = document.Lines.Count - 1;
        caret = new TextPosition(last, document.Lines[last].Length);
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the caret (and the selection's anchor, unless <paramref name="extend"/>) to <paramref name="position"/>.</summary>
    public void MoveCaret(TextPosition position, bool extend = false)
    {
        if (document is null)
            return;
        int line = Math.Clamp(position.Line, 0, document.Lines.Count - 1);
        caret = new TextPosition(line, Math.Clamp(position.Column, 0, document.Lines[line].Length));
        if (!extend)
            anchor = caret;
        BringIntoView(caret, centre: false);
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int lines = document?.Lines.Count ?? 0;
        double viewport = Scroller?.Viewport.Width is > 0 and var width ? width : double.IsFinite(availableSize.Width) ? availableSize.Width : 800;
        if (wrap)
        {
            columnsPerRow = Math.Max(8, (int)Math.Floor((viewport - GutterWidth - 2 * TextPadding) / charWidth));
            rowStarts = new int[lines + 1];
            int row = 0;
            for (int index = 0; index < lines; index++)
            {
                rowStarts[index] = row;
                row += Math.Max(1, (document!.Lines[index].Length + columnsPerRow - 1) / columnsPerRow);
            }
            rowStarts[lines] = row;
            // Room under the last line for the scroll bar, which floats over the content.
            return new Size(viewport, row * LineHeight + 2 * TextPadding + Ui.Theme.ScrollbarWidth);
        }
        columnsPerRow = int.MaxValue;
        rowStarts = [];
        return new Size(Math.Max(viewport, GutterWidth + maxColumns * charWidth + 2 * TextPadding + 40),
            lines * LineHeight + 2 * TextPadding + Ui.Theme.ScrollbarWidth);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (document is null)
            return;
        Vector offset = Scroller?.Offset ?? default;
        Size viewport = Scroller?.Viewport ?? Bounds.Size;
        double lineHeight = LineHeight, size = FontSize, gutter = GutterWidth;
        double textLeft = gutter + TextPadding;
        int totalRows = wrap ? rowStarts[^1] : document.Lines.Count;
        int firstRow = Math.Max(0, (int)Math.Floor((offset.Y - TextPadding) / lineHeight));
        int lastRow = Math.Min(totalRows - 1, (int)Math.Ceiling((offset.Y + viewport.Height - TextPadding) / lineHeight));
        (TextPosition selStart, TextPosition selEnd) = Selection;
        // The type's own line box, centred on the row.
        double textBaseline = Math.Round((lineHeight - TextViewerInk.Text("0", size, Ui.Theme.Text).Height) / 2);

        for (int row = firstRow; row <= lastRow; row++)
        {
            (int line, int chunk) = LineOfRow(row);
            string text = document.Lines[line];
            int from = wrap ? chunk * columnsPerRow : 0;
            int to = wrap ? Math.Min(text.Length, from + columnsPerRow) : text.Length;
            double top = TextPadding + row * lineHeight;

            // The caret's line, across the whole view.
            if (line == caret.Line)
                context.FillRectangle(Ui.Theme.HoverBg, new Rect(offset.X, top, viewport.Width, lineHeight));

            // The selection, then the matches, under the text.
            if (HasSelection && line >= selStart.Line && line <= selEnd.Line)
            {
                int a = line == selStart.Line ? selStart.Column : 0;
                int b = line == selEnd.Line ? selEnd.Column : text.Length + 1;
                a = Math.Max(a, from);
                b = Math.Min(b, wrap ? to + (to == text.Length ? 1 : 0) : b);
                if (b > a)
                    context.FillRectangle(TextViewerInk.SelectionWash, new Rect(textLeft + (a - from) * charWidth, top, (b - a) * charWidth, lineHeight));
            }
            for (int index = FirstMatchOn(line); index >= 0 && index < matches.Count && matches[index].Line == line; index++)
            {
                if (index != currentMatch)
                    continue;
                TextMatch match = matches[index];
                int a = Math.Max(match.Column, from), b = Math.Min(match.Column + match.Length, to);
                if (b > a)
                    context.FillRectangle(TextViewerInk.MatchWash, new Rect(textLeft + (a - from) * charWidth, top + 1, (b - a) * charWidth, lineHeight - 2), 2);
            }

            if (to > from)
            {
                FormattedText formatted = TextViewerInk.Text(text[from..to], size, Ui.Theme.TextRow);
                foreach (TextSpan span in SpansOf(line))
                {
                    int a = Math.Max(span.Start, from), b = Math.Min(span.Start + span.Length, to);
                    if (b > a)
                        formatted.SetForegroundBrush(TextViewerInk.Of(span.Kind), a - from, b - a);
                }
                for (int index = FirstMatchOn(line); index >= 0 && index < matches.Count && matches[index].Line == line; index++)
                {
                    TextMatch match = matches[index];
                    int a = Math.Max(match.Column, from), b = Math.Min(match.Column + match.Length, to);
                    if (b <= a)
                        continue;
                    formatted.SetForegroundBrush(Ui.Theme.Brass, a - from, b - a);
                    formatted.SetTextDecorations(TextViewerInk.Underline, a - from, b - a);
                }
                context.DrawText(formatted, new Point(textLeft, top + textBaseline));
            }

            // The caret, while the view has focus.
            if (IsFocused && line == caret.Line && caret.Column >= from && (caret.Column < to || caret.Column == to && (to == text.Length || !wrap)))
                context.FillRectangle(Ui.Theme.Text, new Rect(Math.Round(textLeft + (caret.Column - from) * charWidth), top + 3, 1, lineHeight - 6));
        }

        // The gutter, kept at the left edge however far the text has scrolled.
        context.FillRectangle(Ui.Theme.PanelBg, new Rect(offset.X, offset.Y, gutter, viewport.Height));
        context.FillRectangle(Ui.Theme.BorderSoft, new Rect(offset.X + gutter - 1, offset.Y, 1, viewport.Height));
        for (int row = firstRow; row <= lastRow; row++)
        {
            (int line, int chunk) = LineOfRow(row);
            if (chunk != 0)
                continue;
            FormattedText number = TextViewerInk.Text((line + 1).ToString(), size, line == caret.Line ? Ui.Theme.TextDim : Ui.Theme.TextFaint);
            context.DrawText(number, new Point(offset.X + gutter - 14 - number.WidthIncludingTrailingWhitespace, TextPadding + row * lineHeight + textBaseline));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed || document is null)
            return;
        Focus(NavigationMethod.Pointer);
        TextPosition at = PositionAt(e.GetPosition(this));
        if (e.ClickCount == 2)
        {
            SelectWord(at);
        }
        else if (e.ClickCount >= 3)
        {
            anchor = new TextPosition(at.Line, 0);
            caret = new TextPosition(at.Line, document.Lines[at.Line].Length);
            InvalidateVisual();
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            MoveCaret(at, extend: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            selecting = true;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (selecting)
            MoveCaret(PositionAt(e.GetPosition(this)), extend: true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        selecting = false;
        e.Pointer.Capture(null);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (document is null || e.Handled)
            return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        int page = Math.Max(1, (int)((Scroller?.Viewport.Height ?? 400) / LineHeight) - 1);
        string line = document.Lines[caret.Line];
        TextPosition? next = e.Key switch
        {
            Key.Left when ctrl => WordStep(caret, -1),
            Key.Right when ctrl => WordStep(caret, 1),
            Key.Left => caret.Column > 0 ? caret with { Column = caret.Column - 1 } : caret.Line > 0 ? new TextPosition(caret.Line - 1, document.Lines[caret.Line - 1].Length) : caret,
            Key.Right => caret.Column < line.Length ? caret with { Column = caret.Column + 1 } : caret.Line < document.Lines.Count - 1 ? new TextPosition(caret.Line + 1, 0) : caret,
            Key.Up => caret with { Line = caret.Line - 1 },
            Key.Down => caret with { Line = caret.Line + 1 },
            Key.PageUp => caret with { Line = caret.Line - page },
            Key.PageDown => caret with { Line = caret.Line + page },
            Key.Home when ctrl => new TextPosition(0, 0),
            Key.End when ctrl => new TextPosition(document.Lines.Count - 1, document.Lines[^1].Length),
            Key.Home => caret with { Column = line.Length - line.TrimStart().Length is var indent && caret.Column != indent ? indent : 0 },
            Key.End => caret with { Column = line.Length },
            _ => null,
        };
        if (next is { } position)
        {
            MoveCaret(position, extend: shift);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (wrap && e.ViewportDelta.X != 0)
            InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateMetrics() => charWidth = TextViewerInk.CharWidth(FontSize);

    private List<TextSpan> SpansOf(int line)
    {
        if (spans.TryGetValue(line, out List<TextSpan>? found))
            return found;
        if (spans.Count > 4096)
            spans.Clear();
        found = TextHighlighter.Spans(document!.Format, document.Lines[line], states[line], out _);
        spans[line] = found;
        return found;
    }

    /// <summary>The first match on <paramref name="line"/>, or -1; matches are in document order.</summary>
    private int FirstMatchOn(int line)
    {
        int low = 0, high = matches.Count - 1, found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (matches[middle].Line < line)
                low = middle + 1;
            else
            {
                if (matches[middle].Line == line)
                    found = middle;
                high = middle - 1;
            }
        }
        return found;
    }

    /// <summary>The line a row shows, and which of its wrapped pieces.</summary>
    private (int Line, int Chunk) LineOfRow(int row)
    {
        if (!wrap)
            return (row, 0);
        int low = 0, high = rowStarts.Length - 2;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (rowStarts[middle] <= row)
                low = middle;
            else
                high = middle - 1;
        }
        return (low, row - rowStarts[low]);
    }

    private TextPosition PositionAt(Point point)
    {
        int totalRows = wrap ? rowStarts[^1] : document!.Lines.Count;
        int row = Math.Clamp((int)Math.Floor((point.Y - TextPadding) / LineHeight), 0, Math.Max(0, totalRows - 1));
        (int line, int chunk) = LineOfRow(row);
        int column = (int)Math.Round((point.X - GutterWidth - TextPadding) / charWidth);
        if (wrap)
            column = Math.Min(Math.Max(0, column), columnsPerRow) + chunk * columnsPerRow;
        return new TextPosition(line, Math.Clamp(column, 0, document!.Lines[line].Length));
    }

    private void SelectWord(TextPosition at)
    {
        string line = document!.Lines[at.Line];
        int start = at.Column, end = at.Column;
        while (start > 0 && IsWordChar(line[start - 1]))
            start--;
        while (end < line.Length && IsWordChar(line[end]))
            end++;
        anchor = at with { Column = start };
        caret = at with { Column = end };
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private TextPosition WordStep(TextPosition origin, int direction)
    {
        string line = document!.Lines[origin.Line];
        int column = origin.Column;
        if (direction < 0)
        {
            if (column == 0)
                return origin.Line > 0 ? new TextPosition(origin.Line - 1, document.Lines[origin.Line - 1].Length) : origin;
            while (column > 0 && !IsWordChar(line[column - 1]))
                column--;
            while (column > 0 && IsWordChar(line[column - 1]))
                column--;
        }
        else
        {
            if (column == line.Length)
                return origin.Line < document.Lines.Count - 1 ? new TextPosition(origin.Line + 1, 0) : origin;
            while (column < line.Length && IsWordChar(line[column]))
                column++;
            while (column < line.Length && !IsWordChar(line[column]))
                column++;
        }
        return origin with { Column = column };
    }

    private static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';

    /// <summary>Scrolls so <paramref name="position"/> is on screen, in the middle when asked.</summary>
    private void BringIntoView(TextPosition position, bool centre)
    {
        if (Scroller is null || document is null)
            return;
        if (wrap && rowStarts.Length != document.Lines.Count + 1)
            return;
        int row = wrap ? rowStarts[position.Line] + Math.Min(position.Column / Math.Max(1, columnsPerRow), Math.Max(0, (document.Lines[position.Line].Length - 1) / Math.Max(1, columnsPerRow))) : position.Line;
        double top = TextPadding + row * LineHeight;
        double x = wrap ? 0 : GutterWidth + TextPadding + position.Column * charWidth;
        Vector offset = Scroller.Offset;
        Size viewport = Scroller.Viewport;
        double y = offset.Y;
        if (centre && (top < offset.Y || top + LineHeight > offset.Y + viewport.Height))
            y = top - viewport.Height / 2;
        else if (top < offset.Y)
            y = top;
        else if (top + LineHeight > offset.Y + viewport.Height)
            y = top + LineHeight - viewport.Height;
        double left = offset.X;
        double textArea = viewport.Width - GutterWidth;
        if (!wrap && x - GutterWidth < offset.X + TextPadding)
            left = Math.Max(0, x - GutterWidth - textArea / 3);
        else if (!wrap && x > offset.X + viewport.Width - TextPadding)
            left = x - viewport.Width + textArea / 3;
        Scroller.Offset = new Vector(Math.Max(0, left), Math.Max(0, y));
    }
}
