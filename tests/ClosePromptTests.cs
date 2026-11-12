using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views;
using HikariHopper.Views.Controls;
using Kit = HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests;

/// <summary>Closing the app, or a tab, with files unsaved: one prompt for all of them, and nothing lost unasked.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ClosePromptTests : ExplorerTestBase
{
    private static void Pump(int milliseconds = 20) => TestWait.Pump(milliseconds);

    private sealed record Explorer(MainWindow Window, ExplorerSession Session)
    {
        public bool Closed { get; set; }
    }

    private Explorer Open(int width = 1200, int height = 760)
    {
        MakeGame();
        File.WriteAllBytes(Path.Combine(Work, "handling.meta"), Encoding.UTF8.GetBytes("<CHandlingData />"));
        var session = new ExplorerSession();
        var window = new MainWindow { Width = width, Height = height };
        session.Attach(window);
        window.Show();
        session.Tabs.ActiveBridge.OpenGame(Work);
        Settle(session.Tabs.ActiveBridge);
        Pump(50);
        var explorer = new Explorer(window, session);
        window.Closed += (_, _) => explorer.Closed = true;
        return explorer;
    }

    /// <summary>handling.meta open in the text viewer from the active tab, with a change typed into it.</summary>
    private static TextViewer EditText(Explorer explorer)
    {
        ExplorerViewModel bridge = explorer.Session.Tabs.ActiveBridge;
        bridge.ActivateEntry(RowFor(bridge, "handling.meta"));
        Pump(400);
        TextViewer viewer = explorer.Session.TextViewer!;
        viewer.Lines.MoveCaret(new TextPosition(0, 0));
        viewer.Lines.Type("<!-- tuned -->");
        Assert.True(viewer.IsModified);
        return viewer;
    }

    /// <summary>A texture dictionary, a loose file, open in a tab's texture editor with a texture renamed.</summary>
    private string EditTextures(ExplorerViewModel tab, string name = "vehicles.ytd")
    {
        string path = Path.Combine(Work, name);
        File.WriteAllBytes(path, Kit.Bytes(Kit.Dictionary("paint")));
        TextureViewerViewModel textures = tab.TextureViewer;
        textures.OpenDictionary(name, path, File.ReadAllBytes(path));
        Kit.Settle(textures);
        Pump(300);
        Assert.True(textures.RenameSelected("tuned"));
        Kit.Settle(textures);
        Assert.True(textures.Modified);
        return path;
    }

    private static IReadOnlyList<string> SavedNames(string path) => YtdSaving.Read(File.ReadAllBytes(path)).Names;

    private static UnsavedFilesDialog Prompt(Explorer explorer)
    {
        TestWait.Until(() => explorer.Session.UnsavedPrompt is { IsOpen: true }, "the prompt about unsaved files");
        return explorer.Session.UnsavedPrompt!;
    }

    private static void Click(ChromeToolButton button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<(string Name, string Where)> Rows(UnsavedFilesDialog prompt) =>
        [.. prompt.FileRows.Children.Cast<Border>().Select(row => ((Grid)row.Child!).Children.OfType<TextBlock>().ToList())
            .Select(texts => (texts[0].Text ?? "", texts[1].Text ?? ""))];

    [AvaloniaFact]
    public void WithNothingUnsavedTheWindowClosesAtOnce()
    {
        Explorer explorer = Open();
        ExplorerViewModel bridge = explorer.Session.Tabs.ActiveBridge;
        bridge.ActivateEntry(RowFor(bridge, "handling.meta"));
        Pump(400);
        Assert.NotNull(explorer.Session.TextViewer);

        explorer.Window.Close();
        Assert.True(explorer.Closed);
        Assert.Null(explorer.Session.UnsavedPrompt);
    }

    [AvaloniaFact]
    public void ClosingWithFilesUnsavedAsksOnceAndCancelKeepsEverything()
    {
        Explorer explorer = Open();
        TextViewer text = EditText(explorer);
        string ytd = EditTextures(explorer.Session.Tabs.ActiveBridge);

        explorer.Window.Close();
        UnsavedFilesDialog prompt = Prompt(explorer);
        Assert.False(explorer.Closed);
        Assert.Equal("2 FILES HAVE UNSAVED CHANGES", prompt.HeadingText.Text);
        Assert.Equal("Save them before HikariHopper closes?", prompt.QuestionText.Text);
        // One tab: where each is open, and no tab named.
        Assert.Equal([("handling.meta", "Text editor"), ("vehicles.ytd", "Texture editor")], Rows(prompt));
        Assert.Equal([Ui.Theme.InkData, Ui.Theme.InkAsset],
            prompt.FileRows.Children.Cast<Border>().Select(row => ((Grid)row.Child!).Children.OfType<LucideIcon>().Single().Stroke));

        // Closing again while it asks asks nothing more.
        explorer.Window.Close();
        Assert.False(explorer.Closed);
        Assert.Same(prompt, explorer.Session.UnsavedPrompt);

        Click(prompt.CancelButton);
        TestWait.Until(() => explorer.Session.UnsavedPrompt is null, "the prompt to go");
        Assert.False(explorer.Closed);
        Assert.True(text.IsOpen && text.IsModified);
        Assert.True(explorer.Session.Tabs.ActiveBridge.TextureViewer.Modified);
        Assert.Equal(["paint"], SavedNames(ytd));

        // The close mark and Escape cancel as well.
        explorer.Window.Close();
        Click(Prompt(explorer).CloseMark);
        TestWait.Until(() => explorer.Session.UnsavedPrompt is null, "the prompt to go");
        explorer.Window.Close();
        Prompt(explorer);
        explorer.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        explorer.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestWait.Until(() => explorer.Session.UnsavedPrompt is null, "Escape to cancel");
        Assert.False(explorer.Closed);
        Assert.True(text.IsModified);
    }

    [AvaloniaFact]
    public void DontSaveClosesAndLeavesTheFilesAsTheyWere()
    {
        Explorer explorer = Open();
        EditText(explorer);
        string ytd = EditTextures(explorer.Session.Tabs.ActiveBridge);

        explorer.Window.Close();
        Click(Prompt(explorer).DiscardButton);
        TestWait.Until(() => explorer.Closed, "the window to close");
        Assert.Equal("<CHandlingData />", File.ReadAllText(Path.Combine(Work, "handling.meta")));
        Assert.Equal(["paint"], SavedNames(ytd));
    }

    [AvaloniaFact]
    public void SaveAllSavesEveryFileWhereItsEditorWouldThenCloses()
    {
        Explorer explorer = Open();
        EditText(explorer);
        string ytd = EditTextures(explorer.Session.Tabs.ActiveBridge);

        explorer.Window.Close();
        UnsavedFilesDialog prompt = Prompt(explorer);
        Click(prompt.SaveButton);
        // While they are written the prompt stays, and nothing in it can be pressed.
        Assert.True(prompt.Saving);
        Assert.False(prompt.CancelButton.IsEnabled || prompt.DiscardButton.IsEnabled || prompt.SaveButton.IsEnabled);
        TestWait.Until(() => explorer.Closed, "the window to close once both are saved");
        Assert.Equal("<!-- tuned --><CHandlingData />", File.ReadAllText(Path.Combine(Work, "handling.meta")));
        Assert.Equal(["tuned"], SavedNames(ytd));
    }

    [AvaloniaFact]
    public void ASaveThatFailsStopsTheClosingAndBringsItsEditorForward()
    {
        Explorer explorer = Open();
        TextViewer text = EditText(explorer);
        ExplorerViewModel first = explorer.Session.Tabs.ActiveBridge;
        // The textures are in a second tab, which isn't showing when the window is closed.
        explorer.Session.Tabs.NewTab();
        ExplorerViewModel second = explorer.Session.Tabs.ActiveBridge;
        string ytd = EditTextures(second);
        second.TextureViewer.WriteFile = (_, _) => throw new IOException("the disk is full");
        explorer.Session.Tabs.ActivateTab(0);
        Assert.Same(first, explorer.Session.Tabs.ActiveBridge);

        explorer.Window.Close();
        UnsavedFilesDialog prompt = Prompt(explorer);
        // Two tabs: each says which it is in.
        Assert.Equal([("handling.meta", "Text editor · tab 1"), ("vehicles.ytd", "Texture editor · tab 2")], Rows(prompt));
        Click(prompt.SaveButton);
        TestWait.Until(() => explorer.Session.UnsavedPrompt is null, "the prompt to go");
        Pump(300);

        Assert.False(explorer.Closed);
        // What saved is saved; what didn't is still there, its tab showing, its editor on top saying why.
        Assert.False(text.IsModified);
        Assert.True(second.TextureViewer.Modified);
        Assert.Equal(["paint"], SavedNames(ytd));
        Assert.Same(second, explorer.Session.Tabs.ActiveBridge);
        Assert.Equal("Could not save vehicles.ytd: the disk is full", second.TextureViewer.Status);
        TextureEditor editor = explorer.Session.TextureEditors[second];
        Assert.True(editor.IsOpen);
        Assert.True(Assert.IsType<ModalOverlay>(editor.Parent).IsTopmost);

        // Asked again, it can still be let go.
        explorer.Window.Close();
        Assert.Equal([("vehicles.ytd", "Texture editor · tab 2")], Rows(Prompt(explorer)));
        Click(explorer.Session.UnsavedPrompt!.DiscardButton);
        TestWait.Until(() => explorer.Closed, "the window to close");
    }

    [AvaloniaFact]
    public void ASaveUnderWayIsWaitedForBeforeTheWindowCloses()
    {
        Explorer explorer = Open();
        ExplorerViewModel bridge = explorer.Session.Tabs.ActiveBridge;
        string ytd = EditTextures(bridge);
        using var release = new ManualResetEventSlim();
        Action<string, byte[]> write = bridge.TextureViewer.WriteFile;
        bridge.TextureViewer.WriteFile = (path, data) =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            write(path, data);
        };
        Assert.True(bridge.TextureViewer.SaveYtd());

        explorer.Window.Close();
        Pump(200);
        // Nothing is asked, and the window waits for the save.
        Assert.False(explorer.Closed);
        Assert.Null(explorer.Session.UnsavedPrompt);
        release.Set();
        TestWait.Until(() => explorer.Closed, "the window to close once the save is written");
        Assert.Null(explorer.Session.UnsavedPrompt);
        Assert.Equal(["tuned"], SavedNames(ytd));
    }

    [AvaloniaFact]
    public void ClosingATabWithFilesUnsavedAsksAboutThatTabsFiles()
    {
        Explorer explorer = Open();
        ExplorerTabsViewModel tabs = explorer.Session.Tabs;
        EditText(explorer);
        // The text viewer is the first tab's; the second tab's textures are what its closing asks about.
        tabs.NewTab();
        ExplorerViewModel second = tabs.ActiveBridge;
        string ytd = EditTextures(second);

        tabs.CloseTab(1);
        UnsavedFilesDialog prompt = Prompt(explorer);
        Assert.Equal("1 FILE HAS UNSAVED CHANGES", prompt.HeadingText.Text);
        Assert.Equal("Save it before the tab closes?", prompt.QuestionText.Text);
        Assert.Equal([("vehicles.ytd", "Texture editor · tab 2")], Rows(prompt));
        Click(prompt.CancelButton);
        TestWait.Until(() => explorer.Session.UnsavedPrompt is null, "the prompt to go");
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.True(second.TextureViewer.Modified);

        tabs.CloseTab(1);
        Click(Prompt(explorer).SaveButton);
        TestWait.Until(() => tabs.Tabs.Count == 1, "the tab to close once its file is saved");
        Assert.Equal(["tuned"], SavedNames(ytd));
        Assert.False(explorer.Closed);
        Assert.True(explorer.Session.TextViewer!.IsModified);
    }

    [AvaloniaFact]
    public void DontSaveClosesTheTabAndWhatItOpened()
    {
        Explorer explorer = Open();
        ExplorerTabsViewModel tabs = explorer.Session.Tabs;
        tabs.NewTab();
        ExplorerViewModel second = tabs.ActiveBridge;
        second.OpenGame(Work);
        Settle(second);
        TextViewer text = EditText(explorer);
        Assert.Same(second, explorer.Session.TextViewerTab);

        tabs.CloseTab(1);
        UnsavedFilesDialog prompt = Prompt(explorer);
        Assert.Equal([("handling.meta", "Text editor · tab 2")], Rows(prompt));
        Click(prompt.DiscardButton);
        TestWait.Until(() => tabs.Tabs.Count == 1, "the tab to close");
        Assert.False(text.IsOpen);
        Assert.Null(explorer.Session.TextViewer);
        Assert.Equal("<CHandlingData />", File.ReadAllText(Path.Combine(Work, "handling.meta")));
        Assert.False(explorer.Closed);
    }

    [AvaloniaFact]
    public void ATabWithNothingUnsavedClosesAtOnce()
    {
        Explorer explorer = Open();
        ExplorerTabsViewModel tabs = explorer.Session.Tabs;
        tabs.NewTab();
        tabs.CloseTab(1);
        Assert.Single(tabs.Tabs);
        Assert.Null(explorer.Session.UnsavedPrompt);
    }

    [AvaloniaFact]
    public void FileExitAsksAsTheCloseButtonDoes()
    {
        Explorer explorer = Open();
        EditText(explorer);
        var screen = (ExplorerScreen)explorer.Window.ExplorerScreenHost.Content!;
        RetroMenuItem exit = screen.Menu.MenuFor(screen.Menu.FileButton).Items.OfType<RetroMenuItem>().Single(item => item.Text == "Exit");
        exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal([("handling.meta", "Text editor")], Rows(Prompt(explorer)));
        Assert.False(explorer.Closed);
    }

    [AvaloniaFact]
    public void AnUnsavedTextHoldsAnUpdatesRestartBack()
    {
        Explorer explorer = Open();
        EditText(explorer);
        var screen = (ExplorerScreen)explorer.Window.ExplorerScreenHost.Content!;
        Assert.Equal("Save or discard your changes first", screen.Menu.Updates.Blocker());
    }
}
