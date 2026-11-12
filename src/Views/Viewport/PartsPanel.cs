using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HikariHopper.Rendering;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The 3D viewer's parts panel: a drawable's drawables, models and geometries at the level of detail picked, as a tree
/// with dotted guides. Each row has an eye that shows or hides it (dimmed while only some of what is under it shows), a
/// badge for what it is, and its triangles. Rows are selected apart from what is shown: click, Ctrl and Shift; the
/// keyboard moves through them and shows, hides and frames what is selected; a right click offers the same. A filter
/// over the rows narrows them by name. Rows come in sections: a fragment's body, its groups of drawables and its extras
/// (toggles numbered as the game numbers them); a drawable dictionary's drawables by what they are, which can show one
/// of each at a time. A fragment with a damaged state has a switch between it intact and damaged, and a drawable with
/// embedded textures a row at the panel's foot that opens them. A collision or a navmesh lists what its colours mean instead: its materials, or its kinds of
/// ground, edges, links and cover points, each row with its colour in place of the badge and how many wear it.
/// </summary>
internal sealed class PartsPanel : UserControl
{
    private const double RowHeight = 22;
    private const double Indent = 14;

    private readonly StackPanel rows = new();
    private readonly TextBlock counts = new();
    private readonly HashSet<string> expanded = [];
    private readonly HashSet<string> swept = [];
    private readonly HashSet<string> selected = [];
    private IPartList? parts;
    // While a press on an eye sweeps over the rows: what each is set to.
    private bool? sweepTo;
    // Where the keyboard is, and where a Shift range starts from.
    private string? cursor, anchor;
    private DropdownWindow? menu;
    // The rows as listed, for finding the one under the pointer while sweeping: section headings sit between them.
    private readonly List<(Control Row, int Index)> rowControls = [];
    private IReadOnlyList<PartSection> sections = [];
    // For a drawable dictionary: one drawable of each section shown at a time, or all of them.
    private bool oneAtATime = true;

    public PartsPanel()
    {
        Background = Ui.Theme.AppBg;
        Focusable = true;
        var title = new TextBlock
        {
            Text = "PARTS",
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.4,
            Foreground = Ui.Theme.TextDim,
            VerticalAlignment = VerticalAlignment.Center,
        };
        counts.FontFamily = Ui.Theme.MonoFont;
        counts.FontSize = Ui.Theme.SmallFontSize;
        counts.Foreground = Ui.Theme.TextFaint;
        counts.VerticalAlignment = VerticalAlignment.Center;
        counts.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Border
        {
            Height = Ui.Theme.ListHeaderHeight,
            Background = Ui.Theme.ListHeaderBg,
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new Panel { Children = { title, counts } },
        };
        DockPanel.SetDock(header, Dock.Top);

        Filter.PlaceholderText = "Filter parts";
        Filter.LeadingIcon = "search";
        Filter.Height = 26;
        Filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Refresh();
        };
        Filter.KeyDown += OnFilterKeyDown;
        AutomationProperties.SetName(Filter, "Filter parts");
        ExpandButton.Click += (_, _) => ExpandAll(!AllExpanded);
        ShowAllButton.Click += (_, _) => ShowAll();
        ThemedToolTip.Set(ShowAllButton, "Show all");
        AutomationProperties.SetName(ShowAllButton, "Show all");
        ModeSwitch.Sides.Add(new SwitchSide { Text = "One", Width = 38, Label = "One of each", ToolTip = "One drawable of each kind at a time" });
        ModeSwitch.Sides.Add(new SwitchSide { Text = "All", Width = 34, Label = "All", ToolTip = "Every drawable, one over another" });
        ModeSwitch.SelectedIndex = 0;
        ModeSwitch.VerticalAlignment = VerticalAlignment.Center;
        ModeSwitch.HorizontalAlignment = HorizontalAlignment.Right;
        ModeSwitch.SideRequested += (_, index) => SetOneAtATime(index == 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 0, 0, 0), Children = { ExpandButton, ShowAllButton } };
        // A drawable dictionary's row under the filter: one drawable of each kind shown, or all of them.
        ModeRow.Margin = new Thickness(0, 6, 0, 0);
        ModeRow.IsVisible = false;
        ModeRow.Children.Add(new TextBlock
        {
            Text = "SHOW",
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.CapsFontSize,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2,
            Foreground = Ui.Theme.TextFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        });
        ModeRow.Children.Add(ModeSwitch);
        DockPanel.SetDock(buttons, Dock.Right);
        var tools = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = Ui.Theme.BorderHard,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { new DockPanel { Children = { buttons, Filter } }, ModeRow } },
        };
        DockPanel.SetDock(tools, Dock.Top);

        // The drawable's embedded textures, at the panel's foot: how many, and a press opens them.
        var texturesIcon = new LucideIcon
        {
            IconName = "image", Width = 14, Height = 14, Weight = 1.6, Stroke = Ui.Theme.InkAsset,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        var texturesGo = new LucideIcon
        {
            IconName = "chevron-right", Width = 11, Height = 11, Weight = 1.8, Stroke = Ui.Theme.TextDim,
            VerticalAlignment = VerticalAlignment.Center,
        };
        texturesCount.FontFamily = Ui.Theme.MonoFont;
        texturesCount.FontSize = Ui.Theme.SmallFontSize;
        texturesCount.Foreground = Ui.Theme.TextFaint;
        texturesCount.VerticalAlignment = VerticalAlignment.Center;
        texturesCount.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(texturesIcon, Dock.Left);
        DockPanel.SetDock(texturesGo, Dock.Right);
        var texturesHit = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = Ui.Theme.ItemCorner,
            Padding = new Thickness(8, 0, 6, 0),
            Height = 26,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel
            {
                Children =
                {
                    texturesIcon,
                    texturesGo,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Embedded textures", FontFamily = Ui.Theme.UiFont, FontSize = Ui.Theme.FontSize,
                                Foreground = Ui.Theme.TextRow, VerticalAlignment = VerticalAlignment.Center,
                            },
                            texturesCount,
                        },
                    },
                },
            },
        };
        texturesHit.PointerEntered += (_, _) => texturesHit.Background = Ui.Theme.HoverBg;
        texturesHit.PointerExited += (_, _) => texturesHit.Background = Brushes.Transparent;
        PointerClick.Attach(texturesHit, _ => TexturesRequested?.Invoke());
        ThemedToolTip.Set(texturesHit, "Open the embedded textures  (T)");
        AutomationProperties.SetName(texturesHit, "Embedded textures");
        TexturesRow.Child = texturesHit;
        TexturesRow.Padding = new Thickness(4, 3, 4, 3);
        TexturesRow.BorderBrush = Ui.Theme.BorderHard;
        TexturesRow.BorderThickness = new Thickness(0, 1, 0, 0);
        TexturesRow.IsVisible = false;
        DockPanel.SetDock(TexturesRow, Dock.Bottom);

        rows.Margin = new Thickness(0, 4, 0, 4);
        rows.Background = Brushes.Transparent;
        rows.PointerMoved += (_, e) =>
        {
            if (sweepTo is null)
                return;
            double y = e.GetPosition(rows).Y;
            foreach ((Control row, int index) in rowControls)
                if (y >= row.Bounds.Top && y < row.Bounds.Bottom)
                {
                    SweepOver(index);
                    break;
                }
        };
        rows.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            EndSweep();
        };
        rows.PointerCaptureLost += (_, _) => EndSweep();
        Content = new DockPanel
        {
            Children = { header, tools, TexturesRow, Scroller(rows) },
        };
        GotFocus += (_, _) => Refresh();
        LostFocus += (_, _) => Refresh();
    }

    private static ChromeToolButton ToolButton(string icon) => new()
    {
        Width = 26,
        Height = 26,
        Bordered = false,
        InWell = true,
        CornerRadius = Ui.Theme.InnerCorner,
        IconKind = icon,
        IconSize = 14,
        IconThickness = 1.5,
        Focusable = false,
    };

    // The app's quiet scroller, its bar over the rows while it's used.
    private static ScrollViewer Scroller(Control rows)
    {
        var scroller = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        if (Application.Current?.TryFindResource("QuietOverlayScrollViewer", out object? theme) == true && theme is Avalonia.Styling.ControlTheme quiet)
            scroller.Theme = quiet;
        return scroller;
    }

    private readonly TextBlock texturesCount = new();

    /// <summary>The embedded textures' row at the panel's foot: there for a drawable that has some that can be opened.</summary>
    internal Border TexturesRow { get; } = new();

    /// <summary>How many embedded textures the row says there are, as shown.</summary>
    internal string TexturesCountText => texturesCount.Text ?? "";

    /// <summary>Raised when the embedded textures' row is pressed.</summary>
    public event Action? TexturesRequested;

    /// <summary>Asks for the embedded textures, as a press on their row does.</summary>
    internal void RequestTextures()
    {
        if (TexturesRow.IsVisible)
            TexturesRequested?.Invoke();
    }

    /// <summary>Shows the embedded textures' row with how many there are; none hides it.</summary>
    public void ShowTextures(int count)
    {
        TexturesRow.IsVisible = count > 0;
        texturesCount.Text = count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Raised when the user shows or hides parts.</summary>
    public event Action? Changed;

    /// <summary>Raised to turn the camera to these rows' parts: double-click, Enter, or Frame in the menu.</summary>
    public event Action<IReadOnlyList<PartNode>>? FrameRequested;

    /// <summary>The filter over the rows, by name.</summary>
    internal FlatTextField Filter { get; } = new();

    /// <summary>Opens every row, or closes them all when they're all open.</summary>
    internal ChromeToolButton ExpandButton { get; } = ToolButton("expand-all");

    /// <summary>Shows every part.</summary>
    internal ChromeToolButton ShowAllButton { get; } = ToolButton("show-all");

    /// <summary>A drawable dictionary's switch: one drawable of each section shown at a time, or all of them.</summary>
    internal SegmentedSwitch ModeSwitch { get; } = new();

    /// <summary>The row the switch sits in, shown for a drawable dictionary only.</summary>
    internal Panel ModeRow { get; } = new();

    /// <summary>The sections listed.</summary>
    internal IReadOnlyList<PartSection> Sections => sections;

    /// <summary>Whether a drawable dictionary shows one drawable of each section at a time.</summary>
    internal bool OneAtATime => oneAtATime;

    /// <summary>
    /// The parts shown, or none; the panel lists them, the body's rows open, nothing selected. A drawable dictionary
    /// shows one drawable of each section until All is picked.
    /// </summary>
    public IPartList? Parts
    {
        get => parts;
        set
        {
            parts = value;
            expanded.Clear();
            selected.Clear();
            cursor = anchor = null;
            sections = value?.Sections() ?? [];
            foreach (PartSection section in sections)
                if (section.Kind == PartSectionKind.Tree && section.Title is "" or "Body")
                    foreach (PartNode node in section.Nodes)
                        expanded.Add(node.Key);
                // A fragment's state shown is open, the other closed.
                else if (section.Kind == PartSectionKind.States)
                    foreach (PartNode node in section.Nodes.Where(node => value!.State(node) != false))
                        expanded.Add(node.Key);
            ModeRow.IsVisible = HasVariants;
            if (HasVariants && oneAtATime)
                ApplyOneAtATime();
            Refresh();
        }
    }

    private bool HasVariants => sections.Any(section => section.Kind == PartSectionKind.Variants);

    /// <summary>The rows of the sections listed as trees, every level down, for opening and closing them all.</summary>
    private IEnumerable<PartNode> TreeNodes()
    {
        IEnumerable<PartNode> All(PartNode node) => [node, .. node.Children.SelectMany(All)];
        return sections.Where(section => section.Kind != PartSectionKind.Extras).SelectMany(section => section.Nodes).SelectMany(All);
    }

    /// <summary>Shows one drawable of each section at a time, or all of them.</summary>
    internal void SetOneAtATime(bool one)
    {
        oneAtATime = one;
        ModeSwitch.SelectedIndex = one ? 0 : 1;
        if (parts is null || !HasVariants)
            return;
        if (one)
            ApplyOneAtATime();
        else
            parts.ShowAll();
        Changed?.Invoke();
        Refresh();
    }

    // Each section keeps one drawable shown: the first that already is, or its first.
    private void ApplyOneAtATime()
    {
        if (parts is null)
            return;
        foreach (PartSection section in sections.Where(section => section.Kind == PartSectionKind.Variants))
        {
            PartNode keep = section.Nodes.FirstOrDefault(node => parts.State(node) == true) ?? section.Nodes[0];
            foreach (PartNode node in section.Nodes)
                parts.Show(node, ReferenceEquals(node, keep));
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The section a row picks one of: a fragment's states, always, or a variant section while one of each shows at a
    /// time; none otherwise.
    /// </summary>
    private PartSection? RadioSection(PartNode node) => sections.FirstOrDefault(section =>
        (section.Kind == PartSectionKind.States || (oneAtATime && section.Kind == PartSectionKind.Variants)) && section.Nodes.Any(each => each.Key == node.Key));

    /// <summary>Shows a drawable of a variant section in place of the one shown.</summary>
    internal void Pick(PartNode node)
    {
        if (parts is null || RadioSection(node) is not { } section)
            return;
        foreach (PartNode each in section.Nodes)
        {
            parts.Show(each, each.Key == node.Key);
            // A fragment's state picked opens, and the other closes.
            if (section.Kind == PartSectionKind.States && each.Children.Count > 0)
                if (each.Key == node.Key)
                    expanded.Add(each.Key);
                else
                    expanded.Remove(each.Key);
        }
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Shows a fragment intact or damaged, as picking its state's row does.</summary>
    internal void PickState(bool damaged)
    {
        string key = damaged ? PartVisibility.DamagedKey : PartVisibility.IntactKey;
        if (sections.Where(section => section.Kind == PartSectionKind.States).SelectMany(section => section.Nodes).FirstOrDefault(node => node.Key == key) is { } state)
            Pick(state);
    }

    /// <summary>The rows shown, in order, for tests: each a node and whether it is open.</summary>
    internal IReadOnlyList<(PartNode Node, bool Open)> Rows { get; private set; } = [];

    /// <summary>The header's count: geometries shown of those at this level.</summary>
    internal string CountText => counts.Text ?? "";

    /// <summary>The rows selected, in the order they're listed.</summary>
    internal IReadOnlyList<PartNode> Selection => [.. Rows.Select(row => row.Node).Where(node => selected.Contains(node.Key))];

    /// <summary>The menu a right click opened, while it's open.</summary>
    internal DropdownWindow? Menu => menu;

    private bool AllExpanded
    {
        get
        {
            if (parts is null)
                return false;
            return TreeNodes().Where(node => node.Children.Count > 0).All(node => expanded.Contains(node.Key));
        }
    }

    /// <summary>Opens or closes a row.</summary>
    internal void Toggle(PartNode node)
    {
        if (!expanded.Remove(node.Key))
            expanded.Add(node.Key);
        Refresh();
    }

    /// <summary>Opens every row that has rows under it, or closes them all.</summary>
    internal void ExpandAll(bool open)
    {
        if (parts is null)
            return;
        foreach (PartNode node in TreeNodes().Where(node => node.Children.Count > 0))
            if (open)
                expanded.Add(node.Key);
            else
                expanded.Remove(node.Key);
        Refresh();
    }

    /// <summary>Shows or hides what is under a row: a partly shown row is shown whole.</summary>
    internal void Flip(PartNode node)
    {
        if (parts is null)
            return;
        parts.Show(node, parts.State(node) != true);
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Shows or hides the selected rows together: hidden if they all show, shown otherwise.</summary>
    internal void FlipSelection()
    {
        IReadOnlyList<PartNode> nodes = Selection;
        if (parts is null || nodes.Count == 0)
            return;
        bool show = nodes.Any(node => parts.State(node) != true);
        SetShown(nodes, show);
    }

    /// <summary>Shows or hides these rows.</summary>
    internal void SetShown(IReadOnlyList<PartNode> nodes, bool shown)
    {
        if (parts is null || nodes.Count == 0)
            return;
        foreach (PartNode node in nodes)
            parts.Show(node, shown);
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Shows these rows only, hiding the rest.</summary>
    internal void ShowOnly(IReadOnlyList<PartNode> nodes)
    {
        if (parts is null || nodes.Count == 0)
            return;
        parts.ShowOnly(nodes);
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Shows every part; a drawable dictionary showing one of each switches to All.</summary>
    internal void ShowAll()
    {
        if (parts is null)
            return;
        if (HasVariants && oneAtATime)
        {
            SetOneAtATime(false);
            return;
        }
        parts.ShowAll();
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Copies the selected rows' names, one to a line.</summary>
    internal Task CopyNames()
    {
        IReadOnlyList<PartNode> nodes = Selection;
        return nodes.Count == 0 ? Task.CompletedTask : Infrastructure.AppServices.Clipboard.SetTextAsync(string.Join(Environment.NewLine, nodes.Select(node => node.Name)));
    }

    /// <summary>Turns the camera to the selected rows' parts.</summary>
    internal void FrameSelection()
    {
        IReadOnlyList<PartNode> nodes = Selection;
        if (nodes.Count > 0)
            FrameRequested?.Invoke(nodes);
    }

    /// <summary>
    /// Selects a row as a click with these keys would: alone; added or taken away with Ctrl; or, with Shift, every row
    /// from the last one clicked.
    /// </summary>
    internal void Select(PartNode node, KeyModifiers modifiers = KeyModifiers.None)
    {
        bool add = modifiers.HasFlag(KeyModifiers.Control);
        if (modifiers.HasFlag(KeyModifiers.Shift) && anchor is not null && IndexOf(anchor) is int from and >= 0 && IndexOf(node.Key) is int to and >= 0)
        {
            if (!add)
                selected.Clear();
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                selected.Add(Rows[i].Node.Key);
        }
        else if (add)
        {
            if (!selected.Remove(node.Key))
                selected.Add(node.Key);
            anchor = node.Key;
        }
        else
        {
            selected.Clear();
            selected.Add(node.Key);
            anchor = node.Key;
        }
        cursor = node.Key;
        // One of each at a time: the drawable picked is the one its section shows.
        if (modifiers == KeyModifiers.None && RadioSection(node) is not null)
        {
            Pick(node);
            return;
        }
        Refresh();
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Node.Key == key)
                return i;
        return -1;
    }

    /// <summary>
    /// A press on a row's eye: it is shown, or hidden if it all showed, and the rows the pointer then passes over while
    /// held are set the same way.
    /// </summary>
    internal void BeginSweep(PartNode node)
    {
        if (parts is null)
            return;
        sweepTo = parts.State(node) != true;
        swept.Clear();
        Set(node);
    }

    /// <summary>The held pointer is over the row at <paramref name="index"/>: it is set as the sweep's first was.</summary>
    internal void SweepOver(int index)
    {
        if (sweepTo is not null && index >= 0 && index < Rows.Count)
            Set(Rows[index].Node);
    }

    /// <summary>The pointer let go: the sweep is over.</summary>
    internal void EndSweep() => sweepTo = null;

    private void Set(PartNode node)
    {
        if (parts is null || sweepTo is not { } to || !swept.Add(node.Key))
            return;
        if (parts.State(node) == to)
            return;
        parts.Show(node, to);
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Whether a row's name, or one under it, has the filter in it.</summary>
    private bool Matches(PartNode node, string filter) =>
        node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Children.Any(child => Matches(child, filter));

    /// <summary>Lists the rows again, after the level, the filter or what is shown or selected changed.</summary>
    public void Refresh()
    {
        // The rows of the level and the state shown.
        sections = parts?.Sections() ?? [];
        rows.Children.Clear();
        rowControls.Clear();
        var listed = new List<(PartNode, bool)>();
        string filter = (Filter.Text ?? "").Trim();
        if (parts is not null)
        {
            void Add(PartNode node, IReadOnlyList<bool> lastAtDepth)
            {
                if (filter.Length > 0 && !Matches(node, filter))
                    return;
                // A filter opens the rows its matches are under.
                bool open = node.Children.Count > 0 && (expanded.Contains(node.Key)
                    || (filter.Length > 0 && !node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
                Control row = Row(node, open, lastAtDepth);
                rowControls.Add((row, listed.Count));
                listed.Add((node, open));
                rows.Children.Add(row);
                if (open)
                {
                    PartNode[] children = [.. node.Children.Where(child => filter.Length == 0 || Matches(child, filter))];
                    for (int i = 0; i < children.Length; i++)
                        Add(children[i], [.. lastAtDepth, i == children.Length - 1]);
                }
            }
            foreach (PartSection section in sections)
            {
                if (section.Kind == PartSectionKind.Extras)
                {
                    PartNode[] extras = [.. section.Nodes.Where(node => filter.Length == 0 || node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];
                    if (extras.Length == 0)
                        continue;
                    rows.Children.Add(Heading(section.Title, $"{extras.Count(node => parts.State(node) == true)} / {extras.Length}"));
                    rows.Children.Add(Chips(extras));
                    continue;
                }
                int before = rows.Children.Count;
                if (section.Title.Length > 0)
                    rows.Children.Add(Heading(section.Title, section.Kind == PartSectionKind.Variants ? section.Nodes.Count.ToString(CultureInfo.InvariantCulture) : ""));
                int listedBefore = listed.Count;
                foreach (PartNode node in section.Nodes)
                    Add(node, []);
                // A section the filter leaves empty goes, heading and all.
                if (listed.Count == listedBefore && rows.Children.Count > before)
                    rows.Children.RemoveRange(before, rows.Children.Count - before);
            }
            (int shown, int total) = parts.Counts();
            counts.Text = $"{shown} / {total}";
            if (listed.Count == 0 && filter.Length > 0)
                rows.Children.Add(new TextBlock
                {
                    Text = "No parts match",
                    Margin = new Thickness(12, 8),
                    FontFamily = Ui.Theme.UiFont,
                    FontSize = Ui.Theme.FontSize,
                    Foreground = Ui.Theme.TextFaint,
                });
        }
        else
        {
            counts.Text = "";
        }
        Rows = listed;
        selected.IntersectWith(listed.Select(row => row.Item1.Key));
        bool allOpen = AllExpanded;
        // A flat list (a collision's materials) has nothing to open or close.
        ExpandButton.IsVisible = TreeNodes().Any(node => node.Children.Count > 0);
        ExpandButton.IconKind = allOpen ? "collapse-all" : "expand-all";
        ThemedToolTip.Set(ExpandButton, allOpen ? "Collapse all" : "Expand all");
        AutomationProperties.SetName(ExpandButton, allOpen ? "Collapse all" : "Expand all");
    }

    // A section's heading, in the sheets' small caps, with a count beside it.
    private static Control Heading(string title, string count) => new DockPanel
    {
        Margin = new Thickness(12, 10, 12, 3),
        Children =
        {
            new TextBlock
            {
                [DockPanel.DockProperty] = Dock.Right,
                Text = count,
                FontFamily = Ui.Theme.MonoFont,
                FontSize = Ui.Theme.CapsFontSize,
                Foreground = Ui.Theme.TextFaint,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontFamily = Ui.Theme.MonoFont,
                FontSize = Ui.Theme.CapsFontSize,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 1.2,
                Foreground = Ui.Theme.TextFaint,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    /// <summary>The extras as toggles, numbered as the game numbers them: lit while shown.</summary>
    private Control Chips(IReadOnlyList<PartNode> extras)
    {
        var chips = new WrapPanel { Margin = new Thickness(12, 2, 12, 6), ItemSpacing = 4, LineSpacing = 4 };
        foreach (PartNode extra in extras)
        {
            bool on = parts?.State(extra) == true;
            int number = PartVisibility.ExtraNumber(extra.Name) ?? 0;
            var chip = new Border
            {
                Name = extra.Name,
                MinWidth = 26,
                Height = 22,
                Padding = new Thickness(6, 0),
                CornerRadius = Ui.Theme.InnerCorner,
                BorderThickness = new Thickness(1),
                Background = on ? new ImmutableSolidColorBrush(Ui.Theme.WithAlpha(Ui.Theme.Accent.Color, .22)) : Ui.Theme.InsetBg,
                BorderBrush = on ? Ui.Theme.Accent : Ui.Theme.BorderHard,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = number.ToString(CultureInfo.InvariantCulture),
                    FontFamily = Ui.Theme.MonoFont,
                    FontSize = Ui.Theme.SmallFontSize,
                    Foreground = on ? Ui.Theme.Text : Ui.Theme.TextFaint,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ThemedToolTip.Set(chip, $"{extra.Name} · {extra.Triangles.ToString("N0", CultureInfo.InvariantCulture)} triangles\n{(on ? "Shown: click to hide" : "Hidden: click to show")}");
            AutomationProperties.SetName(chip, $"Extra {number}");
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
                    return;
                e.Handled = true;
                ToggleExtra(extra);
            };
            chips.Children.Add(chip);
        }
        return chips;
    }

    /// <summary>Shows or hides an extra, as a click on its toggle does.</summary>
    internal void ToggleExtra(PartNode extra) => Flip(extra);

    // A row's badge: its kind's letter in its kind's ink.
    private static (string Letter, IImmutableSolidColorBrush Ink, string Kind) Badge(PartKind kind) => kind switch
    {
        PartKind.Group => ("S", Ui.Theme.InkAsset, "Set of drawables"),
        PartKind.Child => ("C", Ui.Theme.Brass, "Child model, bound to a bone"),
        PartKind.Geometry => ("G", Ui.Theme.InkData, "Geometry, named by its shader"),
        PartKind.State => ("F", Ui.Theme.Brass, "Fragment state, with what is drawn only in it"),
        _ => ("D", Ui.Theme.Accent, "Drawable"),
    };

    private Control Row(PartNode node, bool open, IReadOnlyList<bool> lastAtDepth)
    {
        bool isSelected = selected.Contains(node.Key);
        // Its menu, open, takes the window's focus, but the rows it acts on stay marked as they were.
        bool focused = (IsKeyboardFocusWithin && !Filter.IsKeyboardFocusWithin) || menu is not null;
        bool strong = isSelected && focused;
        bool? state = parts?.State(node);
        IBrush ink = strong ? Ui.Theme.SelectionInk : Ui.Theme.TextFaint;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{6 + node.Depth * Indent},14,20,20,*,Auto"),
            Height = RowHeight,
            Background = Brushes.Transparent,
        };
        // The tree's dotted guides: a column for each level above, running on while its row has siblings still to come,
        // and this row's own, with a tick across to it, ending here when it's the last.
        var guides = new Canvas { Width = 6 + node.Depth * Indent, Height = RowHeight };
        for (int depth = 0; depth < node.Depth && depth < lastAtDepth.Count; depth++)
        {
            bool own = depth == node.Depth - 1;
            bool last = lastAtDepth[depth];
            if (!own && last)
                continue;
            var rule = new DottedRule { Width = 1, Height = own && last ? RowHeight / 2 : RowHeight, DotColor = Ui.Theme.Guide };
            Canvas.SetLeft(rule, 6 + depth * Indent + Indent / 2);
            guides.Children.Add(rule);
            if (own)
            {
                var tick = new DottedRule { Horizontal = true, Width = Indent / 2 - 2, Height = 1, DotColor = Ui.Theme.Guide };
                Canvas.SetLeft(tick, 6 + depth * Indent + Indent / 2 + 2);
                Canvas.SetTop(tick, RowHeight / 2);
                guides.Children.Add(tick);
            }
        }
        grid.Children.Add(guides);

        if (node.Children.Count > 0)
        {
            var caret = new LucideIcon
            {
                IconName = open ? "chevron-down" : "chevron-right",
                Width = 10,
                Height = 10,
                Weight = 1.8,
                Stroke = ink,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var hit = new Border { Background = Brushes.Transparent, Child = caret, Cursor = new Cursor(StandardCursorType.Hand) };
            hit.PointerPressed += (_, e) =>
            {
                Toggle(node);
                e.Handled = true;
            };
            Grid.SetColumn(hit, 1);
            grid.Children.Add(hit);
        }

        // The eye: shown, hidden, or (dimmed) partly shown.
        var eyeIcon = new LucideIcon
        {
            IconName = state == false ? "eye-off" : "eye",
            Width = 13,
            Height = 13,
            Weight = 1.6,
            Stroke = strong ? Ui.Theme.SelectionInk : state == false ? Ui.Theme.TextFaint : Ui.Theme.TextDim,
            Opacity = state is null ? .5 : 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var eye = new Border { Background = Brushes.Transparent, Child = eyeIcon, Cursor = new Cursor(StandardCursorType.Hand) };
        ThemedToolTip.Set(eye, state == true ? "Hide  (Alt+click: show only this)" : "Show  (Alt+click: show only this)");
        // Handled before the row: the eye sets what it's on, and any rows the press then sweeps over.
        eye.AddHandler(PointerPressedEvent, (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(eye).Properties;
            if (!properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                ShowOnly([node]);
                return;
            }
            if (RadioSection(node) is not null)
            {
                Pick(node);
                return;
            }
            BeginSweep(node);
            e.Pointer.Capture(rows);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Grid.SetColumn(eye, 2);
        grid.Children.Add(eye);

        (string letter, IImmutableSolidColorBrush badgeInk, string kind) = Badge(node.Kind);
        // A mark wears the colour it is drawn in: the row is the legend's entry.
        Control badge = node.Colour is { } colour ? new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = new ImmutableSolidColorBrush(ViewportMesh.Unpack(colour)),
            Opacity = state == false ? .4 : 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        }
        : new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(3),
            Background = new ImmutableSolidColorBrush(Ui.Theme.WithAlpha(badgeInk.Color, strong ? .32 : .18)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = letter,
                FontFamily = Ui.Theme.MonoFont,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = strong ? Ui.Theme.SelectionText : badgeInk,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 3);
        grid.Children.Add(badge);

        var name = new TextBlock
        {
            Text = node.Name,
            Margin = new Thickness(4, 0, 0, 0),
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.FontSize,
            Foreground = strong ? (state == false ? Ui.Theme.SelectionFaint : Ui.Theme.SelectionText) : state == false ? Ui.Theme.TextFaint : Ui.Theme.TextRow,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 4);
        grid.Children.Add(name);
        var triangles = new TextBlock
        {
            Text = Compact(node.Triangles),
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            Foreground = strong ? Ui.Theme.SelectionFaint : Ui.Theme.TextFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        Grid.SetColumn(triangles, 5);
        grid.Children.Add(triangles);
        ThemedToolTip.Set(grid, node.Kind == PartKind.Mark
            ? $"{node.Name}: {node.Triangles.ToString("N0", CultureInfo.InvariantCulture)}"
            : $"{node.Name}\n{kind} · {node.Triangles.ToString("N0", CultureInfo.InvariantCulture)} triangles");

        IBrush rest = isSelected ? (focused ? Ui.Theme.Selection : Ui.Theme.SelectionWash) : Brushes.Transparent;
        var row = new Border { CornerRadius = Ui.Theme.ItemCorner, Child = grid, Background = rest, Margin = new Thickness(4, 0, 4, 0) };
        row.PointerEntered += (_, _) =>
        {
            if (!isSelected)
                row.Background = Ui.Theme.HoverBg;
        };
        row.PointerExited += (_, _) => row.Background = rest;
        row.PointerPressed += (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(row).Properties;
            if (properties.IsLeftButtonPressed)
            {
                Focus();
                if (e.ClickCount >= 2)
                {
                    Select(node);
                    FrameSelection();
                }
                else
                    Select(node, e.KeyModifiers);
                e.Handled = true;
            }
            else if (properties.IsRightButtonPressed)
            {
                Focus();
                if (!selected.Contains(node.Key))
                    Select(node);
                OpenMenu(row, e.GetPosition(row));
                e.Handled = true;
            }
        };
        return row;
    }

    /// <summary>The right click's menu, at the pointer, over the viewport too, for the selected rows.</summary>
    internal void OpenMenu(Control anchor, Point at)
    {
        menu?.Close();
        IReadOnlyList<PartNode> nodes = Selection;
        if (parts is null || nodes.Count == 0)
            return;
        bool allShown = nodes.All(node => parts.State(node) == true);
        var items = new StackPanel { Margin = Ui.Theme.MenuInset, MinWidth = 200 };
        void Item(string icon, string text, string shortcut, Action action)
        {
            var row = new MenuRowItem(icon, text, shortcut);
            row.Triggered += () =>
            {
                menu?.Close();
                action();
            };
            items.Children.Add(row);
        }
        Item("eye", "Show only", "Alt+click", () => ShowOnly(nodes));
        if (allShown)
            Item("eye-off", "Hide", "Del", () => SetShown(nodes, false));
        else
            Item("eye", "Show", "Space", () => SetShown(nodes, true));
        Item("eye", "Show all", "", ShowAll);
        items.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 4), Background = Ui.Theme.BorderSoft });
        Item("focus", "Frame", "Enter", FrameSelection);
        Item("copy", nodes.Count == 1 ? "Copy name" : "Copy names", "Ctrl+C", () => _ = CopyNames());
        menu = DropdownWindow.OpenAt(anchor, at, items);
        if (menu is not null)
            menu.Closed += (_, _) =>
            {
                menu = null;
                Refresh();
            };
        Refresh();
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Filter.Text))
        {
            Filter.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Rows.Count > 0)
        {
            Focus();
            Select(Rows[0].Node);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // The filter's keys are its own.
        if (e.Handled || e.Source is TextBox || parts is null)
            return;
        e.Handled = HandleKey(e.Key, e.KeyModifiers);
    }

    /// <summary>A key pressed in the panel: true when it is the panel's.</summary>
    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        int at = cursor is null ? -1 : IndexOf(cursor);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        switch (key)
        {
            case Avalonia.Input.Key.Down or Avalonia.Input.Key.Up when Rows.Count > 0:
            {
                int next = at < 0 ? 0 : Math.Clamp(at + (key == Avalonia.Input.Key.Down ? 1 : -1), 0, Rows.Count - 1);
                Select(Rows[next].Node, shift ? KeyModifiers.Shift : KeyModifiers.None);
                return true;
            }
            case Avalonia.Input.Key.Right when at >= 0:
            {
                (PartNode node, bool open) = Rows[at];
                if (node.Children.Count > 0 && !open)
                    Toggle(node);
                else if (open && at + 1 < Rows.Count)
                    Select(Rows[at + 1].Node);
                return true;
            }
            case Avalonia.Input.Key.Left when at >= 0:
            {
                (PartNode node, bool open) = Rows[at];
                if (open)
                    Toggle(node);
                else
                    for (int i = at - 1; i >= 0; i--)
                        if (Rows[i].Node.Depth < node.Depth)
                        {
                            Select(Rows[i].Node);
                            break;
                        }
                return true;
            }
            case Avalonia.Input.Key.Space:
                FlipSelection();
                return true;
            case Avalonia.Input.Key.Delete:
                SetShown(Selection, false);
                return true;
            case Avalonia.Input.Key.Enter:
                FrameSelection();
                return true;
            case Avalonia.Input.Key.A when modifiers.HasFlag(KeyModifiers.Control):
                selected.Clear();
                foreach ((PartNode node, _) in Rows)
                    selected.Add(node.Key);
                Refresh();
                return true;
            case Avalonia.Input.Key.C when modifiers.HasFlag(KeyModifiers.Control):
                _ = CopyNames();
                return true;
            case Avalonia.Input.Key.Escape when selected.Count > 0:
                selected.Clear();
                Refresh();
                return true;
        }
        return false;
    }

    /// <summary>A triangle count as the row shows it: 840, 9.4k, 38k, 1.2m.</summary>
    internal static string Compact(int value) => value switch
    {
        < 1000 => value.ToString(CultureInfo.InvariantCulture),
        < 10_000 => (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        < 1_000_000 => (value / 1000).ToString(CultureInfo.InvariantCulture) + "k",
        _ => (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "m",
    };

    /// <summary>A row of the panel's menu, laid out as the app's menu items: an icon cell, the label, its key in faint mono.</summary>
    internal sealed class MenuRowItem : Border
    {
        private readonly LucideIcon icon;
        private readonly TextBlock label;
        private readonly TextBlock key;

        public MenuRowItem(string iconName, string text, string shortcut)
        {
            Text = text;
            Height = Ui.Theme.MenuItemHeight;
            CornerRadius = Ui.Theme.InnerCorner;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            icon = new LucideIcon { IconName = iconName, Width = 14, Height = 14, Weight = 1.6, Margin = new Thickness(9, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            key = new TextBlock { Text = shortcut, FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            label = new TextBlock { Text = text, FontFamily = Ui.Theme.UiFont, FontSize = Ui.Theme.FontSize, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(key, Dock.Right);
            Child = new Panel { Children = { icon, new DockPanel { Margin = new Thickness(32, 0, 10, 0), Children = { key, label } } } };
            Paint(false);
            PointerEntered += (_, _) => Paint(true);
            PointerExited += (_, _) => Paint(false);
            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                    Triggered?.Invoke();
            };
        }

        /// <summary>The item's label.</summary>
        public string Text { get; }

        public event Action? Triggered;

        /// <summary>Runs the item, as a click on it does.</summary>
        internal void Trigger() => Triggered?.Invoke();

        private void Paint(bool highlighted)
        {
            Background = highlighted ? Ui.Theme.HoverChrome : Brushes.Transparent;
            icon.Stroke = highlighted ? Ui.Theme.Text : Ui.Theme.TextDim;
            label.Foreground = highlighted ? Ui.Theme.Text : Ui.Theme.TextRow;
            key.Foreground = Ui.Theme.TextDim;
        }
    }
}
