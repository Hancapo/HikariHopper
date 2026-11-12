using System.Diagnostics;
using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Port of <c>tests/test_file_deletion.py</c>.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class EntryDeletionTests : ExplorerTestBase
{
    private RpfProvider GameProvider()
    {
        MakeGame(executable: "GTA5_Enhanced.exe");
        var provider = new RpfProvider();
        provider.ApplyLoadedGame(FakeLoadedGame(Work));
        return provider;
    }

    /// <summary>A tab browsing <see cref="ExplorerTestBase.Work"/> as an Enhanced game.</summary>
    private ExplorerViewModel GameTab(ExplorerViewModel? bridge = null, bool removeExecutable = true)
    {
        string executable = Path.Combine(Work, "GTA5_Enhanced.exe");
        File.WriteAllBytes(executable, [1]);
        bridge ??= new ExplorerViewModel();
        bridge.OpenGame(Work);
        if (removeExecutable)
        {
            File.Delete(executable);
            bridge.Refresh();
        }
        return bridge;
    }

    [AvaloniaFact]
    public void DeletesMultipleFilesFromRpfWithoutTouchingSiblings()
    {
        string archive = SaveArchive(Path.Combine(Work, "archive.rpf"),
            ("folder/first.txt", "first"), ("folder/second.ydr", "second"),
            ("folder/child.rpf", new RpfArchive("child.rpf")), ("folder/keep.txt", "keep"));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        var entries = provider.ArchiveEntries("folder").Where(entry => entry.Name != "keep.txt").ToList();

        Assert.Equal(3, EntryOperations.DeleteArchiveFilesAt(provider.DeletionTarget(entries, "folder")));

        Assert.False(SavedEntryExists(archive, "folder/first.txt"));
        Assert.False(SavedEntryExists(archive, "folder/second.ydr"));
        Assert.False(SavedEntryExists(archive, "folder/child.rpf"));
        Assert.True(SavedEntryExists(archive, "folder/keep.txt"));
        provider.Close();
    }

    [AvaloniaFact]
    public void DeletesFileFromNestedRpf()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"),
            ("inner.rpf", Archive("inner.rpf", ("delete.txt", "delete"), ("keep.txt", "keep"))));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        provider.OpenNested(provider.ArchiveEntries(".")[0]);
        EntryRecord delete = provider.ArchiveEntries(".").First(entry => entry.Name == "delete.txt");

        Assert.Equal(1, EntryOperations.DeleteArchiveFilesAt(provider.DeletionTarget([delete])));

        Assert.False(SavedEntryExists(archive, "inner.rpf/delete.txt"));
        Assert.True(SavedEntryExists(archive, "inner.rpf/keep.txt"));
        provider.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DeletesFolderAndContentsFromRpf(bool populated, bool nested)
    {
        var items = new List<(string, object)> { ("folder", null!) };
        if (populated)
        {
            items.Add(("folder/child/deep.txt", "content"));
            items.Add(("folder/child.rpf", new RpfArchive("child.rpf")));
        }
        items.Add(("keep.txt", "keep"));
        string path = Path.Combine(Work, "archive.rpf");
        RpfArchive archive = Archive("archive.rpf", items.ToArray());
        (nested ? Archive("archive.rpf", ("inner.rpf", archive)) : archive).Save(path);
        var provider = new RpfProvider();
        provider.OpenArchive(path);
        if (nested)
            provider.OpenNested(provider.ArchiveEntries(".")[0]);

        var folder = provider.ArchiveEntries(".").Where(entry => entry.Name == "folder").ToList();
        Assert.Equal(1, EntryOperations.DeleteArchiveFilesAt(provider.DeletionTarget(folder)));
        provider.Close();

        string prefix = nested ? "inner.rpf/" : "";
        Assert.False(SavedEntryExists(path, prefix + "folder"));
        Assert.Equal(Bytes("keep"), ReadSavedEntry(path, prefix + "keep.txt"));
    }

    [AvaloniaFact]
    public void CannotDeleteGameOrArchiveRoot()
    {
        RpfProvider provider = GameProvider();
        Assert.Contains("root", Assert.Throws<ArgumentException>(() =>
            provider.DeletionTarget([new EntryRecord("root", ".", EntryRecord.FolderKind, 0)])).Message);

        string archive = SaveArchive(Path.Combine(Work, "root.rpf"), ("file.txt", "x"));
        provider.OpenArchive(archive);
        ArchiveItem item = provider.ArchiveEntries(".")[0].ArchiveItem!;
        var root = new EntryRecord("root", ".", EntryRecord.FolderKind, 0, NativeEntry: item with { Entry = item.Node.Archive.Root });
        Assert.Contains("root", Assert.Throws<ArgumentException>(() => provider.DeletionTarget([root])).Message);
        provider.Close();
    }

    [AvaloniaFact]
    public void ListingDeletedArchiveFolderDoesNotRecreateIt()
    {
        string archive = SaveArchive(Path.Combine(Work, "test.rpf"));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        Assert.Empty(provider.ArchiveEntries("missing/child"));
        Assert.Empty(provider.ArchiveEntries("."));
        provider.Close();
    }

    [AvaloniaFact]
    public void DeletionRejectsLinkedAndOutsideFolders()
    {
        RpfProvider provider = GameProvider();
        string actual = Directory.CreateDirectory(Path.Combine(Work, "actual")).FullName;
        string linked = Path.Combine(Work, "linked");
        using (Process junction = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linked}\" \"{actual}\"")
        { CreateNoWindow = true, UseShellExecute = false })!)
            junction.WaitForExit();
        try
        {
            Assert.Contains("linked", Assert.Throws<ArgumentException>(() =>
                provider.DeletionTarget([new EntryRecord("linked", "linked", EntryRecord.FolderKind, 0)])).Message);
        }
        finally
        {
            Directory.Delete(linked);
        }
        Assert.Contains("outside", Assert.Throws<ArgumentException>(() =>
            provider.DeletionTarget([new EntryRecord("outside", "../outside", EntryRecord.FolderKind, 0)])).Message);
        Assert.True(Directory.Exists(actual));
    }

    [AvaloniaFact]
    public void TrashingAnOpenRpfReleasesItsArchiveHandle()
    {
        RpfProvider provider = GameProvider();
        string archive = SaveArchive(Path.Combine(Work, "archive.rpf"));
        provider.OpenArchive(archive);
        provider.ShowGame();
        EntryRecord entry = provider.GameEntries(".").First(item => item.Name == "archive.rpf");

        EntryDeletionTarget target = provider.DeletionTarget([entry]);

        Assert.Equal([archive], target.LoosePaths);
        Assert.False(provider.HasArchive);
    }

    [AvaloniaFact]
    public void ArchivePeerMovesOutOfDeletedFolder()
    {
        string path = SaveArchive(Path.Combine(Work, "test.rpf"), ("folder/deep/test.txt", "test"), ("keep.txt", "keep"));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.ActiveBridge;
        first.OpenArchive(path);
        tabs.NewTab();
        ExplorerViewModel peer = tabs.ActiveBridge;
        peer.OpenArchive(path);
        peer.Navigate("folder/deep");
        tabs.ActivateTab(0);

        first.SelectEntry(0);
        Assert.True(first.DeleteSelectedFiles());
        Assert.Equal("Deleting 1 item from RPF…", first.Status);
        Settle(tabs);

        Assert.Equal("Deleted 1 item from RPF", first.Status);
        Assert.Equal(".", peer.CurrentPath);
        Assert.Equal(["keep.txt"], ListedNames(peer));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecyclesFolderAndReleasesDescendantRpf(bool populated)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Work, "folder")).FullName;
        if (populated)
        {
            SaveArchive(Path.Combine(folder, "child.rpf"));
            Write(Path.Combine(folder, "deep", "keep.txt"), "inside");
        }
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = GameTab(tabs.ActiveBridge, removeExecutable: false);
        tabs.NewTab();
        ExplorerViewModel peer = GameTab(tabs.ActiveBridge, removeExecutable: false);
        if (populated)
            peer.OpenArchive(Path.Combine(folder, "child.rpf"));
        peer.ShowGame();
        peer.Navigate("folder");

        bool peerHeldArchive = true;
        TrashHandler = path =>
        {
            peerHeldArchive = peer.Provider.HasArchive;
            Assert.Equal(folder, path);
            Directory.Move(path, Path.Combine(Trash, "folder"));
            return true;
        };
        tabs.ActivateTab(0);
        first.SelectEntry(RowFor(first, "folder"));
        Assert.True(first.SelectionDeletable);
        int confirmations = 0;
        first.DeleteConfirmationRequested += (_, _) => confirmations++;
        first.RequestDeleteSelection();
        Assert.Equal(1, confirmations);
        Assert.True(first.DeleteSelectedFiles());
        Assert.Equal("Moving 1 item to the Recycle Bin…", first.Status);
        Settle(tabs);

        Assert.False(peerHeldArchive);
        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(Path.Combine(Trash, "folder")));
        if (populated)
            Assert.Equal("inside", File.ReadAllText(Path.Combine(Trash, "folder", "deep", "keep.txt")));
        Assert.Equal(".", peer.CurrentPath);
        Assert.DoesNotContain("folder", ListedNames(first));
    }

    [AvaloniaFact]
    public void OpenRpfDeletedInAnotherTabReturnsToItsFolder()
    {
        string archive = SaveArchive(Path.Combine(Work, "mods", "child.rpf"), ("file.txt", "x"));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = GameTab(tabs.ActiveBridge, removeExecutable: false);
        first.Navigate("mods");
        tabs.NewTab();
        ExplorerViewModel peer = GameTab(tabs.ActiveBridge, removeExecutable: false);
        peer.OpenArchive(archive);
        tabs.ActivateTab(0);

        first.SelectEntry(RowFor(first, "child.rpf"));
        Assert.True(first.DeleteSelectedFiles());

        Assert.False(peer.InArchive);
        Assert.Equal("mods", peer.CurrentPath);
        Assert.Equal("The open RPF is being deleted in another tab", peer.Status);
        Settle(tabs);
        Assert.Empty(ListedNames(peer));
    }

    [AvaloniaFact]
    public void MovesLooseFilesToRecycleBinInBackground()
    {
        ExplorerViewModel bridge = GameTab();
        string source = Write(Path.Combine(Work, "delete.txt"), "delete");
        bridge.Refresh();
        bridge.SelectEntry(0);
        int confirmations = 0;
        bridge.DeleteConfirmationRequested += (_, _) => confirmations++;

        bridge.RequestDeleteSelection();
        Assert.Equal(1, confirmations);
        Assert.True(bridge.DeleteSelectedFiles());
        Assert.True(bridge.EntryOperationBusy);
        Settle(bridge);

        Assert.Equal([source], TrashAttempts);
        Assert.False(File.Exists(source));
        Assert.Empty(bridge.EntriesModel);
        Assert.Equal("Moved 1 item to the Recycle Bin", bridge.Status);
    }

    [AvaloniaFact]
    public void RequestDeleteWithoutSelectionDoesNothing()
    {
        var bridge = new ExplorerViewModel();
        int confirmations = 0;
        bridge.DeleteConfirmationRequested += (_, _) => confirmations++;
        bridge.RequestDeleteSelection();
        Assert.False(bridge.DeleteSelectedFiles());
        Assert.Equal(0, confirmations);
    }

    [AvaloniaFact]
    public void TabsReleaseArchiveHandlesAndRefreshSharedFolder()
    {
        string firstPath = SaveArchive(Path.Combine(Work, "hola", "first.rpf"));
        string secondPath = SaveArchive(Path.Combine(Work, "hola", "second.rpf"));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = GameTab(tabs.ActiveBridge, removeExecutable: false);
        first.Navigate("hola");
        tabs.NewTab();
        ExplorerViewModel second = GameTab(tabs.ActiveBridge, removeExecutable: false);
        second.OpenArchive(firstPath);
        second.ShowGame();
        second.Navigate("hola");
        Assert.True(second.Provider.HasArchive);
        Assert.False(second.InArchive);

        bool secondHeldArchive = false;
        TrashHandler = path =>
        {
            secondHeldArchive |= second.Provider.HasArchive;
            File.Delete(path);
            return true;
        };
        tabs.ActivateTab(0);
        first.SelectAllEntries();
        Assert.True(first.DeleteSelectedFiles());
        Assert.True(second.EntryOperationBusy);
        Settle(tabs);

        Assert.False(secondHeldArchive);
        Assert.Equal([firstPath, secondPath], TrashAttempts.Order());
        Assert.Empty(first.EntriesModel);
        Assert.Empty(second.EntriesModel);
        Assert.False(second.EntryOperationBusy);
    }

    [AvaloniaFact]
    public void StaleMissingSelectionDoesNotBlockRemainingDeletions()
    {
        string missing = Write(Path.Combine(Work, "first.txt"), "first");
        string remaining = Write(Path.Combine(Work, "second.txt"), "second");
        ExplorerViewModel bridge = GameTab();
        bridge.SelectAllEntries();
        File.Delete(missing);

        Assert.True(bridge.DeleteSelectedFiles());
        Settle(bridge);

        Assert.False(File.Exists(remaining));
        Assert.Empty(bridge.EntriesModel);
        Assert.Equal("Moved 1 item to the Recycle Bin", bridge.Status);
    }

    [AvaloniaFact]
    public void MultiDeleteAttemptsEverySelectedFileAfterAFailure()
    {
        string blocked = Write(Path.Combine(Work, "blocked.txt"), "blocked");
        string movable = Write(Path.Combine(Work, "movable.txt"), "movable");
        TrashHandler = path =>
        {
            if (path == blocked)
                return false;
            File.Delete(path);
            return true;
        };
        ExplorerViewModel bridge = GameTab();
        bridge.SelectAllEntries();

        Assert.True(bridge.DeleteSelectedFiles());
        Settle(bridge);

        Assert.Equal(["blocked.txt", "movable.txt"], TrashAttempts.Select(Path.GetFileName));
        Assert.True(File.Exists(blocked));
        Assert.False(File.Exists(movable));
        Assert.Single(bridge.EntriesModel);
        Assert.Equal("Could not delete selected items: Could not move to the Recycle Bin: blocked.txt", bridge.Status);
        Assert.False(bridge.EntryOperationBusy);
    }
}
