using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>One key and what it does.</summary>
public sealed record ShortcutEntry(string Key, string Action);

/// <summary>
/// A titled block of the keyboard reference. One that is <paramref name="NewArea"/> starts another part of the app
/// (the explorer, the 3D viewer and texture editor, the text and image viewers), where a column may start.
/// </summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Keys, bool NewArea = false);

/// <summary>
/// The keyboard reference, shown as a sheet over the main window: the explorer's keys, then each viewer's and
/// editor's. Every entry is a shortcut the app actually binds.
/// </summary>
public partial class ShortcutsDialog : ModalSheet
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        SmoothScrolling.Attach(Scroller);
        foreach (ShortcutGroup group in Groups)
            Columns.Add(Group(group), group.NewArea);
    }

    public IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("FILE",
        [
            new("Ctrl+T", "New tab"),
            new("Ctrl+W", "Close tab"),
            new("Ctrl+O", "Open configured game"),
            new("Ctrl+Shift+O", "Open RPF archive"),
            new("Alt+F4", "Exit"),
        ]),
        new("GO",
        [
            new("Alt+Left", "Back"),
            new("Alt+Right", "Forward"),
            new("Backspace", "Up one level"),
            new("Enter", "Open the selected entry"),
            new("Ctrl+L · Alt+D", "Type or paste an address"),
            new("Ctrl+Shift+F", "Search the whole game"),
            new("Ctrl+Enter", "Open a search result's location"),
        ]),
        new("EDIT",
        [
            new("Ctrl+A", "Select all visible entries"),
            new("Ctrl+C", "Copy"),
            new("Ctrl+V", "Paste"),
            new("Ctrl+Shift+C", "Copy path"),
            new("F2", "Rename"),
            new("Del", "Delete the selected items"),
            new("Alt+Enter", "Properties"),
            new("F3 · Ctrl+F", "Search this folder"),
            new("Esc", "Leave the search"),
        ]),
        new("3D VIEWER",
        [
            new("P", "Show or hide the parts"),
            new("1 – 4", "Level of detail"),
            new("H", "Shadows"),
            new("T", "Embedded textures"),
            new("M", "Shader values"),
            new("X", "Damaged"),
            new("B", "Skeleton"),
            new("K", "Collision"),
            new("L", "Lights"),
            new("V", "Orbit or free camera"),
            new("F", "Frame the model"),
            new("W A S D · Q E", "Fly, with Shift faster"),
        ], NewArea: true),
        new("TEXTURE EDITOR",
        [
            new("Ctrl+S", "Save"),
            new("Ctrl+Shift+S", "Save as"),
            new("Ctrl+I", "Import images"),
            new("Ctrl+E", "Extract as DDS"),
            new("Ctrl+C", "Copy the image"),
            new("Ctrl+V", "Paste an image"),
            new("Ctrl+Z", "Undo"),
            new("F2", "Rename"),
            new("Del", "Remove the selected textures"),
            new("F", "Fit"),
            new("1", "Actual size"),
            new("Ctrl+W", "Close"),
        ]),
        new("TEXT VIEWER",
        [
            new("Ctrl+F", "Find"),
            new("F3 · Shift+F3", "Next or previous match"),
            new("Ctrl+C", "Copy"),
            new("Ctrl++ · Ctrl+-", "Zoom in or out"),
            new("Ctrl+0", "Reset the zoom"),
            new("Alt+Z", "Word wrap"),
        ], NewArea: true),
        new("IMAGE VIEWER",
        [
            new("F", "Fit"),
            new("1", "Actual size"),
            new("Esc", "Close"),
        ]),
    ];

    private static Control Group(ShortcutGroup group)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = group.Name, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = 9,
            FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (ShortcutEntry entry in group.Keys)
        {
            var row = new Grid { Height = ShortcutColumns.RowHeight, ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = entry.Key, Foreground = T.TextRow, FontFamily = T.MonoFont, FontSize = T.FontSize,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = entry.Action, Foreground = T.TextDim, FontFamily = T.UiFont, FontSize = T.FontSize,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                [Grid.ColumnProperty] = 1,
            });
            panel.Children.Add(row);
        }
        return panel;
    }
}

/// <summary>
/// The keyboard reference's groups in columns: as many as fit at <see cref="ColumnWidth"/>, three at most. The
/// groups keep their order and a column starts only where a part of the app does, at the starts that leave the
/// columns most even; a dotted rule goes between two groups of a column.
/// </summary>
public sealed class ShortcutColumns : Panel
{
    /// <summary>A row's height: a key and what it does.</summary>
    public const double RowHeight = 22;

    /// <summary>The least a column is wide: its keys and the longest action, unclipped.</summary>
    public const double ColumnWidth = 300;

    /// <summary>The room between two columns.</summary>
    public const double ColumnGap = 28;

    private const double RuleAbove = 14, RuleBelow = 10;

    private readonly List<Control> groups = [];
    private readonly List<bool> starts = [];
    private readonly List<DottedRule> rules = [];

    /// <summary>How many groups each column holds, as last laid out.</summary>
    internal IReadOnlyList<int> Split { get; private set; } = [];

    /// <summary>Adds a group, after the ones before it; a column may start at one that <paramref name="startsArea"/>.</summary>
    public void Add(Control group, bool startsArea = false)
    {
        // One rule for each gap there could be; a column's first group has none above it.
        if (groups.Count > 0)
        {
            var rule = new DottedRule { Horizontal = true };
            rules.Add(rule);
            Children.Add(rule);
        }
        groups.Add(group);
        starts.Add(startsArea);
        Children.Add(group);
    }

    /// <summary>How many columns fit across <paramref name="width"/>.</summary>
    public static int ColumnsFor(double width) =>
        double.IsInfinity(width) ? 3 : Math.Clamp((int)((width + ColumnGap) / (ColumnWidth + ColumnGap)), 1, 3);

    /// <summary>
    /// How many of <paramref name="heights"/> go in each of <paramref name="columns"/> columns, in order, so the
    /// tallest column is as short as it can be. A column starts only at a group <paramref name="starts"/> allows,
    /// so there are no more columns than that gives; <paramref name="gap"/> is the room between two in a column.
    /// </summary>
    internal static int[] Partition(IReadOnlyList<double> heights, IReadOnlyList<bool> starts, int columns, double gap)
    {
        columns = Math.Clamp(columns, 1, 1 + starts.Skip(1).Count(start => start));
        var best = new int[columns];
        double bestTallest = double.PositiveInfinity;
        var counts = new int[columns];
        void Place(int column, int from, double tallest)
        {
            if (column == columns - 1)
            {
                counts[column] = heights.Count - from;
                tallest = Math.Max(tallest, Height(from, heights.Count));
                if (tallest < bestTallest)
                {
                    bestTallest = tallest;
                    counts.CopyTo(best, 0);
                }
                return;
            }
            for (int to = from + 1; to < heights.Count; to++)
            {
                if (!starts[to])
                    continue;
                counts[column] = to - from;
                Place(column + 1, to, Math.Max(tallest, Height(from, to)));
            }
        }
        double Height(int from, int to)
        {
            double sum = Math.Max(0, to - from - 1) * gap;
            for (int index = from; index < to; index++)
                sum += heights[index];
            return sum;
        }
        if (heights.Count > 0)
            Place(0, 0, 0);
        return best;
    }

    private static double RuleGap(DottedRule rule) => RuleAbove + rule.DesiredSize.Height + RuleBelow;

    private (int Columns, double Width) Layout(double width)
    {
        int columns = Math.Min(ColumnsFor(width), 1 + starts.Skip(1).Count(start => start));
        double each = double.IsInfinity(width) ? ColumnWidth : Math.Max(0, (width - (columns - 1) * ColumnGap) / columns);
        return (columns, Math.Floor(each));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        (int columns, double width) = Layout(availableSize.Width);
        foreach (Control child in Children)
            child.Measure(new Size(width, double.PositiveInfinity));
        double gap = rules.Count > 0 ? RuleGap(rules[0]) : 0;
        Split = Partition([.. groups.Select(group => group.DesiredSize.Height)], starts, columns, gap);
        double tallest = 0;
        int at = 0;
        foreach (int count in Split)
        {
            tallest = Math.Max(tallest, groups.Skip(at).Take(count).Sum(group => group.DesiredSize.Height) + Math.Max(0, count - 1) * gap);
            at += count;
        }
        return new Size(columns * width + (columns - 1) * ColumnGap, tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (int columns, double width) = Layout(finalSize.Width);
        // The measured split, unless the width it was measured for gave another number of columns.
        IReadOnlyList<int> split = Split.Count == columns
            ? Split
            : Partition([.. groups.Select(group => group.DesiredSize.Height)], starts, columns, rules.Count > 0 ? RuleGap(rules[0]) : 0);
        int at = 0;
        for (int column = 0; column < split.Count; column++)
        {
            double x = column * (width + ColumnGap), y = 0;
            for (int index = at; index < at + split[column]; index++)
            {
                // The rule before a group is the one at its index, less one; a column's first group has it folded away.
                if (index > 0)
                {
                    DottedRule rule = rules[index - 1];
                    if (index == at)
                        rule.Arrange(default);
                    else
                    {
                        rule.Arrange(new Rect(x, y + RuleAbove, width, rule.DesiredSize.Height));
                        y += RuleGap(rule);
                    }
                }
                groups[index].Arrange(new Rect(x, y, width, groups[index].DesiredSize.Height));
                y += groups[index].DesiredSize.Height;
            }
            at += split[column];
        }
        return finalSize;
    }
}
