using System.IO.Compression;
using FiveFury;
using FiveFury.Rpf;
using FiveFury.Ytd;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Ports of <c>tests/test_rpf_creation.py</c>, <c>tests/test_ytd_creation.py</c> and <c>tests/test_file_import.py</c>.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class EntryCreationTests : ExplorerTestBase
{
    /// <summary>A game tab showing an empty game folder (the executable is removed after loading).</summary>
    private ExplorerViewModel OpenBareGame(string executable = "GTA5_Enhanced.exe")
    {
        MakeGame(executable: executable);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        File.Delete(Path.Combine(Work, executable));
        bridge.Refresh();
        return bridge;
    }

    [AvaloniaFact]
    public void CreatesFoldersAndRpfsInLooseGameDirectory()
    {
        string sourceFolder = Directory.CreateDirectory(Path.Combine(Work, "source")).FullName;
        Write(Path.Combine(sourceFolder, "from-folder.txt"), "folder");
        string sourceZip = Path.Combine(Work, "source.zip");
        using (ZipArchive zip = ZipFile.Open(sourceZip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("from-zip.txt").Open()))
            writer.Write("zip");
        string destination = Directory.CreateDirectory(Path.Combine(Work, "game")).FullName;
        var target = new EntryCreationTarget(Directory: destination);

        Assert.Equal("stream", EntryOperations.CreateFolderAt(target, "stream"));
        Assert.Equal("empty.rpf", EntryOperations.CreateEmptyRpfAt(target, "empty"));
        Assert.Equal("folder_source.rpf", EntryOperations.CreateRpfFromFolderAt(target, "folder_source", sourceFolder));
        Assert.Equal("zip_source.rpf", EntryOperations.CreateRpfFromZipAt(target, "zip_source.rpf", sourceZip));

        Assert.True(Directory.Exists(Path.Combine(destination, "stream")));
        Assert.Empty(ReadArchive(Path.Combine(destination, "empty.rpf")).Root.Children);
        Assert.NotNull(ReadArchive(Path.Combine(destination, "folder_source.rpf")).Find("from-folder.txt"));
        Assert.NotNull(ReadArchive(Path.Combine(destination, "zip_source.rpf")).Find("from-zip.txt"));
    }

    [AvaloniaFact]
    public void CreatesEntriesInsideAnOpenRpf()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("mods", null!));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        EntryCreationTarget target = provider.CreationTarget("mods");

        Assert.Equal("stream", EntryOperations.CreateFolderAt(target, "stream"));
        Assert.Equal("inner.rpf", EntryOperations.CreateEmptyRpfAt(target, "inner.rpf"));
        var error = Assert.Throws<EntryExistsException>(() => EntryOperations.CreateEmptyRpfAt(target, "INNER.RPF"));
        Assert.Contains("this name already exists", error.Message);

        Assert.NotNull(ReadArchive(archive).Find("mods/stream"));
        byte[] nested = ReadSavedEntry(archive, "mods/inner.rpf")!;
        Assert.Empty(RpfArchive.Read(nested, "inner.rpf").Root.Children);
        provider.Close();
    }

    [AvaloniaFact]
    public void CreatesRpfInsideANestedArchive()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("inner.rpf", new RpfArchive("inner.rpf")));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        provider.OpenNested(provider.ArchiveEntries(".")[0]);

        Assert.Equal("deepest.rpf", EntryOperations.CreateEmptyRpfAt(provider.CreationTarget("."), "deepest.rpf"));

        Assert.Empty(RpfArchive.Read(ReadSavedEntry(archive, "inner.rpf/deepest.rpf")!, "deepest.rpf").Root.Children);
        provider.Close();
    }

    [AvaloniaFact]
    public void CreationRejectsInvalidAndDuplicateNames()
    {
        var target = new EntryCreationTarget(Directory: Work);
        EntryOperations.CreateFolderAt(target, "existing");

        Assert.Contains("already exists", Assert.Throws<EntryExistsException>(() => EntryOperations.CreateFolderAt(target, "EXISTING")).Message);
        Assert.Contains("cannot contain", Assert.Throws<ArgumentException>(() => EntryOperations.CreateEmptyRpfAt(target, "bad/name")).Message);
        Assert.Contains("reserved by Windows", Assert.Throws<ArgumentException>(() => EntryOperations.CreateFolderAt(target, "CON")).Message);
        File.WriteAllText(Path.Combine(Work, "existing.rpf"), "existing");
        Assert.Contains("this name already exists",
            Assert.Throws<EntryExistsException>(() => EntryOperations.CreateEmptyRpfAt(target, "EXISTING")).Message);
    }

    [AvaloniaTheory]
    [InlineData("", "Enter a name")]
    [InlineData("..", "reserved")]
    [InlineData("bad:name", "cannot contain")]
    [InlineData("name.", "cannot end")]
    [InlineData("NUL.txt", "reserved by Windows")]
    public void EntryNameValidationReportsSpecificErrors(string name, string message) =>
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => EntryNames.NormalizeEntryName(name)).Message);

    [AvaloniaFact]
    public void SuffixedNamesAccountForTheExtension()
    {
        Assert.Contains("cannot exceed 255", Assert.Throws<ArgumentException>(() => EntryNames.NormalizeEntryName(new string('a', 256))).Message);
        Assert.Contains("before .rpf", Assert.Throws<ArgumentException>(() => EntryNames.NormalizeRpfName(".rpf")).Message);
        Assert.Contains("including .rpf", Assert.Throws<ArgumentException>(() => EntryNames.NormalizeRpfName(new string('a', 252))).Message);
        Assert.Equal("textures.ytd", EntryNames.NormalizeYtdName("textures"));
        Assert.Contains("before .ytd", Assert.Throws<ArgumentException>(() => EntryNames.NormalizeYtdName(".ytd")).Message);
    }

    [AvaloniaFact]
    public void RejectsDuplicateRpfBeforeStartingWorker()
    {
        ExplorerViewModel bridge = OpenBareGame();
        File.WriteAllText(Path.Combine(Work, "existing.rpf"), "existing");
        bridge.Refresh();

        Assert.Equal("An entry with this name already exists", bridge.CreationNameError("EXISTING", "rpf"));
        Assert.Equal("", bridge.CreationNameError("fresh", "rpf"));
        Assert.Equal("Enter a name", bridge.CreationNameError("  ", "entry"));
        Assert.False(bridge.CreateEmptyRpf("EXISTING"));
        Assert.False(bridge.EntryOperationBusy);
        Assert.Equal("An entry with this name already exists", bridge.Status);
    }

    [AvaloniaFact]
    public void CreatesEntriesWithoutBlockingTheUiThread()
    {
        ExplorerViewModel bridge = OpenBareGame();
        var busyChanges = new List<bool>();
        bridge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExplorerViewModel.EntryOperationBusy))
                busyChanges.Add(bridge.EntryOperationBusy);
        };

        Assert.True(bridge.CreateEmptyRpf("background"));
        Assert.True(bridge.EntryOperationBusy);
        Assert.Equal("Creating background.rpf…", bridge.Status);
        Assert.False(bridge.CreateFolder("second"));
        Settle(bridge);

        Assert.False(bridge.EntryOperationBusy);
        Assert.Equal([true, false], busyChanges);
        Assert.True(File.Exists(Path.Combine(Work, "background.rpf")));
        Assert.Equal(["background.rpf"], ListedNames(bridge));
        Assert.True(bridge.EntriesModel[0].Selected);
        Assert.Equal("background.rpf", bridge.SelectedName);
        Assert.Equal("Created background.rpf", bridge.Status);
    }

    [AvaloniaFact]
    public void CreatesFoldersAndPackedRpfsFromTheExplorer()
    {
        string source = Directory.CreateDirectory(Path.Combine(Work, "..", "pack")).FullName;
        Write(Path.Combine(source, "inside.txt"), "inside");
        string zip = Path.Combine(Path.GetDirectoryName(source)!, "pack.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("zipped.txt");
        ExplorerViewModel bridge = OpenBareGame();

        Assert.True(bridge.CreateFolder("stream"));
        Settle(bridge);
        Assert.True(bridge.CreateRpfFromFolder("packed", source));
        Settle(bridge);
        Assert.True(bridge.CreateRpfFromZip("zipped.rpf", zip));
        Settle(bridge);

        Assert.Equal(["stream", "packed.rpf", "zipped.rpf"], ListedNames(bridge));
        Assert.NotNull(ReadArchive(Path.Combine(Work, "packed.rpf")).Find("inside.txt"));
        Assert.NotNull(ReadArchive(Path.Combine(Work, "zipped.rpf")).Find("zipped.txt"));
    }

    [AvaloniaFact]
    public void CreatesInsideTheOpenArchiveAndReloadsIt()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("mods/keep.txt", "keep"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(archive);
        bridge.Navigate("mods");
        var written = new List<string>();
        bridge.ArchiveWriteRequested += (_, path) => written.Add(path);

        Assert.True(bridge.CreateFolder("stream"));
        Settle(bridge);

        Assert.Equal([archive], written);
        Assert.Equal(["stream", "keep.txt"], ListedNames(bridge));
        Assert.Equal("stream", bridge.SelectedName);
        Assert.NotNull(ReadArchive(archive).Find("mods/stream"));
    }

    [AvaloniaFact]
    public void FailedWriteReportsTheErrorAndEndsTheOperation()
    {
        ExplorerViewModel bridge = OpenBareGame();
        string source = Path.Combine(Work, "..", "missing-folder");
        Assert.True(bridge.CreateRpfFromFolder("packed", source));
        Settle(bridge);
        Assert.StartsWith("Could not create packed.rpf: RPF source folder does not exist:", bridge.Status);
        Assert.False(bridge.EntryOperationBusy);
    }

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public void CreatesValidYtdForEachEdition(GameTarget game)
    {
        var target = new EntryCreationTarget(Directory: Work, Game: game);
        Assert.Equal("textures.ytd", EntryOperations.CreateYtdAt(target, "textures"));
        Ytd created = Ytd.Read(File.ReadAllBytes(Path.Combine(Work, "textures.ytd")));
        Assert.Empty(created.Textures);
        Assert.Equal(game, created.Target);
    }

    [AvaloniaFact]
    public void CreatesYtdInsideAnRpfWithTheActiveGameTarget()
    {
        MakeGame(executable: "GTA5_Enhanced.exe");
        string archive = SaveArchive(Path.Combine(Work, "assets.rpf"), ("textures", null!));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.OpenArchive(archive);
        bridge.Navigate("textures");

        Assert.True(bridge.CreateYtd("created.ytd"));
        Settle(bridge);

        Assert.Equal("Created created.ytd", bridge.Status);
        Ytd created = Ytd.Read(ReadSavedEntry(archive, "textures/created.ytd")!);
        Assert.Empty(created.Textures);
        Assert.Equal(GameTarget.Enhanced, created.Target);
    }

    [AvaloniaFact]
    public void YtdCreationUsesNameValidationAndRunsInBackground()
    {
        ExplorerViewModel bridge = OpenBareGame("GTA5.exe");

        Assert.True(bridge.CreateYtd("textures"));
        Assert.True(bridge.EntryOperationBusy);
        Settle(bridge);

        Assert.False(bridge.EntryOperationBusy);
        EntryItem entry = Assert.Single(bridge.EntriesModel);
        Assert.Equal("textures.ytd", entry.Name);
        Assert.Equal("Texture Dictionary", entry.Kind);
        Assert.Equal("An entry with this name already exists", bridge.CreationNameError("textures", "ytd"));
    }

    [AvaloniaFact]
    public void StandaloneRpfWithoutGameContextDoesNotGuessYtdEdition()
    {
        string archive = SaveArchive(Path.Combine(Work, "standalone.rpf"));
        byte[] original = File.ReadAllBytes(archive);
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(archive);

        Assert.True(bridge.CreateYtd("unknown"));
        Settle(bridge);

        Assert.Contains("configured game", bridge.Status);
        Assert.StartsWith("Could not create unknown.ytd: ", bridge.Status);
        Assert.Equal(original, File.ReadAllBytes(archive));
    }

    [AvaloniaFact]
    public void ImportsFilesIntoALooseDirectoryWithoutOverwriting()
    {
        string first = Write(Path.Combine(Work, "source", "first.txt"), "first");
        string second = Write(Path.Combine(Work, "source", "second.bin"), "second");
        string destination = Directory.CreateDirectory(Path.Combine(Work, "game")).FullName;
        var target = new EntryCreationTarget(Directory: destination);

        Assert.Equal(["first.txt", "second.bin"], EntryOperations.ImportFilesAt(target, [first, second]));
        Assert.Equal("first", File.ReadAllText(Path.Combine(destination, "first.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(destination, "second.bin")));

        Write(first, "changed");
        Assert.Contains("already exists", Assert.Throws<EntryExistsException>(() => EntryOperations.ImportFilesAt(target, [first])).Message);
        Assert.Equal("first", File.ReadAllText(Path.Combine(destination, "first.txt")));
    }

    [AvaloniaFact]
    public void A_drop_whose_names_are_there_replaces_skips_or_keeps_both_in_a_loose_folder()
    {
        string destination = Directory.CreateDirectory(Path.Combine(Work, "game")).FullName;
        Write(Path.Combine(destination, "first.txt"), "old");
        Write(Path.Combine(destination, "mods", "kept.txt"), "kept");
        Write(Path.Combine(destination, "mods", "swap.txt"), "old swap");
        string first = Write(Path.Combine(Work, "source", "first.txt"), "new");
        string fresh = Write(Path.Combine(Work, "source", "fresh.txt"), "fresh");
        string mods = Path.GetDirectoryName(Write(Path.Combine(Work, "source", "mods", "swap.txt"), "new swap"))!;
        var target = new EntryCreationTarget(Directory: destination);

        // Skip: only what's new goes in.
        Assert.Equal(["fresh.txt"], EntryOperations.ImportFilesAt(target, [first, fresh], ImportConflict.Skip));
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "first.txt")));

        // Keep both: the dropped one beside it, numbered.
        Assert.Equal(["first (2).txt"], EntryOperations.ImportFilesAt(target, [first], ImportConflict.KeepBoth));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "first (2).txt")));

        // Replace: the file is replaced, and a folder merges, its files of the same name replaced and the rest kept.
        Assert.Equal(["first.txt", "mods"], EntryOperations.ImportFilesAt(target, [first, mods], ImportConflict.Replace));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "first.txt")));
        Assert.Equal("new swap", File.ReadAllText(Path.Combine(destination, "mods", "swap.txt")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(destination, "mods", "kept.txt")));
        Assert.Empty(Directory.EnumerateFiles(destination, "*.hikari-replace", SearchOption.AllDirectories));

        // A file can't replace a folder.
        string clash = Write(Path.Combine(Work, "other", "mods"), "a file");
        Assert.Throws<ArgumentException>(() => EntryOperations.ImportFilesAt(target, [clash], ImportConflict.Replace));
    }

    [AvaloniaFact]
    public void A_drop_whose_names_are_there_replaces_or_skips_them_in_an_rpf()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("existing.txt", "original"), ("other.txt", "other"));
        string existing = Write(Path.Combine(Work, "drop", "existing.txt"), "replacement");
        string fresh = Write(Path.Combine(Work, "drop", "fresh.txt"), "fresh");
        var provider = new RpfProvider();
        provider.OpenArchive(archive);

        Assert.Equal(["fresh.txt"], EntryOperations.ImportFilesAt(provider.CreationTarget("."), [existing, fresh], ImportConflict.Skip));
        Assert.Equal(Bytes("original"), ReadSavedEntry(archive, "existing.txt"));
        Assert.Equal(["existing.txt"], EntryOperations.ImportFilesAt(provider.CreationTarget("."), [existing], ImportConflict.Replace));
        Assert.Equal(Bytes("replacement"), ReadSavedEntry(archive, "existing.txt"));
        Assert.Equal(Bytes("other"), ReadSavedEntry(archive, "other.txt"));
        provider.Close();
    }

    [AvaloniaFact]
    public void A_drop_with_names_already_there_asks_before_importing_anything()
    {
        Write(Path.Combine(Work, "first.txt"), "old");
        ExplorerViewModel bridge = OpenBareGame();
        string first = Write(Path.Combine(Work, "..", "drops", "first.txt"), "new");
        string second = Write(Path.Combine(Work, "..", "drops", "second.txt"), "second");
        ImportConflicts? asked = null;
        bridge.ImportConflictsFound += (_, pending) => asked = pending;

        // Nothing starts: the names already here are asked about.
        Assert.True(bridge.ImportDroppedFiles([first, second]));
        Assert.False(bridge.EntryOperationBusy);
        Assert.NotNull(asked);
        Assert.Equal(["first.txt"], asked.Names);
        Assert.Equal(2, asked.Sources.Count);
        Assert.False(File.Exists(Path.Combine(Work, "second.txt")));

        // Replace: both go in, the old one replaced.
        Assert.True(bridge.ResolveImport(asked, ImportConflict.Replace));
        Settle(bridge);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Work, "first.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(Work, "second.txt")));
        Assert.Equal("Imported 2 files", bridge.Status);

        // Skipping everything says so.
        asked = null;
        Assert.True(bridge.ImportDroppedFiles([first]));
        Assert.True(bridge.ResolveImport(asked!, ImportConflict.Skip));
        Settle(bridge);
        Assert.Equal("Nothing imported: every file was already here", bridge.Status);
    }

    [AvaloniaFact]
    public void ImportsFilesAndNestedRpfsIntoAnRpfFolder()
    {
        string plain = Write(Path.Combine(Work, "source", "plain.txt"), "plain");
        string nested = SaveArchive(Path.Combine(Work, "source", "nested.rpf"));
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("mods", null!));
        var provider = new RpfProvider();
        provider.OpenArchive(archive);

        Assert.Equal(["plain.txt", "nested.rpf"], EntryOperations.ImportFilesAt(provider.CreationTarget("mods"), [plain, nested]));

        Assert.Equal(Bytes("plain"), ReadSavedEntry(archive, "mods/plain.txt"));
        Assert.Empty(RpfArchive.Read(ReadSavedEntry(archive, "mods/nested.rpf")!, "nested.rpf").Root.Children);
        provider.Close();
    }

    [AvaloniaFact]
    public void RpfImportRejectsNameCollisionsBeforeWriting()
    {
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("existing.txt", "original"));
        string source = Write(Path.Combine(Work, "existing.txt"), "replacement");
        var provider = new RpfProvider();
        provider.OpenArchive(archive);
        byte[] before = File.ReadAllBytes(archive);

        Assert.Contains("already exists",
            Assert.Throws<EntryExistsException>(() => EntryOperations.ImportFilesAt(provider.CreationTarget("."), [source])).Message);

        Assert.Equal(before, File.ReadAllBytes(archive));
        provider.Close();
    }

    [AvaloniaFact]
    public void ImportsDroppedFilesInBackground()
    {
        string first = Write(Path.Combine(Work, "..", "drops", "first.txt"), "first");
        string second = new Uri(Write(Path.Combine(Work, "..", "drops", "second.txt"), "second")).AbsoluteUri;
        ExplorerViewModel bridge = OpenBareGame();

        Assert.True(bridge.ImportDroppedFiles([first, second]));
        Assert.True(bridge.EntryOperationBusy);
        Assert.Equal("Importing 2 files…", bridge.Status);
        Settle(bridge);

        Assert.False(bridge.EntryOperationBusy);
        Assert.Equal("first", File.ReadAllText(Path.Combine(Work, "first.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(Work, "second.txt")));
        Assert.Equal(2, bridge.SelectionCount);
        Assert.Equal("Imported 2 files", bridge.Status);
    }

    [AvaloniaFact]
    public void RejectsNonLocalDrops()
    {
        // A dropped folder is imported now (CopyPasteTests); what isn't a local file or folder still isn't.
        ExplorerViewModel bridge = OpenBareGame();

        Assert.False(bridge.ImportDroppedFiles([null]));
        Assert.Equal("Could not import files: Only local files can be imported", bridge.Status);
        Assert.False(bridge.ImportDroppedFiles(["https://example.com/file.txt"]));
        Assert.Equal("Could not import files: Only local files can be imported", bridge.Status);
        Assert.False(bridge.ImportDroppedFiles([]));
        Assert.Equal("Could not import files: Drop one or more files to import", bridge.Status);
        Assert.False(bridge.EntryOperationBusy);
    }

    [AvaloniaFact]
    public void LocalDropPathsResolvePathsAndFileUris()
    {
        string file = Write(Path.Combine(Work, "with space.txt"), "x");
        Assert.Equal([file, file], FileDrops.LocalDropPaths([file, new Uri(file).AbsoluteUri]));
        Assert.Throws<FileNotFoundException>(() => FileDrops.LocalDropPaths([Path.Combine(Work, "missing.txt")]));
    }
}
