using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A text file, read only, shown over the main window like the texture editor: TXT, XML and META, JSON,
/// CSV, INI and the rest <see cref="TextDocument"/> knows. The text is coloured, selectable and searchable;
/// long lines can wrap, the type can be zoomed, and a CSV file reads as a table (or as text). Escape
/// clears the find first, then closes.
/// </summary>
public partial class TextViewer : UserControl, IModalContent
{
    /// <summary>Matches found past this many are not marked; the count says so.</summary>
    public const int MaxMatches = 10000;

    private readonly TextDocument document;
    private readonly CsvTable? table;
    private ModalOverlay? overlay;
    private List<TextMatch> textMatches = [];
    private List<(int Row, int Column)> cellMatches = [];
    private int currentMatch = -1;
    private int zoomStep = TextViewerInk.Zooms.ToList().IndexOf(1);

    /// <summary>An empty viewer, for the XAML loader and previews.</summary>
    public TextViewer() : this(TextDocument.Load("", []))
    {
    }

    public TextViewer(TextDocument document)
    {
        this.document = document;
        InitializeComponent();
        TitleText.Text = document.Name;
        TitleGlyph.FileKind = RpfProvider.FileKind(document.Name);
        Lines.Scroller = TextScroller;
        Lines.Document = document;
        // The wheel glides, as it does in the explorer's lists.
        SmoothScrolling.Attach(TextScroller);
        SmoothScrolling.Attach(TableScroller);
        Lines.ContextMenu = BuildMenu();
        Lines.CaretMoved += (_, _) => RefreshCaret();
        if (document.Format == TextFormat.Csv)
        {
            table = CsvTable.Parse(document.Text);
            Table.Scroller = TableScroller;
            Table.Table = table;
            Table.CurrentRowChanged += (_, _) => RefreshCaret();
            ViewSwitch.IsVisible = true;
            ViewSwitch.SideRequested += (_, side) => ShowTable(side == 1);
        }

        CloseMark.Click += (_, _) => Close();
        Menus = new ViewerMenuRow(
            ("File", [new("Close", "Ctrl+W", Close)]),
            ("Edit",
            [
                new("Copy", "Ctrl+C", () => Copy()),
                null,
                new("Find…", "Ctrl+F", FocusFind),
                new("Find next", "F3", () => StepMatch(1), () => (FindField.Text ?? "").Length > 0),
                new("Find previous", "Shift+F3", () => StepMatch(-1), () => (FindField.Text ?? "").Length > 0),
            ]));
        MenuHolder.Children.Add(Menus);
        WrapButton.Click += (_, _) => SetWrap(!Lines.Wrap);
        ZoomOutButton.Click += (_, _) => SetZoom(zoomStep - 1);
        ZoomInButton.Click += (_, _) => SetZoom(zoomStep + 1);
        ZoomButton.Click += (_, _) => SetZoom(TextViewerInk.Zooms.ToList().IndexOf(1));
        CopyButton.Click += (_, _) => Copy();
        PreviousButton.Click += (_, _) => StepMatch(-1);
        NextButton.Click += (_, _) => StepMatch(1);
        FindField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Find(FindField.Text ?? "");
            else if (e.Property == IsKeyboardFocusWithinProperty)
                RefreshFindField();
        };
        FindField.KeyDown += OnFindKeyDown;
        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        ThemedToolTip.Set(WrapButton, "Wrap long lines  (Alt+Z)");
        ThemedToolTip.Set(ZoomOutButton, "Smaller text  (Ctrl+−)");
        ThemedToolTip.Set(ZoomInButton, "Larger text  (Ctrl++)");
        ThemedToolTip.Set(ZoomButton, "Actual size  (Ctrl+0)");
        ThemedToolTip.Set(CopyButton, "Copy the selection, or the whole file  (Ctrl+C)");
        ThemedToolTip.Set(PreviousButton, "Previous match  (Shift+F3)");
        ThemedToolTip.Set(NextButton, "Next match  (F3)");

        SizeText.Text = Formatting.FormatSize(document.Size);
        EncodingText.Text = document.Encoding;
        EndingText.Text = document.LineEnding;
        EndingText.IsVisible = document.LineEnding.Length > 0;
        FormatText.Text = document.FormatName;
        ShowTable(table is not null);
        SetWrap(false);
        SetZoom(zoomStep);
        RefreshFindField();
        RefreshCaret();
    }

    public TextDocument Document => document;

    /// <summary>The control the viewer opens over, in the main window.</summary>
    public Control? Anchor { get; set; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>The viewer closed.</summary>
    public event EventHandler? Closed;

    /// <summary>Whether the CSV table is showing, rather than the text.</summary>
    public bool ShowingTable => TableScroller.IsVisible;

    /// <summary>The find's matches in what is showing, and which is current (-1 for none).</summary>
    public int MatchCount => ShowingTable ? cellMatches.Count : textMatches.Count;

    public int CurrentMatch => currentMatch;

    public void Present()
    {
        if (IsOpen)
            return;
        Control? anchor = Anchor
            ?? ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.ExplorerScreenHost;
        if (anchor is null)
            return;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is not null)
            overlay.Closed += (_, _) =>
            {
                overlay = null;
                Closed?.Invoke(this, EventArgs.Empty);
            };
        Dispatcher.UIThread.Post(FocusBody, DispatcherPriority.Loaded);
    }

    public void Close() => overlay?.Close();

    /// <summary>Escape: clears the find (and goes back to the text) first, then closes.</summary>
    void IModalContent.RequestClose()
    {
        if (!string.IsNullOrEmpty(FindField.Text) || FindField.IsKeyboardFocusWithin)
        {
            FindField.Text = "";
            FocusBody();
            return;
        }
        Close();
    }

    /// <summary>Shows the CSV as a table, or as text.</summary>
    public void ShowTable(bool showTable)
    {
        bool tableShown = showTable && table is not null;
        TableScroller.IsVisible = tableShown;
        TextScroller.IsVisible = !tableShown;
        ViewSwitch.SelectedIndex = tableShown ? 1 : 0;
        // Wrapping is for text; a table's cells are cut to their columns instead.
        WrapWell.IsEnabled = !tableShown;
        LinesText.Text = tableShown ? $"{Table.RecordCount:N0} rows" : $"{document.Lines.Count:N0} lines";
        Find(FindField.Text ?? "");
        RefreshCaret();
    }

    public void SetWrap(bool wrap)
    {
        Lines.Wrap = wrap;
        TextScroller.HorizontalScrollBarVisibility = wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        WrapButton.Sunken = wrap;
        WrapButton.Foreground = wrap ? Ui.Theme.Text : Ui.Theme.TextDim;
    }

    public void SetZoom(int step)
    {
        zoomStep = Math.Clamp(step, 0, TextViewerInk.Zooms.Count - 1);
        Lines.Zoom = TextViewerInk.Zooms[zoomStep];
        ZoomButton.Text = $"{Math.Round(Lines.Zoom * 100)}%";
        ZoomOutButton.IsEnabled = zoomStep > 0;
        ZoomInButton.IsEnabled = zoomStep < TextViewerInk.Zooms.Count - 1;
    }

    /// <summary>Copies the selection, or the current row of the table, or else the whole file.</summary>
    public string Copy()
    {
        string text, said;
        if (ShowingTable && Table.CurrentRow > 0)
        {
            text = Table.CurrentRowText(table!.Separator);
            said = $"Copied row {Table.CurrentRow}";
        }
        else if (!ShowingTable && Lines.HasSelection)
        {
            text = Lines.SelectedText;
            int lines = Lines.Selection.End.Line - Lines.Selection.Start.Line + 1;
            said = lines == 1 ? $"Copied {text.Length:N0} characters" : $"Copied {lines:N0} lines";
        }
        else
        {
            text = document.Text;
            said = $"Copied {document.Name}";
        }
        _ = AppServices.Clipboard.SetTextAsync(text);
        StatusText.Text = said;
        return text;
    }

    /// <summary>Looks for <paramref name="query"/> (case aside) in what is showing.</summary>
    public void Find(string query)
    {
        textMatches = [];
        cellMatches = [];
        currentMatch = -1;
        if (query.Length > 0 && ShowingTable && table is not null)
        {
            // The titles too: they are showing, at the top.
            for (int row = 0; row < table.Rows.Count && cellMatches.Count < MaxMatches; row++)
                for (int column = 0; column < table.Rows[row].Length; column++)
                    if (CsvTableView.Shown(table.Rows[row][column]).Contains(query, StringComparison.OrdinalIgnoreCase))
                        cellMatches.Add((row, column));
            currentMatch = cellMatches.Count > 0 ? Math.Max(0, cellMatches.FindIndex(match => match.Row >= Table.CurrentRow)) : -1;
        }
        else if (query.Length > 0)
        {
            for (int line = 0; line < document.Lines.Count && textMatches.Count < MaxMatches; line++)
            {
                string text = document.Lines[line];
                for (int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0 && textMatches.Count < MaxMatches;
                     at = text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase))
                    textMatches.Add(new TextMatch(line, at, query.Length));
            }
            // The first match from the caret on, so a find starts where you are.
            TextPosition from = Lines.Selection.Start;
            int index = textMatches.FindIndex(match => new TextPosition(match.Line, match.Column) >= from);
            currentMatch = textMatches.Count == 0 ? -1 : index < 0 ? 0 : index;
        }
        ApplyMatches(query);
    }

    /// <summary>Moves to the next (or, with -1, the previous) match, wrapping round.</summary>
    public void StepMatch(int direction)
    {
        int count = MatchCount;
        if (count == 0)
            return;
        currentMatch = ((currentMatch < 0 ? 0 : currentMatch + direction) % count + count) % count;
        ApplyMatches(FindField.Text ?? "");
    }

    private void ApplyMatches(string query)
    {
        if (ShowingTable)
            Table.SetMatches(query, cellMatches, currentMatch);
        else
            Lines.SetMatches(textMatches, currentMatch);
        int count = MatchCount;
        bool any = query.Length > 0;
        FindWell.IsVisible = any;
        MatchText.IsVisible = any;
        MatchText.Text = count == 0 ? "No matches" : $"{currentMatch + 1:N0} of {count:N0}{(count >= MaxMatches ? "+" : "")}";
        MatchText.Foreground = count == 0 ? Ui.Theme.Warning : Ui.Theme.TextDim;
        PreviousButton.IsEnabled = NextButton.IsEnabled = count > 0;
        StatusText.Text = !any ? "Read only" : count == 0 ? $"No matches for \"{query}\"" : $"{count:N0}{(count >= MaxMatches ? "+" : "")} matches for \"{query}\"";
        RefreshFindField();
    }

    private void RefreshFindField()
    {
        bool hasText = !string.IsNullOrEmpty(FindField.Text);
        FindField.KeyCap = FindField.IsKeyboardFocusWithin || hasText ? "Esc" : "Ctrl+F";
        FindField.LeadingColor = hasText ? Ui.Theme.Brass : Ui.Theme.TextFaint;
    }

    private void RefreshCaret()
    {
        if (ShowingTable)
        {
            CaretText.Text = Table.RecordCount == 0 ? "No rows" : $"Row {Table.CurrentRow:N0} of {Table.RecordCount:N0}";
            return;
        }
        TextPosition caret = Lines.Caret;
        string selection = Lines.HasSelection ? $"  ({Lines.SelectionLength:N0} selected)" : "";
        CaretText.Text = $"Ln {caret.Line + 1:N0}, Col {caret.Column + 1:N0}{selection}";
    }

    /// <summary>Right-click: copy what is selected (or the whole file), or select everything.</summary>
    private RetroMenu BuildMenu()
    {
        var menu = new RetroMenu { Width = 200 + 2 * Ui.Theme.MenuPadding };
        var copy = new RetroMenuItem { Text = "Copy", ShortcutText = "Ctrl+C", MinWidth = 0 };
        copy.Triggered += (_, _) => Copy();
        var selectAll = new RetroMenuItem { Text = "Select all", ShortcutText = "Ctrl+A", MinWidth = 0 };
        selectAll.Triggered += (_, _) => Lines.SelectAll();
        menu.Items.Add(copy);
        menu.Items.Add(selectAll);
        menu.Opening += (_, _) => copy.Text = Lines.HasSelection ? "Copy" : "Copy the whole file";
        return menu;
    }

    /// <summary>The menu bar under the title: File and Edit.</summary>
    internal ViewerMenuRow Menus { get; }

    // Find: the field takes the keyboard, what it holds selected.
    private void FocusFind()
    {
        FindField.Focus();
        FindField.SelectAll();
    }

    private void FocusBody()
    {
        if (ShowingTable)
            Table.Focus();
        else
            Lines.Focus();
    }

    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StepMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        KeyModifiers modifiers = e.KeyModifiers;
        bool ctrl = modifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.F when ctrl:
                FocusFind();
                break;
            case Key.W when ctrl:
                Close();
                break;
            case Key.F3:
                StepMatch(modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                break;
            case Key.C when ctrl && !FindField.IsKeyboardFocusWithin:
                Copy();
                break;
            case Key.OemPlus or Key.Add when ctrl:
                SetZoom(zoomStep + 1);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                SetZoom(zoomStep - 1);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                SetZoom(TextViewerInk.Zooms.ToList().IndexOf(1));
                break;
            case Key.Z when modifiers == KeyModifiers.Alt && !ShowingTable:
                SetWrap(!Lines.Wrap);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !ShowingTable)
        {
            SetZoom(zoomStep + (e.Delta.Y > 0 ? 1 : -1));
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.Y != 0)
        {
            // Shift+wheel goes sideways, through long lines or wide tables.
            ScrollViewer scroller = ShowingTable ? TableScroller : TextScroller;
            double maximum = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            scroller.Offset = scroller.Offset.WithX(Math.Clamp(scroller.Offset.X - e.Delta.Y * SmoothScrolling.NotchDistance, 0, maximum));
            e.Handled = true;
        }
    }
}
