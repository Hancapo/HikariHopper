using HikariHopper.Core;
using HikariHopper.ViewModels;
using RedFury.Resources;
using RedFury.Rpf;
using Hash32 = CommonFury.Hashing.Hash32;

namespace HikariHopper.Tests;

/// <summary>What stays GTA V's so far, kept from Red Dead Redemption 2: changing its archives.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2GatesTests : ExplorerTestBase
{
    // An RPF8 holding a drawable, a collision and a binary file, opened on its own in a tab.
    private ExplorerViewModel OpenRpf8()
    {
        var archive = new RpfBuilder();
        archive.Files.Add(RpfFile.FromBytes(new Hash32(0x12345678), "hello"u8));
        archive.Files.Add(RpfFile.FromResource(new Hash32(0x00C0FFEE), new Rsc8Resource(new Rsc8Header(0xA2, 0, 255, 0, 0x1A, 0x12), new byte[32]), 2));
        archive.Files.Add(RpfFile.FromResource(new Hash32(0x00B0B0B0), new Rsc8Resource(new Rsc8Header(0xA2, 0, 255, 0, 0x1A, 0x12), new byte[32]), 6));
        string path = Path.Combine(Work, "x.rpf");
        File.WriteAllBytes(path, archive.Write());
        var tab = new ExplorerViewModel();
        tab.OpenArchive(path);
        Assert.True(tab.Provider.InArchive);
        return tab;
    }

    private static int RowOf(ExplorerViewModel tab, string name) =>
        tab.EntriesModel.Select((item, row) => (item, row)).First(pair => pair.item.Record.Name == name).row;

    [Fact]
    public void An_RDR2_archive_can_be_read_but_not_changed()
    {
        ExplorerViewModel tab = OpenRpf8();
        Assert.False(tab.Provider.ArchiveWritable);
        Assert.False(tab.CanCreateHere);
        Clipboard.Files.Add(Path.Combine(Work, "x.rpf"));
        Assert.False(tab.CanPaste);
        tab.SelectEntry(RowOf(tab, "0x12345678"));
        Assert.False(tab.CanRenameSelection);
        Assert.False(tab.SelectionDeletable);
        // Anything that would change it says why, rather than failing in FiveFury.
        var error = Assert.Throws<ArgumentException>(() => tab.Provider.CreationTarget());
        Assert.Equal(RpfProvider.ReadOnlyArchive, error.Message);
        Assert.Equal(RpfProvider.ReadOnlyArchive,
            Assert.Throws<ArgumentException>(() => tab.Provider.GetArchiveEntryTarget(tab.Provider.Entries().First())).Message);
        // Its files still read.
        Assert.Equal("hello"u8.ToArray(), tab.Provider.ReadEntryBytes(tab.Provider.Entries().First(entry => entry.Name == "0x12345678")));
    }

    [Fact]
    public void Its_drawables_and_collisions_open_in_the_3D_viewer()
    {
        ExplorerViewModel tab = OpenRpf8();
        var opened = new List<ModelFile>();
        tab.ModelFileOpened += (_, file) => opened.Add(file);
        foreach (string name in new[] { "0x00C0FFEE.ydr", "0x00B0B0B0.ybn" })
        {
            int row = RowOf(tab, name);
            tab.SelectEntry(row);
            Assert.True(tab.CanOpenSelection);
            tab.ActivateEntry(row);
        }
        Assert.Equal(["0x00C0FFEE.ydr", "0x00B0B0B0.ybn"], opened.Select(file => file.Name));
        // Read only: nothing to save them back to.
        Assert.All(opened, file => Assert.Null(file.SaveTarget));
    }

    [Fact]
    public void A_loose_RDR2_resource_shows_its_format_and_version()
    {
        string root = Directory.CreateDirectory(Path.Combine(Work, "Red Dead Redemption 2")).FullName;
        File.WriteAllBytes(Path.Combine(root, "RDR2.exe"), []);
        byte[] file = new Rsc8Resource(new Rsc8Header(0xA2, 0, 255, 0, 0x1A, 0x12), new byte[32]).Write();
        File.WriteAllBytes(Path.Combine(root, "bench.ydr"), file);
        var provider = new RpfProvider();
        provider.ApplyLoadedGame(new LoadedGame(root, GameSettings.Rdr2Edition, Rdr2Keys.Bundled));
        EntryRecord bench = provider.Entries().First(entry => entry.Name == "bench.ydr");
        PropertySection resource = provider.DescribeEntry(bench).Sections.First(section => section.Heading == "RESOURCE");
        Assert.Contains(("Format", "RSC8 · version 162"), resource.Rows);
        Assert.Contains(("Game", "RDR2"), resource.Rows);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_text_file_from_the_installed_games_archives_opens_with_nothing_to_save_to()
    {
        string game = TestGames.Rdr2();
        ExplorerViewModel.GameLoader = path => RpfProvider.LoadGame(path);
        var tab = new ExplorerViewModel();
        TextFile? opened = null;
        tab.TextFileOpened += (_, file) => opened = file;
        tab.OpenGame(game);
        tab.GoToAddress("common_0.rpf/data");
        Assert.True(tab.Provider.InArchive);
        EntryItem text = tab.EntriesModel.First(item => !item.Record.IsDirectory && TextDocument.IsTextName(item.Record.Name)
            && item.Record.Size is > 0 and < 200_000);
        tab.ActivateEntry(tab.EntriesModel.IndexOf(text));
        Settle(tab);
        Assert.NotNull(opened);
        Assert.Null(opened.SaveTarget);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_texture_dictionary_from_the_installed_games_archives_opens_with_save_as_only()
    {
        string game = TestGames.Rdr2();
        ExplorerViewModel.GameLoader = path => RpfProvider.LoadGame(path);
        var tab = new ExplorerViewModel();
        tab.OpenGame(game);
        tab.GoToAddress("textures_0.rpf");
        Assert.True(tab.Provider.InArchive);
        EntryItem dictionary = tab.EntriesModel.First(item => !item.Record.IsDirectory && item.Record.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase));
        tab.ActivateEntry(tab.EntriesModel.IndexOf(dictionary));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while ((tab.TextureViewer.Loading || tab.TextureViewer.TextureCount == 0) && clock.ElapsedMilliseconds < 15_000)
            TestWait.Pump(20);
        Assert.DoesNotContain("Could not", tab.Status);
        Assert.Equal(dictionary.Record.Name, tab.TextureViewer.SourceName);
        Assert.True(tab.TextureViewer.TextureCount > 0);
        // Nothing to save back into: Save As only.
        Assert.False(tab.TextureViewer.CanSave);
        Assert.True(tab.TextureViewer.CanSaveAs);
    }
}
