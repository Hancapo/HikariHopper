using HikariHopper.Core;
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
    public void DeletingNestedRpfReturnsPeerToRoot()
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
    }

    [AvaloniaFact]
    public void SharedArchiveCreationImportAndDeletion()
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
    }

    [AvaloniaFact]
    public void FailedEntryWriteRestoresPeers()
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
    }

    [AvaloniaFact]
    public void InFlightWriteBlocksCompetingWritesAndReopening()
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
    }

    /// <summary>
    /// The game's texture tables made in the background, holding <paramref name="archive"/> open as making them does
    /// until <paramref name="release"/> is set; <paramref name="reading"/> is set once it's open.
    /// </summary>
    private Task TablesReading(string archive, ManualResetEventSlim reading, ManualResetEventSlim release) =>
        GameTextures.Share(Work, () =>
        {
            using var held = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
            reading.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return new GameTextures.Tables();
        }).Load();

    [Fact]
    public void AnArchiveTheGamesTablesAreReadFromIsRewrittenOnceTheyAreMade()
    {
        string archive = SaveArchive(Path.Combine(Work, "mods", "mod.rpf"), ("data/handling.meta", "<Item />"));
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task tables = TablesReading(archive, reading, release);
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)));

        // Windows can't replace the archive while the tables hold it, so the write waits for them rather than fail.
        Task write = Task.Run(() => ArchiveWriter.ReplaceEntry(archive, null, "data/handling.meta", "<Item edited=\"1\" />"u8.ToArray()));
        Assert.False(write.Wait(TimeSpan.FromMilliseconds(300)));
        release.Set();
        write.Wait(TimeSpan.FromSeconds(10));
        Assert.True(tables.IsCompletedSuccessfully);
        Assert.Equal("<Item edited=\"1\" />", System.Text.Encoding.UTF8.GetString(ReadSavedEntry(archive, "data/handling.meta")!));
    }

    [Fact]
    public void AnArchiveTheGamesTablesAreReadFromIsRenamedOnceTheyAreMade()
    {
        string archive = SaveArchive(Path.Combine(Work, "mods", "mod.rpf"), ("data/handling.meta", "<Item />"));
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TablesReading(archive, reading, release);
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)));

        var folder = new EntryCreationTarget(Directory: Path.Combine(Work, "mods"));
        Task rename = Task.Run(() => EntryOperations.RenameAt(folder, "mod.rpf", "renamed.rpf"));
        Assert.False(rename.Wait(TimeSpan.FromMilliseconds(300)));
        release.Set();
        rename.Wait(TimeSpan.FromSeconds(10));
        Assert.True(File.Exists(Path.Combine(Work, "mods", "renamed.rpf")));
    }

    [Fact]
    public void ALooseFileIsntHeldUpByTheGamesTables()
    {
        string archive = SaveArchive(Path.Combine(Work, "mods", "mod.rpf"), ("data/handling.meta", "<Item />"));
        Write(Path.Combine(Work, "mods", "readme.txt"), "notes");
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TablesReading(archive, reading, release);
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            EntryOperations.RenameAt(new EntryCreationTarget(Directory: Path.Combine(Work, "mods")), "readme.txt", "notes.txt");
            Assert.True(File.Exists(Path.Combine(Work, "mods", "notes.txt")));
        }
        finally
        {
            release.Set();
        }
    }
}
