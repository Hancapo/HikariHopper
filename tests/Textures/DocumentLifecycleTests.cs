using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Port of the texture side of test_document_lifecycle.py.</summary>
public sealed class DocumentLifecycleTests
{
    private static (TextureViewerViewModel Viewer, string Path) EditedDocument(TempFolder folder, FakeDialogs? dialogs = null)
    {
        string path = folder.File("first.ytd");
        File.WriteAllBytes(path, Bytes(Dictionary("paint")));
        TextureViewerViewModel viewer = Viewer(dialogs: dialogs);
        viewer.OpenDictionary("first.ytd", path, File.ReadAllBytes(path));
        Settle(viewer);
        Assert.True(viewer.RenameSelected("edited"));
        return (viewer, path);
    }

    private static IReadOnlyList<string> SavedNames(string path) => YtdSaving.Read(File.ReadAllBytes(path)).Names;

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("discard")]
    [InlineData("save")]
    public Task OpeningAnotherYtdRequiresADecision(string decision) => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var (viewer, path) = EditedDocument(folder);
        int prompts = 0, opens = 0;
        viewer.UnsavedChangesRequested += (_, _) => prompts++;
        viewer.OpenRequested += (_, _) => opens++;
        viewer.OpenDictionary("second.ytd", "", Bytes(Dictionary("other")));
        Assert.Equal(1, prompts);
        Assert.Equal("first.ytd", viewer.SourceName);
        Assert.True(viewer.Modified);
        await viewer.ResolvePendingChanges(decision);
        Settle(viewer);
        Assert.Equal(decision == "cancel" ? "first.ytd" : "second.ytd", viewer.SourceName);
        Assert.Equal(decision == "cancel" ? 0 : 1, opens);
        Assert.Equal(decision == "save" ? ["edited"] : ["paint"], SavedNames(path));
        if (decision == "cancel")
        {
            Assert.True(viewer.Modified);
            Assert.True(viewer.CanUndo);
        }
        Assert.Null(viewer.PendingChange);
    });

    [AvaloniaFact]
    public Task ClosingADocumentThatWasSavedTellsWhoOpenedItWhatItSaved() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string path = folder.File("first.ytd");
        File.WriteAllBytes(path, Bytes(Dictionary("paint")));
        TextureViewerViewModel viewer = Viewer();
        var told = new List<SavedDocument>();
        viewer.OpenDictionary("first.ytd", path, File.ReadAllBytes(path), closedAfterSave: told.Add);
        Settle(viewer);

        // Saved twice: the last save is what it's told, once, when the document closes.
        Assert.True(viewer.RenameSelected("edited"));
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.True(viewer.RenameSelected("again"));
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.Empty(told);
        viewer.CloseDocument();
        SavedDocument saved = Assert.Single(told);
        Assert.Equal(["again"], saved.Textures.Select(texture => texture.Name));
        Assert.Equal(File.ReadAllBytes(path), saved.Data);

        // Closed without saving: nothing to tell.
        viewer.OpenDictionary("first.ytd", path, File.ReadAllBytes(path), closedAfterSave: told.Add);
        Settle(viewer);
        viewer.CloseDocument();
        Assert.Single(told);

        // Replaced by another after a save: it's told too, and the next document's own isn't.
        viewer.OpenDictionary("first.ytd", path, File.ReadAllBytes(path), closedAfterSave: told.Add);
        Settle(viewer);
        Assert.True(viewer.RenameSelected("third"));
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        viewer.OpenDictionary("second.ytd", "", Bytes(Dictionary("other")));
        Settle(viewer);
        Assert.Equal(2, told.Count);
        Assert.Equal(["third"], told[1].Textures.Select(texture => texture.Name));
        viewer.CloseDocument();
        Assert.Equal(2, told.Count);
    });

    [AvaloniaFact]
    public Task RequestCloseClearsTheDocumentAfterDiscard() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var (viewer, _) = EditedDocument(folder);
        int closed = 0;
        viewer.CloseRequested += (_, _) => closed++;
        viewer.RequestClose();
        Assert.Equal(0, closed);
        await viewer.ResolvePendingChanges("discard");
        Settle(viewer);
        Assert.Equal(1, closed);
        Assert.Equal(0, viewer.TextureCount);
        Assert.False(viewer.Modified);
        Assert.False(viewer.HasDocument);
        Assert.Equal("No texture dictionary loaded", viewer.Status);
    });

    [AvaloniaFact]
    public Task FailedSaveCancelsDocumentSwitch() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var (viewer, _) = EditedDocument(folder);
        viewer.WriteFile = (_, _) => throw new IOException("simulated write failure");
        var results = new List<bool>();
        viewer.SaveFinished += (_, success) => results.Add(success);
        viewer.OpenDictionary("second.ytd", "", Bytes(Dictionary("other")));
        await viewer.ResolvePendingChanges("save");
        Settle(viewer);
        Assert.Equal("first.ytd", viewer.SourceName);
        Assert.True(viewer.Modified);
        Assert.Equal("Could not save first.ytd: simulated write failure", viewer.Status);
        Assert.Null(viewer.PendingChange);
        Assert.Equal([false], results);
    });

    [AvaloniaFact]
    public Task CancelledSaveAsPreservesDocument() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogs { SaveFile = null };
        var (viewer, _) = EditedDocument(folder, dialogs);
        viewer.SetSourceForTesting("first.ytd", "");
        viewer.OpenDictionary("second.ytd", "", Bytes(Dictionary("other")));
        await viewer.ResolvePendingChanges("save");
        Assert.Equal(["Save texture dictionary"], dialogs.Titles);
        Assert.Equal("first.ytd", viewer.SourceName);
        Assert.True(viewer.Modified);
        Assert.Null(viewer.PendingChange);
    });

    [AvaloniaFact]
    public Task DocumentChangeWaitsForAnInFlightSave() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        var (viewer, _) = EditedDocument(folder);
        using var release = new ManualResetEventSlim();
        Assert.True(viewer.StartSave("first.ytd", () => release.Wait(5000)));
        int ran = 0;
        try
        {
            viewer.RequestDocumentChange(() => ran++);
            Assert.True(viewer.Saving);
            Assert.Equal(0, ran);
        }
        finally
        {
            release.Set();
        }
        Settle(viewer);
        Assert.Equal(1, ran);
        Assert.False(viewer.Saving);
    });

    [AvaloniaFact]
    public Task BusyOperationsBlockDocumentChanges() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(Dictionary("paint"));
        using var release = new ManualResetEventSlim();
        Assert.True(viewer.StartTransform("Resizing", texture => { release.Wait(5000); return texture; }));
        int ran = 0;
        viewer.RequestDocumentChange(() => ran++);
        Assert.Equal("Wait for the texture operation to finish", viewer.Status);
        release.Set();
        Settle(viewer);
        Assert.Equal(0, ran);
        viewer.RequestDocumentChange(() => ran++);
        Assert.Equal(1, ran);
    });

    [AvaloniaFact]
    public Task ReadFailureReportsTheError() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Viewer();
        int opens = 0;
        viewer.OpenRequested += (_, _) => opens++;
        viewer.OpenDictionary("broken.ytd", "", [1, 2, 3]);
        Assert.True(viewer.Loading);
        Assert.Equal("Reading texture dictionary…", viewer.Status);
        Settle(viewer);
        Assert.Equal(1, opens);
        Assert.False(viewer.Loading);
        Assert.False(viewer.HasDocument);
        Assert.NotEqual("", viewer.Error);
        Assert.Equal($"Could not read texture dictionary: {viewer.Error}", viewer.Status);
    });
}
