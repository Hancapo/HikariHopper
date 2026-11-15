using HikariHopper.Core;
using HikariHopper.ViewModels;
using RedFury.Resources;
using RedFury.Rpf;
using Hash32 = CommonFury.Hashing.Hash32;

namespace HikariHopper.Tests;

/// <summary>What stays GTA V's so far, kept from Red Dead Redemption 2: changing its archives, and the viewers only GTA V files open in.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2GatesTests : ExplorerTestBase
{
    // An RPF8 holding a drawable and a binary file, opened on its own in a tab.
    private ExplorerViewModel OpenRpf8()
    {
        var archive = new RpfBuilder();
        archive.Files.Add(RpfFile.FromBytes(new Hash32(0x12345678), "hello"u8));
        archive.Files.Add(RpfFile.FromResource(new Hash32(0x00C0FFEE), new Rsc8Resource(new Rsc8Header(0xA2, 0, 255, 0, 0x1A, 0x12), new byte[32]), 2));
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
    public void Its_textures_and_models_say_they_dont_open_yet()
    {
        ExplorerViewModel tab = OpenRpf8();
        ModelFile? opened = null;
        tab.ModelFileOpened += (_, file) => opened = file;
        int row = RowOf(tab, "0x00C0FFEE.ydr");
        tab.SelectEntry(row);
        Assert.False(tab.CanOpenSelection);
        tab.ActivateEntry(row);
        Assert.Null(opened);
        Assert.Equal("0x00C0FFEE.ydr: Red Dead Redemption 2's models don't open in HikariHopper yet", tab.Status);
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
}
