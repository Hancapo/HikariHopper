using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// One icon-field cell (port of <c>EntryGridCell.qml</c>): the inset, rounded selection/hover fill,
/// the 34px glyph, the name (Text.WrapAnywhere, two lines, the second elided) and the size, or for a
/// folder its item count ("Empty" when it holds nothing).
/// Pointer input is routed by <see cref="EntryGrid"/>.
/// </summary>
public partial class EntryGridCell : UserControl
{
    private const double NameWidth = 130;
    private EntryItem? item;
    private EntryGrid? owner;

    public EntryGridCell()
    {
        InitializeComponent();
        double inset = Ui.Theme.GridCellInset;
        Fill.Width = Ui.Theme.GridCellWidth - 2 * inset;
        Fill.Height = Ui.Theme.GridCellHeight - 2 * inset;
        Canvas.SetLeft(Glyph, (Ui.Theme.GridCellWidth - Ui.Theme.GridGlyphSize) / 2);
        NameText.Width = NameWidth;
        SizeText.Width = Ui.Theme.GridCellWidth - 16;
    }

    public EntryItem? Item => item;

    /// <summary>The model index of this cell, or -1 when it is an empty slot of the last row.</summary>
    public int Index { get; private set; } = -1;

    internal void Attach(EntryGrid? grid, EntryItem? entry, int index)
    {
        if (item is not null)
            item.PropertyChanged -= OnItemChanged;
        owner = grid;
        item = entry;
        Index = entry is null ? -1 : index;
        IsVisible = entry is not null;
        if (item is not null)
            item.PropertyChanged += OnItemChanged;
        Refresh();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            RefreshFill();
    }

    private void RefreshFill() =>
        Fill.Background = item?.Selected == true ? Ui.Theme.Selection : IsPointerOver ? Ui.Theme.HoverBg : null;

    internal void Refresh()
    {
        if (item is null)
            return;
        bool selected = item.Selected;
        RefreshFill();
        Glyph.Kind = item.IsDirectory ? "folder" : item.Kind.Contains("Package", StringComparison.Ordinal) ? "archive" : "file";
        Glyph.FileKind = item.Kind;
        Glyph.Selected = selected;
        NameText.Foreground = selected ? Ui.Theme.SelectionText : Ui.Theme.TextRow;
        WrapName(NameText.Inlines!, item.Name, owner?.Bridge?.SearchQuery, selected ? Ui.Theme.SelectionText : Ui.Theme.Brass);
        SizeText.Text = item.SizeText;
        SizeText.Foreground = selected ? Ui.Theme.SelectionInk : Ui.Theme.TextFaint;
        AutomationProperties.SetName(this, item.Name);
    }

    /// <summary>
    /// Qt's <c>WrapAnywhere</c> + <c>maximumLineCount: 2</c> + <c>ElideRight</c> on the monospaced
    /// face: as many characters per line as fit, the remainder elided onto the second line, with
    /// the search match marked across the break.
    /// </summary>
    private void WrapName(InlineCollection inlines, string name, string? query, IBrush ink)
    {
        inlines.Clear();
        double advance = CharacterAdvance();
        int perLine = Math.Max(1, (int)Math.Floor(NameWidth / advance));
        (int at, int length) = EntryText.Match(name, query);
        var lines = new List<(int Start, int Length, bool Elided)>();
        if (name.Length <= perLine)
        {
            lines.Add((0, name.Length, false));
        }
        else
        {
            lines.Add((0, perLine, false));
            int rest = name.Length - perLine;
            if (rest <= perLine)
                lines.Add((perLine, rest, false));
            else
                lines.Add((perLine, Math.Max(0, (int)Math.Floor((NameWidth - advance) / advance)), true));
        }
        for (int index = 0; index < lines.Count; index++)
        {
            if (index > 0)
                inlines.Add(new LineBreak());
            (int start, int count, bool elided) = lines[index];
            AddMarked(inlines, name, start, start + count, at, at + length, ink);
            if (elided)
                inlines.Add(new Run("…"));
        }
    }

    private static void AddMarked(InlineCollection inlines, string name, int start, int end, int matchStart, int matchEnd, IBrush ink)
    {
        int cursor = start;
        if (matchStart >= 0)
        {
            int markStart = Math.Clamp(matchStart, start, end);
            int markEnd = Math.Clamp(matchEnd, start, end);
            if (markEnd > markStart)
            {
                if (markStart > cursor)
                    inlines.Add(new Run(name[cursor..markStart]));
                inlines.Add(new Run(name[markStart..markEnd])
                {
                    Foreground = ink,
                    FontWeight = FontWeight.Bold,
                    TextDecorations = TextDecorations.Underline,
                });
                cursor = markEnd;
            }
        }
        if (end > cursor)
            inlines.Add(new Run(name[cursor..end]));
    }

    private static double? advance;

    /// <summary>The whole-pixel advance of one Space Mono character at the small size.</summary>
    private static double CharacterAdvance()
    {
        if (advance is { } known)
            return known;
        var typeface = new Typeface(Ui.Theme.MonoFont);
        using var layout = new TextLayout("0000000000", typeface, Ui.Theme.SmallFontSize, Brushes.Black);
        double value = layout.WidthIncludingTrailingWhitespace / 10;
        advance = value > 0 ? value : 6;
        return advance.Value;
    }
}
