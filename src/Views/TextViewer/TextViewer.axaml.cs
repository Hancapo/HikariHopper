using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A text file, shown over the main window like the texture editor and edited as typed: TXT, XML and META, JSON,
/// CSV, INI and the rest <see cref="TextDocument"/> knows. The text is coloured, selectable and searchable;
/// long lines can wrap, the type can be zoomed, and a CSV file reads as a table (or as text, which is where it is
/// edited). Save puts it back where it was read from, in its own encoding and line endings; closing with changes
/// unsaved asks first. Escape clears the find first, then closes.
/// </summary>
public partial class TextViewer : UserControl, IModalContent
{
    /// <summary>Matches found past this many are not marked; the count says so.</summary>
    public const int MaxMatches = 10000;

    /// <summary>How long typing pauses before the find looks again, so a big file isn't searched on every key.</summary>
    internal static TimeSpan FindAgainDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    private readonly TextDocument document;
    private readonly bool isCsv;
    private CsvTable? table;
    // The table was read from the text before its last edit, and is read again when it next shows.
    private bool tableStale;
    private ModalOverlay? overlay;
    private List<TextMatch> textMatches = [];
    private List<(int Row, int Column)> cellMatches = [];
    private int currentMatch = -1;
    private int zoomStep = TextViewerInk.Zooms.ToList().IndexOf(1);
    private readonly DispatcherTimer findAgain;

    /// <summary>An empty viewer, for the XAML loader and previews.</summary>
    public TextViewer() : this(TextDocument.Load("", []))
    {
    }

    /// <summary>A text with nowhere to save it but a file picked for it (Save as).</summary>
    public TextViewer(TextDocument document) : this(new TextFile(document))
    {
    }

    public TextViewer(TextFile file)
    {
        document = file.Document;
        saveTarget = file.SaveTarget;
        documentName = document.Name;
        InitializeComponent();
        TitleGlyph.FileKind = RpfProvider.FileKind(document.Name);
        Lines.Scroller = TextScroller;
        Lines.Document = document;
        // The wheel glides, as it does in the explorer's lists.
        SmoothScrolling.Attach(TextScroller);
        SmoothScrolling.Attach(TableScroller);
        Lines.ContextMenu = BuildMenu();
        Lines.CaretMoved += (_, _) => RefreshCaret();
        Lines.TextChanged += (_, _) => OnTextChanged();
        findAgain = new DispatcherTimer(FindAgainDelay, DispatcherPriority.Background, (_, _) => FindAgain());
        isCsv = document.Format == TextFormat.Csv;
        if (isCsv)
        {
            table = CsvTable.Parse(document.Text);
            Table.Scroller = TableScroller;
            Table.Table = table;
            Table.CurrentRowChanged += (_, _) => RefreshCaret();
            ViewSwitch.IsVisible = true;
            ViewSwitch.SideRequested += (_, side) => ShowTable(side == 1);
        }

        CloseMark.Click += (_, _) => Close();
        Menus = BuildMenus();
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

        EncodingText.Text = document.Encoding;
        FormatText.Text = document.FormatName;
        RefreshTitle();
        RefreshFacts();
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

    /// <summary>Whether the text has changes not yet saved.</summary>
    public bool IsModified => Lines.IsModified;

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

    // Whether the viewer has closed, or may close with its changes unsaved (they were saved, or the dialog was told not to).
    private bool closed;
    private bool closeApproved;
    // What to do once the viewer has closed (open another file in it), unless the closing is cancelled.
    private Action? afterClose;
    private TextureUnsavedChangesDialog? unsavedDialog;

    /// <summary>The dialog asking whether to save before the viewer closes, while it is open.</summary>
    internal TextureUnsavedChangesDialog? UnsavedDialog => unsavedDialog is { IsOpen: true } ? unsavedDialog : null;

    /// <summary>Closes the viewer; with changes unsaved it asks first, and may stay.</summary>
    public void Close()
    {
        if (!closed && !closeApproved && Lines.IsModified)
        {
            AskToSave();
            return;
        }
        if (!closed)
        {
            closed = true;
            findAgain.Stop();
            overlay?.Close();
        }
        Action? next = afterClose;
        afterClose = null;
        next?.Invoke();
    }

    /// <summary>
    /// Closes the viewer as <see cref="Close"/> does, then runs <paramref name="next"/>: at once, or once the changes are
    /// saved or let go. Cancelled, the viewer stays and <paramref name="next"/> never runs.
    /// </summary>
    public void CloseThen(Action next)
    {
        if (UnsavedDialog is not null)
            return;
        afterClose = next;
        Close();
    }

    /// <summary>Escape: clears the find (and goes back to the text) first, then closes.</summary>
    void IModalContent.RequestClose()
    {
        if (UnsavedDialog is not null)
            return;
        if (!string.IsNullOrEmpty(FindField.Text) || FindField.IsKeyboardFocusWithin)
        {
            FindField.Text = "";
            FocusBody();
            return;
        }
        Close();
    }

    /// <summary>
    /// Asks whether to save the changes before the viewer closes: Save closes it once they are saved (into a file picked
    /// for them, for a text with nowhere of its own), Don't save closes it, Cancel stays.
    /// </summary>
    private void AskToSave()
    {
        if (UnsavedDialog is not null)
            return;
        var dialog = new TextureUnsavedChangesDialog { DocumentName = documentName };
        dialog.SaveRequested += async (_, _) =>
        {
            bool saved = saveTarget is not null ? Save() && await Saving : await SaveAs();
            if (saved)
            {
                closeApproved = true;
                Close();
            }
            else
                StayOpen();
        };
        dialog.DiscardRequested += (_, _) =>
        {
            closeApproved = true;
            Close();
        };
        dialog.Rejected += (_, _) => StayOpen();
        unsavedDialog = dialog;
        dialog.Open(DialogLayer);
    }

    // The closing was called off: nothing waits on it, and the text has the keyboard again.
    private void StayOpen()
    {
        afterClose = null;
        FocusBody();
    }

    /// <summary>Shows the CSV as a table, or as text.</summary>
    public void ShowTable(bool showTable)
    {
        bool tableShown = showTable && table is not null;
        if (tableShown && tableStale)
        {
            // The table shows the text as it is now; it is only read, never edited.
            table = CsvTable.Parse(document.Text);
            Table.Table = table;
            tableStale = false;
        }
        TableScroller.IsVisible = tableShown;
        TextScroller.IsVisible = !tableShown;
        ViewSwitch.SelectedIndex = tableShown ? 1 : 0;
        // Wrapping is for text; a table's cells are cut to their columns instead.
        WrapWell.IsEnabled = !tableShown;
        RefreshFacts();
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
            said = $"Copied {documentName}";
        }
        _ = AppServices.Clipboard.SetTextAsync(text);
        ShowStatus(said);
        return text;
    }

    /// <summary>Takes the selection out of the text onto the clipboard; "" with nothing selected.</summary>
    public string Cut()
    {
        if (ShowingTable || !Lines.HasSelection)
            return "";
        int lines = Lines.Selection.End.Line - Lines.Selection.Start.Line + 1;
        string text = Lines.Cut();
        _ = AppServices.Clipboard.SetTextAsync(text);
        ShowStatus(lines == 1 ? $"Cut {text.Length:N0} characters" : $"Cut {lines:N0} lines");
        return text;
    }

    /// <summary>Puts the clipboard's text in place of the selection; false when it holds none.</summary>
    public async Task<bool> Paste()
    {
        if (ShowingTable)
            return false;
        string? text = await AppServices.Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text) || closed || ShowingTable)
            return false;
        Lines.Paste(text);
        return true;
    }

    /// <summary>Looks for <paramref name="query"/> (case aside) in what is showing.</summary>
    public void Find(string query) => Find(query, select: true);

    /// <summary>
    /// Looks for <paramref name="query"/> in what is showing; the current match is selected only when
    /// <paramref name="select"/>, so a find run again after an edit leaves the caret where the typing is.
    /// </summary>
    private void Find(string query, bool select)
    {
        findAgain.Stop();
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
        ApplyMatches(query, select);
    }

    // The text changed under the find: it looks again, without moving the caret.
    private void FindAgain()
    {
        findAgain.Stop();
        if (!closed && !ShowingTable && (FindField.Text ?? "").Length > 0)
            Find(FindField.Text ?? "", select: false);
    }

    /// <summary>Moves to the next (or, with -1, the previous) match, wrapping round.</summary>
    public void StepMatch(int direction)
    {
        if (findAgain.IsEnabled)
            FindAgain();
        int count = MatchCount;
        if (count == 0)
            return;
        currentMatch = ((currentMatch < 0 ? 0 : currentMatch + direction) % count + count) % count;
        ApplyMatches(FindField.Text ?? "", select: true);
    }

    private void ApplyMatches(string query, bool select)
    {
        if (ShowingTable)
            Table.SetMatches(query, cellMatches, currentMatch);
        else
            Lines.SetMatches(textMatches, currentMatch, select);
        int count = MatchCount;
        bool any = query.Length > 0;
        FindWell.IsVisible = any;
        MatchText.IsVisible = any;
        MatchText.Text = count == 0 ? "No matches" : $"{currentMatch + 1:N0} of {count:N0}{(count >= MaxMatches ? "+" : "")}";
        MatchText.Foreground = count == 0 ? Ui.Theme.Warning : Ui.Theme.TextDim;
        PreviousButton.IsEnabled = NextButton.IsEnabled = count > 0;
        // Looking again after an edit leaves what the status says (a save, say) alone.
        if (select || any)
            ShowStatus(!any ? IdleStatus : count == 0 ? $"No matches for \"{query}\"" : $"{count:N0}{(count >= MaxMatches ? "+" : "")} matches for \"{query}\"");
        RefreshFindField();
    }

    /// <summary>What the status says with nothing else to say: for a text with nowhere of its own, how to keep it.</summary>
    private string IdleStatus => saveTarget is null ? "Save as… to keep changes" : "";

    private void ShowStatus(string text)
    {
        StatusText.Foreground = Ui.Theme.TextFaint;
        StatusText.Text = text;
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

    /// <summary>The status bar's facts that change with the text: lines (or rows), size and line endings.</summary>
    private void RefreshFacts()
    {
        LinesText.Text = ShowingTable ? $"{Table.RecordCount:N0} rows" : $"{document.Lines.Count:N0} lines";
        SizeText.Text = Formatting.FormatSize(document.Size);
        EndingText.Text = document.LineEnding;
        EndingText.IsVisible = document.LineEnding.Length > 0;
    }

    /// <summary>The file's name in the title, marked with an asterisk while it has unsaved changes.</summary>
    private void RefreshTitle() => TitleText.Text = Lines.IsModified ? $"{documentName} *" : documentName;

    // An edit, an undo or a redo: the title, the facts and the caret catch up; the table and the find follow later.
    private void OnTextChanged()
    {
        RefreshTitle();
        RefreshFacts();
        RefreshCaret();
        tableStale = isCsv;
        if ((FindField.Text ?? "").Length > 0)
        {
            findAgain.Stop();
            findAgain.Interval = FindAgainDelay;
            findAgain.Start();
        }
    }

    // ---------------- Saving ----------------

    // The file's name as shown, and where Save puts it: as opened, until it is saved as another file.
    private string documentName;
    private SaveTarget? saveTarget;
    private bool saving;

    /// <summary>The last save, done once it is written (true) or failed (false).</summary>
    internal Task<bool> Saving { get; private set; } = Task.FromResult(false);

    /// <summary>Whether the text can be saved where it lives: it was opened from there, or saved as a file since.</summary>
    internal bool CanSave => saveTarget is not null;

    /// <summary>The file pickers Save as asks; the app's, unless a test gives its own.</summary>
    internal IFileDialogs Dialogs { get; set; } = AppServices.Dialogs;

    /// <summary>
    /// Saves the text (Ctrl+S) where it lives, into its RPF or over the loose file, off the UI thread, in the encoding
    /// and line endings it was read in. False when nothing is changed, a save is under way, it has nowhere to go, or it
    /// can't be written in its encoding.
    /// </summary>
    internal bool Save()
    {
        if (saving || !Lines.IsModified || saveTarget is not { } target)
            return false;
        return SaveTo(target, documentName, null);
    }

    /// <summary>
    /// Saves the text as a loose file picked for it (Ctrl+Shift+S), changed or not; from then on it is that file, which
    /// Save saves to. False when nothing was picked or it couldn't be written.
    /// </summary>
    internal async Task<bool> SaveAs()
    {
        if (saving)
            return false;
        string extension = Path.GetExtension(documentName);
        IReadOnlyList<FileFilter> filters = extension.Length > 0
            ? [new FileFilter($"{extension.TrimStart('.').ToUpperInvariant()} file", [$"*{extension}"]), new FileFilter("All files", ["*.*"])]
            : [new FileFilter("All files", ["*.*"])];
        string? path = await Dialogs.PickSaveFileAsync($"Save {documentName} as", documentName, filters);
        if (string.IsNullOrEmpty(path) || closed)
            return false;
        var target = new SaveTarget(bytes => CommonFury.IO.AtomicFile.Write(path, bytes));
        string name = Path.GetFileName(path);
        if (!SaveTo(target, name, () =>
            {
                saveTarget = target;
                documentName = name;
                TitleGlyph.FileKind = RpfProvider.FileKind(name);
            }))
            return false;
        return await Saving;
    }

    // Writes the text as it is now through a target, off the UI thread; once it is written, runs afterwards.
    private bool SaveTo(SaveTarget target, string name, Action? afterwards)
    {
        // What is written is the text as it is now; what is typed while it is put in place stays a change.
        int state = Lines.EditState;
        byte[] bytes;
        try
        {
            bytes = document.Encode();
            target.Prepare?.Invoke();
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            ShowSaveFailure(name, error.Message);
            return false;
        }
        Lines.SealEdits();
        saving = true;
        ShowStatus($"Saving {name}…");
        Saving = Finish();
        return true;

        async Task<bool> Finish()
        {
            string? failure = null;
            try
            {
                await Task.Run(() => target.Write(bytes));
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                failure = error.Message;
            }
            saving = false;
            if (failure is null)
            {
                afterwards?.Invoke();
                Lines.MarkSaved(state);
                document.MarkSaved(bytes.LongLength);
                RefreshTitle();
                RefreshFacts();
            }
            target.Finished?.Invoke(failure is null);
            if (failure is not null)
                ShowSaveFailure(name, failure);
            else if (!closed)
                ShowStatus($"Saved {name}");
            return failure is null;
        }
    }

    private void ShowSaveFailure(string name, string message)
    {
        StatusText.Text = $"Couldn't save {name}: {message}";
        StatusText.Foreground = Ui.Theme.Error;
    }

    /// <summary>Right-click: cut, copy (what is selected, or the whole file) and paste, or select everything.</summary>
    private RetroMenu BuildMenu()
    {
        var menu = new RetroMenu { Width = 200 + 2 * Ui.Theme.MenuPadding };
        var cut = new RetroMenuItem { Text = "Cut", ShortcutText = "Ctrl+X", MinWidth = 0 };
        cut.Triggered += (_, _) => Cut();
        var copy = new RetroMenuItem { Text = "Copy", ShortcutText = "Ctrl+C", MinWidth = 0 };
        copy.Triggered += (_, _) => Copy();
        var paste = new RetroMenuItem { Text = "Paste", ShortcutText = "Ctrl+V", MinWidth = 0 };
        paste.Triggered += (_, _) => _ = Paste();
        var selectAll = new RetroMenuItem { Text = "Select all", ShortcutText = "Ctrl+A", MinWidth = 0 };
        selectAll.Triggered += (_, _) => Lines.SelectAll();
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new RetroMenuSeparator());
        menu.Items.Add(selectAll);
        menu.Opening += (_, _) =>
        {
            copy.Text = Lines.HasSelection ? "Copy" : "Copy the whole file";
            cut.IsEnabled = Lines.HasSelection;
        };
        return menu;
    }

    /// <summary>The menu bar under the title: File and Edit.</summary>
    internal ViewerMenuRow Menus { get; }

    private ViewerMenuRow BuildMenus() => new(
        ("File",
        [
            new("Save", "Ctrl+S", () => Save(), () => CanSave && Lines.IsModified && !saving),
            new("Save as…", "Ctrl+Shift+S", () => _ = SaveAs(), () => !saving),
            null,
            new("Close", "Ctrl+W", Close),
        ]),
        ("Edit",
        [
            new("Undo", "Ctrl+Z", () => Lines.Undo(), () => !ShowingTable && Lines.CanUndo),
            new("Redo", "Ctrl+Y", () => Lines.Redo(), () => !ShowingTable && Lines.CanRedo),
            null,
            new("Cut", "Ctrl+X", () => Cut(), () => !ShowingTable && Lines.HasSelection),
            new("Copy", "Ctrl+C", () => Copy()),
            new("Paste", "Ctrl+V", () => _ = Paste(), () => !ShowingTable),
            new("Select all", "Ctrl+A", Lines.SelectAll, () => !ShowingTable),
            null,
            new("Find…", "Ctrl+F", FocusFind),
            new("Find next", "F3", () => StepMatch(1), () => (FindField.Text ?? "").Length > 0),
            new("Find previous", "Shift+F3", () => StepMatch(-1), () => (FindField.Text ?? "").Length > 0),
        ]));

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
        // The dialog asking whether to save has the keyboard while it is open.
        if (UnsavedDialog is not null)
            return;
        KeyModifiers modifiers = e.KeyModifiers;
        bool ctrl = modifiers == KeyModifiers.Control;
        bool ctrlShift = modifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        // The text's own keys; in the find field, its own undo, cut and paste instead.
        bool inText = !ShowingTable && !FindField.IsKeyboardFocusWithin;
        switch (e.Key)
        {
            case Key.S when ctrl:
                Save();
                break;
            case Key.S when ctrlShift:
                _ = SaveAs();
                break;
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
            case Key.X when ctrl && inText:
                Cut();
                break;
            case Key.V when ctrl && inText:
                _ = Paste();
                break;
            case Key.Z when ctrl && inText:
                Lines.Undo();
                break;
            case Key.Y when ctrl && inText:
            case Key.Z when ctrlShift && inText:
                Lines.Redo();
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
