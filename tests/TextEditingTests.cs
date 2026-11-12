using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Editing a text file in the text viewer, and saving it in its own encoding and line endings.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class TextEditingTests : ExplorerTestBase
{
    private static void Pump(int milliseconds = 20) => ExplorerStateRenders.Pump(milliseconds);

    private static (MainWindow Window, TextViewer Viewer) Open(string name, string text, SaveTarget? target = null) =>
        Open(name, Encoding.UTF8.GetBytes(text), target);

    private static (MainWindow Window, TextViewer Viewer) Open(string name, byte[] data, SaveTarget? target = null)
    {
        var window = new MainWindow { Width = 1000, Height = 640 };
        window.Show();
        var viewer = new TextViewer(new TextFile(TextDocument.Load(name, data)) { SaveTarget = target }) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        Pump(400);
        viewer.Lines.Focus();
        Pump();
        return (window, viewer);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Pump();
    }

    private static void Type(MainWindow window, string text)
    {
        window.KeyTextInput(text);
        Pump();
    }

    private static void Wait(Task task)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            Pump(10);
        Assert.True(task.IsCompleted);
    }

    private static void Click(Avalonia.Controls.Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    // ---------------- Typing ----------------

    [AvaloniaFact]
    public void TypingDeletingAndNewLinesEditTheText()
    {
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "    alpha beta\r\nend");
        TextLinesView lines = viewer.Lines;
        lines.MoveCaret(new TextPosition(0, 14));
        Type(window, " gamma");
        Assert.Equal("    alpha beta gamma", viewer.Document.Lines[0]);
        Assert.Equal("Ln 1, Col 21", viewer.CaretText.Text);

        // Ctrl+Backspace takes the word, Backspace a character.
        Key(window, PhysicalKey.Backspace, RawInputModifiers.Control);
        Assert.Equal("    alpha beta ", viewer.Document.Lines[0]);
        Key(window, PhysicalKey.Backspace);
        Assert.Equal("    alpha beta", viewer.Document.Lines[0]);

        // Enter keeps the line's indent, and the break is the file's own.
        Key(window, PhysicalKey.Enter);
        Assert.Equal(new TextPosition(1, 4), lines.Caret);
        Type(window, "x");
        Assert.Equal("    alpha beta\r\n    x\r\nend", viewer.Document.Text);
        Assert.Equal("Ln 2, Col 6", viewer.CaretText.Text);
        Assert.Equal("3 lines", viewer.LinesText.Text);

        // Delete at a line's end joins the next one; Ctrl+Delete takes a word.
        Key(window, PhysicalKey.Delete);
        Assert.Equal("    xend", viewer.Document.Lines[1]);
        lines.MoveCaret(new TextPosition(0, 4));
        Key(window, PhysicalKey.Delete, RawInputModifiers.Control);
        Assert.Equal("    beta", viewer.Document.Lines[0]);

        // Typing over a selection replaces it.
        lines.MoveCaret(new TextPosition(0, 4));
        lines.MoveCaret(new TextPosition(0, 8), extend: true);
        Type(window, "B");
        Assert.Equal("    B", viewer.Document.Lines[0]);
        Assert.False(lines.HasSelection);

        // Tab puts in a tab character, shown as spaces to the next stop; the focus stays in the text.
        lines.MoveCaret(new TextPosition(0, 0));
        Key(window, PhysicalKey.Tab);
        Assert.Equal("\t    B", viewer.Document.RawLine(0));
        Assert.Equal("        B", viewer.Document.Lines[0]);
        Assert.Equal(new TextPosition(0, 4), lines.Caret);
        Assert.True(lines.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCaretStepsOverATabWholeAndNeverStopsInsideIt()
    {
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "a\tb");
        TextLinesView lines = viewer.Lines;
        Assert.Equal("a   b", viewer.Document.Lines[0]);
        lines.MoveCaret(new TextPosition(0, 1));
        Key(window, PhysicalKey.ArrowRight);
        Assert.Equal(new TextPosition(0, 4), lines.Caret);
        Key(window, PhysicalKey.ArrowLeft);
        Assert.Equal(new TextPosition(0, 1), lines.Caret);
        // A place inside the tab's spaces goes to its nearer end.
        lines.MoveCaret(new TextPosition(0, 3));
        Assert.Equal(new TextPosition(0, 4), lines.Caret);
        // Backspace after the tab takes the tab.
        Key(window, PhysicalKey.Backspace);
        Assert.Equal("ab", viewer.Document.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void UndoAndRedoTakeARunOfTypingAsOneStep()
    {
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "one");
        TextLinesView lines = viewer.Lines;
        lines.MoveCaret(new TextPosition(0, 3));
        Assert.Equal("notes.txt", viewer.TitleText.Text);
        Type(window, "a");
        Type(window, "b");
        Type(window, "c");
        Assert.Equal("notes.txt *", viewer.TitleText.Text);

        // Ctrl+Z takes the whole run back, and the text is as read again.
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("one", viewer.Document.Text);
        Assert.Equal("notes.txt", viewer.TitleText.Text);
        Assert.False(lines.CanUndo);
        Key(window, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal("oneabc", viewer.Document.Text);
        Assert.Equal(new TextPosition(0, 6), lines.Caret);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Key(window, PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("oneabc", viewer.Document.Text);

        // A caret moved between two runs makes them two steps.
        lines.MoveCaret(new TextPosition(0, 0));
        Type(window, "x");
        Type(window, "y");
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("oneabc", viewer.Document.Text);

        // A run of Backspace is one step too, and undoing it puts the selection back as it was.
        lines.MoveCaret(new TextPosition(0, 6));
        Key(window, PhysicalKey.Backspace);
        Key(window, PhysicalKey.Backspace);
        Key(window, PhysicalKey.Backspace);
        Assert.Equal("one", viewer.Document.Text);
        Assert.True(viewer.Menus.Run("Edit", "Undo"));
        Assert.Equal("oneabc", viewer.Document.Text);
        Assert.Equal(new TextPosition(0, 6), lines.Caret);
        Assert.True(viewer.Menus.ItemsOf("Edit").Single(item => item.Text == "Redo").Enabled);
        window.Close();
    }

    [AvaloniaFact]
    public void CutAndPasteGoThroughTheClipboardInTheFilesLineEndings()
    {
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "one two\r\nthree");
        TextLinesView lines = viewer.Lines;
        Assert.False(viewer.Menus.ItemsOf("Edit").Single(item => item.Text == "Cut").Enabled);
        lines.MoveCaret(new TextPosition(0, 4));
        lines.MoveCaret(new TextPosition(0, 7), extend: true);
        Assert.True(viewer.Menus.ItemsOf("Edit").Single(item => item.Text == "Cut").Enabled);
        Key(window, PhysicalKey.X, RawInputModifiers.Control);
        Assert.Equal("two", Clipboard.Text);
        Assert.Equal("one \r\nthree", viewer.Document.Text);
        Assert.Equal("Cut 3 characters", viewer.StatusText.Text);

        // Pasted lines take the file's CRLF, whatever the clipboard had.
        Clipboard.SetTextAsync("a\nb");
        Key(window, PhysicalKey.V, RawInputModifiers.Control);
        Pump();
        Assert.Equal("one a\r\nb\r\nthree", viewer.Document.Text);
        Assert.Equal(new TextPosition(1, 1), lines.Caret);
        // One step: undone, the paste goes whole.
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("one \r\nthree", viewer.Document.Text);

        // Ctrl+A, then typing, replaces everything.
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Type(window, "all");
        Assert.Equal("all", viewer.Document.Text);
        Assert.Equal("", viewer.EndingText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheFindLooksAgainAfterAnEditWithoutMovingTheCaret()
    {
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "alpha beta\r\ngamma\r\n");
        viewer.FindField.Text = "beta";
        Pump();
        Assert.Equal(1, viewer.MatchCount);
        viewer.Lines.Focus();
        viewer.Lines.MoveCaret(new TextPosition(1, 5));
        Type(window, " beta");
        Pump(400);
        Assert.Equal(2, viewer.MatchCount);
        Assert.Equal(new TextPosition(1, 10), viewer.Lines.Caret);
        Assert.False(viewer.Lines.HasSelection);
        Type(window, "!");
        Assert.Equal("gamma beta!", viewer.Document.Lines[1]);
        window.Close();
    }

    [AvaloniaFact]
    public void TypingInABigFileStaysQuick()
    {
        var text = new StringBuilder();
        for (int line = 0; line < 200_000; line++)
            text.Append("<Item key=\"value\" />\r\n");
        (MainWindow window, TextViewer viewer) = Open("big.meta", text.ToString());
        viewer.Lines.MoveCaret(new TextPosition(100_000, 5));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int key = 0; key < 40; key++)
        {
            window.KeyTextInput("x");
            if (key % 10 == 9)
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
        Pump();
        // Only the lines touched are made again: whole-file work on each key would take seconds.
        Assert.True(clock.ElapsedMilliseconds < 4000, $"{clock.ElapsedMilliseconds} ms for 44 keys");
        Assert.Equal(200_005, viewer.Document.Lines.Count);
        window.Close();
    }

    // ---------------- Encodings and line endings ----------------

    [Theory]
    [InlineData("one\r\ntwo\r\n")]
    [InlineData("one\ntwo")]
    [InlineData("one\rtwo\r")]
    [InlineData("a\r\nb\nc\rd")]
    [InlineData("no break at all")]
    [InlineData("\t<a>\r\n\t\t<b />\r\n")]
    public void UnchangedTextSavesAsTheBytesItWasReadFrom(string text)
    {
        foreach (byte[] data in new[]
        {
            Encoding.UTF8.GetBytes(text),
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)],
            [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)],
            [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)],
        })
            Assert.Equal(data, TextDocument.Load("a.txt", data).Encode());
        // Windows-1252, its high bytes and all.
        byte[] legacy = [.. Encoding.ASCII.GetBytes(text), (byte)'c', (byte)'a', (byte)'f', 0xE9, 0x80, 0x81, 0x9F];
        TextDocument document = TextDocument.Load("a.txt", legacy);
        Assert.Equal("Windows-1252", document.Encoding);
        Assert.Equal(legacy, document.Encode());
    }

    private static RawPosition End(TextDocument document) =>
        new(document.Lines.Count - 1, document.RawLine(document.Lines.Count - 1).Length);

    [AvaloniaFact]
    public void EditedTextKeepsItsEncodingAndEachLinesOwnEnding()
    {
        // UTF-16 with its mark; a new line takes the file's CRLF.
        TextDocument utf16 = TextDocument.Load("a.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("é\r\nü")]);
        utf16.Replace(End(utf16), End(utf16), TextRun.Of("\nñ", utf16.NewLine));
        Assert.Equal([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("é\r\nü\r\nñ")], utf16.Encode());

        // Mixed: each line keeps its break; new ones take the most common (CRLF here, two to one).
        TextDocument mixed = TextDocument.Load("a.txt", "a\r\nb\r\nc\nd"u8.ToArray());
        Assert.Equal(("Mixed", "\r\n"), (mixed.LineEnding, mixed.NewLine));
        mixed.Replace(new RawPosition(3, 1), new RawPosition(3, 1), TextRun.Of("x\ny", mixed.NewLine));
        Assert.Equal("a\r\nb\r\nc\ndx\r\ny", mixed.Text);
        // Joining b to c, the line keeps the break c had; the file still doesn't end in one.
        mixed.Replace(new RawPosition(1, 1), new RawPosition(2, 0), TextRun.Empty);
        Assert.Equal("a\r\nbc\ndx\r\ny", mixed.Text);
        Assert.Equal("Mixed", mixed.LineEnding);
        Assert.Equal("\n", TextDocument.Load("a.txt", "a\nb\nc\r\nd"u8.ToArray()).NewLine);
        Assert.Equal("\r\n", TextDocument.Load("a.txt", "single"u8.ToArray()).NewLine);

        // What is undone puts back exactly the breaks it took, a lone CR before an empty LF line among them.
        (MainWindow window, TextViewer viewer) = Open("a.txt", "x\ry\nz\n");
        viewer.Lines.MoveCaret(new TextPosition(1, 0));
        viewer.Lines.MoveCaret(new TextPosition(1, 1), extend: true);
        viewer.Lines.Backspace();
        Assert.Equal("x\r\nz\n", viewer.Document.Text);
        Assert.True(viewer.Lines.Undo());
        Assert.Equal("x\ry\nz\n", viewer.Document.Text);
        viewer.Lines.MoveCaret(new TextPosition(0, 1));
        viewer.Lines.MoveCaret(new TextPosition(2, 0), extend: true);
        viewer.Lines.Backspace();
        Assert.True(viewer.Lines.Undo());
        Assert.Equal("x\ry\nz\n"u8.ToArray(), viewer.Document.Encode());
        window.Close();
    }

    [AvaloniaFact]
    public void TextWindows1252CantHoldFailsTheSaveInsteadOfWritingQuestionMarks()
    {
        TextDocument document = TextDocument.Load("a.txt", [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n', (byte)'x']);
        document.Replace(new RawPosition(1, 1), new RawPosition(1, 1), TextRun.Of("ł", "\n"));
        var error = Assert.Throws<InvalidDataException>(document.Encode);
        Assert.Contains("ł", error.Message);
        Assert.Contains("line 2, column 2", error.Message);

        // In the viewer: nothing is written, nothing let go of, and the status says why.
        bool written = false, prepared = false;
        (MainWindow window, TextViewer viewer) = Open("a.txt", [(byte)'c', (byte)'a', (byte)'f', 0xE9],
            new SaveTarget(_ => written = true, () => prepared = true));
        viewer.Lines.MoveCaret(new TextPosition(0, 4));
        Type(window, "ł");
        Assert.False(viewer.Save());
        Assert.False(written || prepared);
        Assert.StartsWith("Couldn't save a.txt: Windows-1252, the file's encoding, has no “ł”", viewer.StatusText.Text);
        Assert.Equal("a.txt *", viewer.TitleText.Text);
        window.Close();
    }

    // ---------------- Saving ----------------

    private (MainWindow Window, ExplorerSession Session) Explorer(params (string Name, string Text)[] files)
    {
        MakeGame();
        foreach ((string name, string text) in files)
            File.WriteAllBytes(Path.Combine(Work, name), Encoding.UTF8.GetBytes(text));
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1200, Height = 760 };
        session.Attach(window);
        window.Show();
        session.Tabs.ActiveBridge.OpenGame(Work);
        Settle(session.Tabs.ActiveBridge);
        Pump(50);
        return (window, session);
    }

    private static void Activate(ExplorerSession session, string name)
    {
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.ActivateEntry(RowFor(bridge, name));
        Pump(400);
    }

    [AvaloniaFact]
    public void CtrlSSavesOverTheLooseFileAndEditingGoesOn()
    {
        (MainWindow window, ExplorerSession session) = Explorer(("notes.txt", "one\r\ntwo"));
        Activate(session, "notes.txt");
        TextViewer viewer = session.TextViewer!;
        Assert.False(viewer.Menus.ItemsOf("File").Single(item => item.Text == "Save").Enabled);
        viewer.Lines.Focus();
        viewer.Lines.MoveCaret(new TextPosition(1, 3));
        Type(window, "!");
        Assert.Equal("notes.txt *", viewer.TitleText.Text);
        Assert.True(viewer.Menus.ItemsOf("File").Single(item => item.Text == "Save").Enabled);

        Key(window, PhysicalKey.S, RawInputModifiers.Control);
        Wait(viewer.Saving);
        Assert.True(viewer.Saving.Result, viewer.StatusText.Text);
        Assert.Equal("one\r\ntwo!"u8.ToArray(), File.ReadAllBytes(Path.Combine(Work, "notes.txt")));
        Assert.Equal("notes.txt", viewer.TitleText.Text);
        Assert.Equal("Saved notes.txt", viewer.StatusText.Text);
        Assert.Equal(Formatting.FormatSize(9), viewer.SizeText.Text);
        Assert.Equal("Saved notes.txt", session.Tabs.ActiveBridge.Status);

        // Still editable, and saved again where it was.
        Type(window, "?");
        Assert.Equal("notes.txt *", viewer.TitleText.Text);
        Assert.True(viewer.Save());
        Wait(viewer.Saving);
        Assert.Equal("one\r\ntwo!?"u8.ToArray(), File.ReadAllBytes(Path.Combine(Work, "notes.txt")));
        // Undone to what was saved before, it is changed again: the file on disk has the "?".
        Key(window, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("notes.txt *", viewer.TitleText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void ATextFileInAnRpfIsSavedBackIntoIt()
    {
        MakeGame();
        string archivePath = SaveArchive(Path.Combine(Work, "data.rpf"), ("notes/readme.txt", "hello\nworld\n"), ("notes/keep.txt", "untouched"));
        // In tabs, as the app has it: a write into an RPF has every tab let go of it first.
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel bridge = tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        Assert.True(bridge.GoToAddress(@"data.rpf\notes"));
        Settle(bridge);
        TextFile? opened = null;
        bridge.TextFileOpened += (_, file) => opened = file;
        bridge.ActivateEntry(RowFor(bridge, "readme.txt"));
        Assert.NotNull(opened?.SaveTarget);

        var viewer = new TextViewer(opened!);
        viewer.Lines.MoveCaret(new TextPosition(1, 5));
        viewer.Lines.Type("!");
        // Just after the game opens, the folder tree is still looking into its RPFs on a worker of its own, which holds
        // the file a moment; a save then fails and says so, and the next one goes through.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Assert.True(viewer.Save());
            Wait(viewer.Saving);
            if (viewer.Saving.Result)
                break;
            Assert.Equal("readme.txt *", viewer.TitleText.Text);
            Pump(100);
        }
        Assert.True(viewer.Saving.Result, viewer.StatusText.Text);
        Assert.Equal("readme.txt", viewer.TitleText.Text);
        Settle(bridge);
        Assert.Equal("hello\nworld!\n"u8.ToArray(), ReadSavedEntry(archivePath, "notes/readme.txt"));
        Assert.Equal("untouched"u8.ToArray(), ReadSavedEntry(archivePath, "notes/keep.txt"));
        Assert.Equal("Saved readme.txt", bridge.Status);
    }

    private sealed class SaveDialogs(string? path) : IFileDialogs
    {
        public List<(string Title, string Suggested)> Asked { get; } = [];

        public Task<string?> PickFolderAsync(string title, string? startPath = null) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters, bool allowMultiple = false,
            string? startPath = null) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters, string? startPath = null)
        {
            Asked.Add((title, suggestedName));
            return Task.FromResult(path);
        }
    }

    [AvaloniaFact]
    public void SaveAsWritesALooseCopyThatSaveThenSavesTo()
    {
        // Nowhere of its own: Save can't, Save as can.
        (MainWindow window, TextViewer viewer) = Open("handling.meta", "<a />\r\n");
        Assert.Equal("Save as… to keep changes", viewer.StatusText.Text);
        viewer.Lines.MoveCaret(new TextPosition(0, 5));
        Type(window, "<b />");
        Assert.False(viewer.Menus.ItemsOf("File").Single(item => item.Text == "Save").Enabled);
        Assert.False(viewer.Save());

        string copy = Path.Combine(Work, "copy.meta");
        var dialogs = new SaveDialogs(copy);
        viewer.Dialogs = dialogs;
        Task<bool> saving = viewer.SaveAs();
        Wait(saving);
        Assert.True(saving.Result, viewer.StatusText.Text);
        Assert.Equal([("Save handling.meta as", "handling.meta")], dialogs.Asked);
        Assert.Equal("<a /><b />\r\n"u8.ToArray(), File.ReadAllBytes(copy));
        Assert.Equal("copy.meta", viewer.TitleText.Text);
        Assert.Equal("Saved copy.meta", viewer.StatusText.Text);

        Type(window, "x");
        Assert.Equal("copy.meta *", viewer.TitleText.Text);
        Key(window, PhysicalKey.S, RawInputModifiers.Control);
        Wait(viewer.Saving);
        Assert.Equal("<a /><b />x\r\n"u8.ToArray(), File.ReadAllBytes(copy));
        window.Close();
    }

    [AvaloniaFact]
    public void WhileASaveIsWrittenTheStatusSaysSoAndTypingGoesOn()
    {
        using var gate = new ManualResetEventSlim();
        byte[]? written = null;
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "one", new SaveTarget(bytes =>
        {
            gate.Wait(10_000);
            written = bytes;
        }));
        viewer.Lines.MoveCaret(new TextPosition(0, 3));
        Type(window, "!");
        Assert.True(viewer.Save());
        Assert.Equal("Saving notes.txt…", viewer.StatusText.Text);
        // Nor can a second save start meanwhile.
        Assert.False(viewer.Menus.ItemsOf("File").Single(item => item.Text == "Save as…").Enabled);
        Type(window, "?");
        gate.Set();
        Wait(viewer.Saving);
        // What was saved is the text as it was when the save began; what was typed since is still a change.
        Assert.Equal("one!"u8.ToArray(), written);
        Assert.Equal("Saved notes.txt", viewer.StatusText.Text);
        Assert.Equal("notes.txt *", viewer.TitleText.Text);
        Assert.Equal("one!?", viewer.Document.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void ClosingWithChangesUnsavedAsksFirst()
    {
        byte[]? written = null;
        (MainWindow window, TextViewer viewer) = Open("notes.txt", "one", new SaveTarget(bytes => written = bytes));
        // Unchanged, Ctrl+W just closes.
        viewer.Lines.MoveCaret(new TextPosition(0, 3));
        Type(window, "!");

        // Ctrl+W asks; Cancel stays, and the text has the keyboard again.
        Key(window, PhysicalKey.W, RawInputModifiers.Control);
        Assert.NotNull(viewer.UnsavedDialog);
        Assert.Equal("Save changes to notes.txt?", viewer.UnsavedDialog!.QuestionText.Text);
        Assert.True(viewer.IsOpen);
        Click(viewer.UnsavedDialog.CancelButton);
        Pump(50);
        Assert.Null(viewer.UnsavedDialog);
        Assert.True(viewer.IsOpen);

        // Escape and the close mark ask too; Save saves, then closes.
        Key(window, PhysicalKey.Escape);
        Assert.NotNull(viewer.UnsavedDialog);
        Key(window, PhysicalKey.Escape);
        Assert.Null(viewer.UnsavedDialog);
        Assert.True(viewer.IsOpen);
        Click(viewer.CloseMark);
        Assert.NotNull(viewer.UnsavedDialog);
        Click(viewer.UnsavedDialog!.SaveButton);
        Wait(viewer.Saving);
        Pump(400);
        Assert.Equal("one!"u8.ToArray(), written);
        Assert.False(viewer.IsOpen);
        window.Close();

        // Don't save closes without writing.
        written = null;
        (window, viewer) = Open("notes.txt", "one", new SaveTarget(bytes => written = bytes));
        viewer.Lines.Type("?");
        viewer.Close();
        Click(viewer.UnsavedDialog!.DiscardButton);
        Pump(400);
        Assert.False(viewer.IsOpen);
        Assert.Null(written);
        window.Close();
    }

    [AvaloniaFact]
    public void OpeningAnotherFileOverChangesAsksAndCancelledKeepsTheFirst()
    {
        (MainWindow window, ExplorerSession session) = Explorer(("a.txt", "first"), ("b.txt", "second"));
        Activate(session, "a.txt");
        TextViewer first = session.TextViewer!;
        first.Lines.Type("1");

        // Cancelled, the first stays, and the second doesn't open.
        Activate(session, "b.txt");
        Assert.Same(first, session.TextViewer);
        Assert.NotNull(first.UnsavedDialog);
        Click(first.UnsavedDialog!.CancelButton);
        Pump(400);
        Assert.Same(first, session.TextViewer);
        Assert.True(first.IsOpen);

        // Don't save: the first goes unsaved and the second opens.
        Activate(session, "b.txt");
        Click(first.UnsavedDialog!.DiscardButton);
        Pump(400);
        TextViewer second = session.TextViewer!;
        Assert.NotSame(first, second);
        Assert.Equal("b.txt", second.TitleText.Text);
        Assert.True(second.IsOpen);
        Assert.False(first.IsOpen);
        Assert.Equal("first", File.ReadAllText(Path.Combine(Work, "a.txt")));

        // Unchanged, the next file opens over it at once.
        Activate(session, "a.txt");
        Assert.Equal("a.txt", session.TextViewer!.TitleText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCsvTableShowsTheTextAsEdited()
    {
        (MainWindow window, TextViewer viewer) = Open("vehicles.csv", "model,price\r\nadder,1000\r\n");
        Assert.True(viewer.ShowingTable);
        Assert.Equal("1 rows", viewer.LinesText.Text);
        // The table is only read: the text is where it is edited.
        Assert.False(viewer.Menus.ItemsOf("Edit").Single(item => item.Text == "Paste").Enabled);
        viewer.ShowTable(false);
        Pump();
        viewer.Lines.Focus();
        viewer.Lines.MoveCaret(new TextPosition(2, 0));
        Type(window, "blista,15000");
        Assert.Equal("model,price\r\nadder,1000\r\nblista,15000", viewer.Document.Text);
        viewer.ShowTable(true);
        Assert.Equal("2 rows", viewer.LinesText.Text);
        Assert.Equal("blista", viewer.Table.Table!.Rows[2][0]);
        window.Close();
    }
}
