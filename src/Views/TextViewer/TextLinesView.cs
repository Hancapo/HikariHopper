using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    // Each drawn row's shaped, coloured text, by line and the columns drawn: a redraw then only shapes the rows
    // new to it. Cleared whenever what a row looks like can change (the text, the type's size, the matches).
    private readonly Dictionary<(int Line, int From, int To), FormattedText> shaped = [];
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
    // The stretch of the view the last render drew, a screen above and below the visible one: a vertical
    // scroll that stays well inside it only moves that drawing, and needs no new one.
    private double drawnTop = double.NaN, drawnBottom = double.NaN;

    // A drag selects by what the press picked: characters, words (double-click) or lines (triple-click, or
    // the gutter). The press's own word or line stays selected whichever way the drag goes.
    private enum DragUnit { Character, Word, Line }

    private bool selecting;
    private DragUnit dragUnit;
    private TextPosition pressStart, pressEnd;
    private Point lastPointer;
    private readonly DispatcherTimer autoScroll;
    private readonly DispatcherTimer blink;
    private bool caretShown = true;

    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);
    private static readonly Cursor GutterCursor = new(StandardCursorType.Arrow);

    public TextLinesView()
    {
        Focusable = true;
        Cursor = TextCursor;
        ClipToBounds = false;
        UpdateMetrics();
        // Dragging past an edge keeps scrolling, faster the further out the pointer is, until it comes back.
        autoScroll = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => AutoScrollTick());
        blink = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) =>
        {
            caretShown = !caretShown;
            InvalidateVisual();
        });
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
            shaped.Clear();
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
            shaped.Clear();
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

    /// <summary>Whether a drag selection is under way.</summary>
    internal bool IsSelecting => selecting;

    /// <summary>How many characters are selected, line breaks counting one each, without building the text.</summary>
    public long SelectionLength
    {
        get
        {
            if (document is null || !HasSelection)
                return 0;
            (TextPosition start, TextPosition end) = Selection;
            if (start.Line == end.Line)
                return end.Column - start.Column;
            long length = document.Lines[start.Line].Length - start.Column + 1;
            for (int line = start.Line + 1; line < end.Line; line++)
                length += document.Lines[line].Length + 1;
            return length + end.Column;
        }
    }

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
                int length = document.Lines[line].Length;
                int from = line == start.Line ? Math.Min(start.Column, length) : 0;
                int to = line == end.Line ? Math.Min(end.Column, length) : length;
                // From the file's own line, so tabs stay tabs.
                builder.Append(document.RawSlice(line, from, to));
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
        shaped.Clear();
        if (current >= 0 && current < found.Count)
        {
            TextMatch match = found[current];
            SetSelection(new TextPosition(match.Line, match.Column), new TextPosition(match.Line, match.Column + match.Length), scroll: false);
            BringIntoView(caret, centre: true);
        }
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (document is null)
            return;
        int last = document.Lines.Count - 1;
        SetSelection(default, new TextPosition(last, document.Lines[last].Length), scroll: false);
    }

    /// <summary>Moves the caret (and the selection's anchor, unless <paramref name="extend"/>) to <paramref name="position"/>.</summary>
    public void MoveCaret(TextPosition position, bool extend = false) => SetSelection(extend ? anchor : position, position, scroll: true);

    /// <summary>Selects from <paramref name="from"/> to <paramref name="to"/>, the caret at <paramref name="to"/>.</summary>
    private void SetSelection(TextPosition from, TextPosition to, bool scroll)
    {
        if (document is null)
            return;
        TextPosition newAnchor = Clamp(from), newCaret = Clamp(to);
        bool changed = newAnchor != anchor || newCaret != caret;
        anchor = newAnchor;
        caret = newCaret;
        if (scroll)
            BringIntoView(caret, centre: false);
        RestartBlink();
        InvalidateVisual();
        if (changed)
            CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private TextPosition Clamp(TextPosition position)
    {
        int line = Math.Clamp(position.Line, 0, document!.Lines.Count - 1);
        return new TextPosition(line, Math.Clamp(position.Column, 0, document.Lines[line].Length));
    }

    /// <summary>The caret shows at once after it moves, then blinks.</summary>
    private void RestartBlink()
    {
        caretShown = true;
        blink.Stop();
        if (IsFocused)
            blink.Start();
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
        // A clear fill over the whole view, so every point of it takes the pointer: without it only the
        // pixels drawn on (the letters) would, and a click between them would fall through.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (document is null)
            return;
        Vector offset = Scroller?.Offset ?? default;
        Size viewport = Scroller?.Viewport ?? Bounds.Size;
        double lineHeight = LineHeight, size = FontSize, gutter = GutterWidth;
        double textLeft = gutter + TextPadding;
        int totalRows = wrap ? rowStarts[^1] : document.Lines.Count;
        // A screen above and below the visible one too: a scroll moves the last frame before this one is drawn,
        // and it must not uncover blank rows meanwhile.
        int firstRow = Math.Max(0, (int)Math.Floor((offset.Y - viewport.Height - TextPadding) / lineHeight));
        int lastRow = Math.Min(totalRows - 1, (int)Math.Ceiling((offset.Y + 2 * viewport.Height - TextPadding) / lineHeight));
        Renders++;
        drawnTop = offset.Y - viewport.Height;
        drawnBottom = offset.Y + 2 * viewport.Height;
        (TextPosition selStart, TextPosition selEnd) = Selection;
        // The type's own line box, centred on the row.
        double textBaseline = Math.Round((lineHeight - TextViewerInk.Text("0", size, Ui.Theme.Text).Height) / 2);

        for (int row = firstRow; row <= lastRow; row++)
        {
            (int line, int chunk) = LineOfRow(row);
            string text = document.Lines[line];
            // Wrapped, the row's piece of the line; otherwise only the columns on screen and a screen either
            // side, so a minified file's one huge line costs no more than a short one.
            int from = wrap ? chunk * columnsPerRow
                : Math.Clamp((int)Math.Floor((offset.X - viewport.Width - textLeft) / charWidth), 0, text.Length);
            int to = wrap ? Math.Min(text.Length, from + columnsPerRow)
                : Math.Clamp((int)Math.Ceiling((offset.X + 2 * viewport.Width - textLeft) / charWidth), from, text.Length);
            double lineLeft = textLeft + (wrap ? 0 : from * charWidth);
            double top = TextPadding + row * lineHeight;

            // The caret's line, across the whole view.
            if (line == caret.Line)
                context.FillRectangle(Ui.Theme.HoverBg, new Rect(Math.Max(0, offset.X - viewport.Width), top, 3 * viewport.Width, lineHeight));

            // The selection, then the matches, under the text.
            if (HasSelection && line >= selStart.Line && line <= selEnd.Line)
            {
                int a = line == selStart.Line ? selStart.Column : 0;
                int b = line == selEnd.Line ? selEnd.Column : text.Length + 1;
                a = Math.Max(a, from);
                b = Math.Min(b, to + (to == text.Length ? 1 : 0));
                if (b > a)
                    context.FillRectangle(TextViewerInk.SelectionWash, new Rect(lineLeft + (a - from) * charWidth, top, (b - a) * charWidth, lineHeight), 2);
            }
            for (int index = FirstMatchOn(line); index >= 0 && index < matches.Count && matches[index].Line == line; index++)
            {
                if (index != currentMatch)
                    continue;
                TextMatch match = matches[index];
                int a = Math.Max(match.Column, from), b = Math.Min(match.Column + match.Length, to);
                if (b > a)
                    context.FillRectangle(TextViewerInk.MatchWash, new Rect(lineLeft + (a - from) * charWidth, top + 1, (b - a) * charWidth, lineHeight - 2), 2);
            }

            if (to > from)
                context.DrawText(Shaped(line, from, to, size), new Point(lineLeft, top + textBaseline));

            // The caret, while the view has focus.
            if (IsFocused && caretShown && line == caret.Line && caret.Column >= from && (caret.Column < to || caret.Column == to && (to == text.Length || !wrap)))
                context.FillRectangle(Ui.Theme.Text, new Rect(Math.Round(lineLeft + (caret.Column - from) * charWidth), top + 3, 1, lineHeight - 6));
        }

        // The gutter, kept at the left edge however far the text has scrolled.
        double gutterTop = Math.Max(0, offset.Y - viewport.Height);
        double gutterHeight = 3 * viewport.Height;
        context.FillRectangle(Ui.Theme.PanelBg, new Rect(offset.X, gutterTop, gutter, gutterHeight));
        context.FillRectangle(Ui.Theme.BorderSoft, new Rect(offset.X + gutter - 1, gutterTop, 1, gutterHeight));
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
        if (document is null)
            return;
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        Point point = e.GetPosition(this);
        TextPosition at = PositionAt(point);
        if (properties.IsRightButtonPressed)
        {
            // A right-click outside the selection moves the caret there first, so the menu acts where you clicked.
            (TextPosition start, TextPosition end) = Selection;
            if (!HasSelection || at < start || at > end)
                SetSelection(at, at, scroll: false);
            Focus(NavigationMethod.Pointer);
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;
        Focus(NavigationMethod.Pointer);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (InGutter(point))
        {
            // The line numbers pick whole lines, and a drag down them picks more.
            dragUnit = DragUnit.Line;
            (pressStart, pressEnd) = LineAround(at.Line);
            SetSelection(shift ? anchor : pressStart, pressEnd, scroll: false);
        }
        else if (e.ClickCount == 2)
        {
            dragUnit = DragUnit.Word;
            (pressStart, pressEnd) = WordAround(at);
            SetSelection(pressStart, pressEnd, scroll: false);
        }
        else if (e.ClickCount >= 3)
        {
            dragUnit = DragUnit.Line;
            (pressStart, pressEnd) = LineAround(at.Line);
            SetSelection(pressStart, pressEnd, scroll: false);
        }
        else
        {
            dragUnit = DragUnit.Character;
            pressStart = pressEnd = shift ? anchor : at;
            SetSelection(shift ? anchor : at, at, scroll: false);
        }
        selecting = true;
        lastPointer = point;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        if (!selecting)
        {
            Cursor = InGutter(point) ? GutterCursor : TextCursor;
            return;
        }
        lastPointer = point;
        ExtendTo(point);
        if (EdgeDistance(point) != default)
            autoScroll.Start();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!selecting)
            return;
        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Lost to something else (a window switch): the drag is over, whatever the button does next.
        EndDrag();
    }

    private void EndDrag()
    {
        selecting = false;
        autoScroll.Stop();
    }

    /// <summary>Extends the drag's selection to <paramref name="point"/>, by the unit the press picked.</summary>
    private void ExtendTo(Point point)
    {
        TextPosition at = PositionAt(point);
        switch (dragUnit)
        {
            case DragUnit.Word:
            {
                (TextPosition start, TextPosition end) = WordAround(at);
                if (at < pressStart)
                    SetSelection(pressEnd, start, scroll: false);
                else
                    SetSelection(pressStart, end > pressEnd ? end : pressEnd, scroll: false);
                break;
            }
            case DragUnit.Line:
            {
                (TextPosition start, TextPosition end) = LineAround(at.Line);
                if (at.Line < pressStart.Line)
                    SetSelection(pressEnd, start, scroll: false);
                else
                    SetSelection(pressStart, end, scroll: false);
                break;
            }
            default:
                SetSelection(pressStart, at, scroll: false);
                break;
        }
    }

    /// <summary>How far past the view's edges <paramref name="point"/> is: negative above or left, positive below or right.</summary>
    private Vector EdgeDistance(Point point)
    {
        if (Scroller is null)
            return default;
        Vector offset = Scroller.Offset;
        Size viewport = Scroller.Viewport;
        double x = point.X - offset.X, y = point.Y - offset.Y;
        double dy = y < 0 ? y : y > viewport.Height ? y - viewport.Height : 0;
        double dx = wrap || dragUnit == DragUnit.Line ? 0 : x < GutterWidth ? Math.Min(0, x - GutterWidth) : x > viewport.Width ? x - viewport.Width : 0;
        return new Vector(dx, dy);
    }

    private void AutoScrollTick()
    {
        if (!selecting || Scroller is null)
        {
            autoScroll.Stop();
            return;
        }
        Vector distance = EdgeDistance(lastPointer);
        if (distance == default)
        {
            autoScroll.Stop();
            return;
        }
        // Gentle near the edge, quick further out.
        static double Speed(double past) => past == 0 ? 0 : Math.Sign(past) * Math.Min(80, 2 + Math.Pow(Math.Abs(past), 1.35) * 0.35);
        Vector before = Scroller.Offset;
        Size extent = Scroller.Extent, viewport = Scroller.Viewport;
        var after = new Vector(
            Math.Clamp(before.X + Speed(distance.X), 0, Math.Max(0, extent.Width - viewport.Width)),
            Math.Clamp(before.Y + Speed(distance.Y), 0, Math.Max(0, extent.Height - viewport.Height)));
        if (after == before)
            return;
        Scroller.Offset = after;
        // The pointer stays put on screen while the text moves under it.
        lastPointer += after - before;
        ExtendTo(lastPointer);
    }

    private bool InGutter(Point point) => point.X - (Scroller?.Offset.X ?? 0) < GutterWidth;

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        blink.Stop();
        EndDrag();
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
        // Redrawing every row of three screens on each step of a smooth scroll costs more than a frame in a big
        // file, so the scroll stutters. A vertical scroll moves the last drawing instead, and a new one is made
        // only once the visible screen comes within half a screen of what was drawn. The gutter is drawn at
        // the horizontal offset, so a sideways scroll, and a new size, always redraw.
        if (e.OffsetDelta.X != 0 || e.ViewportDelta != default || e.ExtentDelta != default || NeedsRedraw())
            InvalidateVisual();
    }

    /// <summary>How many times the view has drawn its text, for tests.</summary>
    internal int Renders { get; private set; }

    /// <summary>Whether the visible screen is near the edge of the stretch the last render drew.</summary>
    internal bool NeedsRedraw()
    {
        if (Scroller is not { } scroller || double.IsNaN(drawnTop))
            return true;
        double top = scroller.Offset.Y, height = scroller.Viewport.Height;
        // Past the start or the end of the text there is nothing more to draw, however close the screen gets.
        bool nearTop = drawnTop > 0 && top - height / 2 < drawnTop;
        bool nearBottom = drawnBottom < scroller.Extent.Height && top + height * 1.5 > drawnBottom;
        return nearTop || nearBottom;
    }

    private void UpdateMetrics() => charWidth = TextViewerInk.CharWidth(FontSize);

    /// <summary>Columns <paramref name="from"/> to <paramref name="to"/> of a line, shaped in its colours and with its matches marked.</summary>
    private FormattedText Shaped(int line, int from, int to, double size)
    {
        if (shaped.TryGetValue((line, from, to), out FormattedText? cached))
            return cached;
        if (shaped.Count > 2048)
            shaped.Clear();
        string text = document!.Lines[line];
        FormattedText formatted = TextViewerInk.Text(text[from..to], size, Ui.Theme.TextRow);
        List<TextSpan> lineSpans = SpansOf(line);
        for (int index = FirstSpanEndingAfter(lineSpans, from); index < lineSpans.Count && lineSpans[index].Start < to; index++)
        {
            TextSpan span = lineSpans[index];
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
        shaped[(line, from, to)] = formatted;
        return formatted;
    }

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

    /// <summary>The first of <paramref name="spans"/> (in order, apart) that ends after <paramref name="column"/>.</summary>
    private static int FirstSpanEndingAfter(List<TextSpan> spans, int column)
    {
        int low = 0, high = spans.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (spans[middle].Start + spans[middle].Length <= column)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
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

    /// <summary>The word (or run of spaces, or of symbols) at <paramref name="at"/>.</summary>
    private (TextPosition Start, TextPosition End) WordAround(TextPosition at)
    {
        string line = document!.Lines[at.Line];
        if (line.Length == 0)
            return (at, at);
        int probe = Math.Min(at.Column, line.Length - 1);
        char kind = line[probe];
        Func<char, bool> same = IsWordChar(kind) ? IsWordChar
            : char.IsWhiteSpace(kind) ? char.IsWhiteSpace
            : value => !IsWordChar(value) && !char.IsWhiteSpace(value);
        int start = probe, end = probe + 1;
        while (start > 0 && same(line[start - 1]))
            start--;
        while (end < line.Length && same(line[end]))
            end++;
        return (at with { Column = start }, at with { Column = end });
    }

    /// <summary>A whole line with its line break: from its start to the next line's start (or its own end, if last).</summary>
    private (TextPosition Start, TextPosition End) LineAround(int line) =>
        line < document!.Lines.Count - 1
            ? (new TextPosition(line, 0), new TextPosition(line + 1, 0))
            : (new TextPosition(line, 0), new TextPosition(line, document.Lines[line].Length));

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
