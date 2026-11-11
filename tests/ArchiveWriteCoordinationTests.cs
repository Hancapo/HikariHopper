using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Explorer side of <c>tests/test_archive_write_coordination.py</c> and <c>tests/test_document_lifecycle.py</c>.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ArchiveWriteCoordinationTests : ExplorerTestBase
{
    private (ExplorerTabsViewModel Tabs, ExplorerViewModel First, ExplorerViewModel Peer, string Path) SharedArchive()
    {
        string path = SaveArchive(System.IO.Path.Combine(Work, "shared.rpf"), ("textures.ytd", "ytd"), ("sibling.txt", "untouched"));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.ActiveBridge;
        first.OpenArchive(path);
        tabs.NewTab();
        ExplorerViewModel peer = tabs.ActiveBridge;
        peer.OpenArchive(path);
        return (tabs, first, peer, path);
    }

    [AvaloniaFact]
    public void DeletingNestedRpfReturnsPeerToRoot() => OnUiThread(() =>
    {
        string path = SaveArchive(Path.Combine(Work, "outer.rpf"), ("inner.rpf", Archive("inner.rpf", ("payload.txt", "example"))));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.ActiveBridge;
        first.OpenArchive(path);
        tabs.NewTab();
        ExplorerViewModel peer = tabs.ActiveBridge;
        peer.OpenArchive(path);
        peer.ActivateEntry(RowFor(peer, "inner.rpf"));
        Assert.Equal("inner.rpf", peer.ArchiveName);

        first.SelectEntry(RowFor(first, "inner.rpf"));
        Assert.True(first.DeleteSelectedFiles());
        Settle(tabs);

        Assert.False(SavedEntryExists(path, "inner.rpf"));
        Assert.Equal("outer.rpf", peer.ArchiveName);
        Assert.Equal("", peer.Provider.ArchivePrefix);
        Assert.Equal(".", peer.CurrentPath);
        Assert.Empty(ListedNames(peer));
        Assert.True(peer.CreateEmptyRpf("replacement"));
        Settle(tabs);
        Assert.Equal(["replacement.rpf"], ListedNames(first));
    });

    [AvaloniaFact]
    public void SharedArchiveCreationImportAndDeletion() => OnUiThread(() =>
    {
        var (tabs, first, peer, path) = SharedArchive();
        var contentChanges = new List<object>();
        first.ContentChanged += (_, target) => contentChanges.Add(target);

        Assert.True(first.CreateEmptyRpf("new"));
        Settle(tabs);
        Assert.Contains("new.rpf", ListedNames(peer));
        Assert.Single(contentChanges);

        string source = Write(Path.Combine(Work, "import.txt"), "imported");
        Assert.True(peer.ImportDroppedFiles([source]));
        Settle(tabs);
        Assert.Contains("import.txt", ListedNames(first));

        first.SelectEntry(RowFor(first, "new.rpf"));
        Assert.True(first.DeleteSelectedFiles());
        Settle(tabs);
        Assert.DoesNotContain("new.rpf", ListedNames(peer));
        Assert.Equal(Bytes("untouched"), ReadSavedEntry(path, "sibling.txt"));
        Assert.Equal(Bytes("imported"), ReadSavedEntry(path, "import.txt"));
    });

    [AvaloniaFact]
    public void FailedEntryWriteRestoresPeers() => OnUiThread(() =>
    {
        var (tabs, first, peer, path) = SharedArchive();
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.True(first.CreateEmptyRpf("failed"));
            Assert.True(peer.EntryOperationBusy);
            Settle(tabs);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Assert.StartsWith("Could not create failed.rpf: ", first.Status);
        Assert.False(peer.EntryOperationBusy);
        Assert.False(first.EntryOperationBusy);
        Assert.Equal(["sibling.txt", "textures.ytd"], ListedNames(peer).Order());

        Assert.True(first.CreateEmptyRpf("retry"));
        Settle(tabs);
        Assert.Contains("retry.rpf", ListedNames(peer));
    });

    [AvaloniaFact]
    public void InFlightWriteBlocksCompetingWritesAndReopening() => OnUiThread(() =>
    {
        MakeGame(executable: "GTA5_Enhanced.exe");
        string archive = SaveArchive(Path.Combine(Work, "shared.rpf"), ("file.txt", "x"));
        Write(Path.Combine(Work, "delete.txt"), "delete");
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.ActiveBridge;
        first.OpenGame(Work);
        tabs.NewTab();
        ExplorerViewModel peer = tabs.ActiveBridge;
        peer.OpenGame(Work);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TrashHandler = path =>
        {
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            File.Delete(path);
            return true;
        };

        first.SelectEntry(RowFor(first, "delete.txt"));
        Assert.True(first.DeleteSelectedFiles());
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            Assert.True(first.EntryOperationBusy);
            Assert.True(peer.EntryOperationBusy);
            Assert.False(peer.CreateEmptyRpf("blocked"));
            Assert.False(first.DeleteSelectedFiles());
            Assert.False(peer.OpenGameAsync(Work));
            peer.OpenArchive(archive);
            Assert.False(peer.HasArchive);
            peer.Navigate("anything");
            Assert.Equal(".", peer.CurrentPath);
        }
        finally
        {
            release.Set();
        }
        Settle(tabs);

        Assert.False(first.EntryOperationBusy);
        Assert.False(peer.EntryOperationBusy);
        Assert.DoesNotContain("delete.txt", ListedNames(peer));
        Assert.False(File.Exists(Path.Combine(Work, "blocked.rpf")));
    });

}
