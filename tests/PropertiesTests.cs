using Avalonia.Headless;
using Avalonia.Input;
using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.Tests.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>The Properties sheet: where an item is, its size and, for a resource or an RPF, what is in it.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class PropertiesTests : ExplorerTestBase
{
    private static int Row(ExplorerViewModel bridge, string name) =>
        Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == name);

    private static EntryProperties Read(ExplorerViewModel bridge, string name)
    {
        bridge.SelectEntry(Row(bridge, name));
        Task<EntryProperties?> reading = bridge.ReadSelectionProperties();
        Settle(bridge);
        Assert.True(reading.IsCompleted);
        return Assert.IsType<EntryProperties>(reading.Result);
    }

    private static Dictionary<string, string> Rows(EntryProperties properties, string heading) =>
        properties.Sections.Single(section => section.Heading == heading).Rows.ToDictionary(row => row.Label, row => row.Value);

    private static byte[] TwoTextures() => TextureTestKit.Bytes(TextureTestKit.Dictionary("paint", "decal"));

    [AvaloniaFact]
    public void AYtdInAnArchiveShowsItsPackedSizeFormatAndTextures()
    {
        MakeGame();
        var archive = new RpfArchive("mods.rpf");
        archive.AddResource("textures/cars.ytd", TwoTextures());
        string path = Path.Combine(Work, "mods.rpf");
        archive.Save(path);
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        bridge.Navigate("textures");
        Settle(bridge);

        EntryProperties properties = Read(bridge, "cars.ytd");
        Assert.Equal("cars.ytd", properties.Name);
        Assert.Equal(["LOCATION", "SIZE", "RESOURCE"], properties.Sections.Select(section => section.Heading));
        Assert.Equal("textures/cars.ytd", Rows(properties, "LOCATION")["Path"]);
        Dictionary<string, string> size = Rows(properties, "SIZE");
        Assert.EndsWith("· compressed", size["In the archive"]);
        Assert.True(size.ContainsKey("Unpacked"));
        Dictionary<string, string> resource = Rows(properties, "RESOURCE");
        Assert.StartsWith("RSC7 · version ", resource["Format"]);
        Assert.Equal("2 textures", resource["Contents"]);
        // Reading leaves the status bar as it was.
        Assert.DoesNotContain("Reading", bridge.Status);
        Assert.False(bridge.EntryOperationBusy);
    }

    [AvaloniaFact]
    public void ALooseFileShowsItsFolderAndSize()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes.txt"), "hello");
        File.WriteAllBytes(Path.Combine(Work, "cars.ytd"), TwoTextures());
        Write(Path.Combine(Work, "x64", "readme.txt"), "x");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);

        EntryProperties notes = Read(bridge, "notes.txt");
        Assert.Equal("Game folder", Rows(notes, "LOCATION")["Folder"]);
        Assert.Equal(Path.Combine(Work, "notes.txt"), Rows(notes, "LOCATION")["Full path"]);
        Assert.Equal(new Dictionary<string, string> { ["Size"] = Formatting.FormatSize(5) }, Rows(notes, "SIZE"));
        Assert.Equal(["LOCATION", "SIZE"], notes.Sections.Select(section => section.Heading));

        // A resource on disk is compressed too: its unpacked size, and what it holds.
        EntryProperties ytd = Read(bridge, "cars.ytd");
        Assert.True(Rows(ytd, "SIZE").ContainsKey("Unpacked"));
        Assert.Equal("2 textures", Rows(ytd, "RESOURCE")["Contents"]);

        bridge.Navigate("x64");
        Settle(bridge);
        Assert.Equal("x64", Rows(Read(bridge, "readme.txt"), "LOCATION")["Folder"]);
    }

    [AvaloniaFact]
    public void AFolderCountsItsItems()
    {
        MakeGame();
        Write(Path.Combine(Work, "pack", "a.txt"), "a");
        Write(Path.Combine(Work, "pack", "b.txt"), "b");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        EntryProperties pack = Read(bridge, "pack");
        Assert.Equal("folder", pack.Glyph);
        Assert.Equal("Folder", pack.Kind);
        Assert.Equal("2 items", Rows(pack, "CONTENTS")["Items"]);
        Assert.DoesNotContain(pack.Sections, section => section.Heading == "SIZE");
    }

    [AvaloniaFact]
    public void AnRpfShowsItsEncryptionAndWhatItHolds()
    {
        MakeGame();
        RpfArchive inner = Archive("inner.rpf", ("a.txt", "a"), ("b.txt", "b"), ("c/d.txt", "d"), ("e/f.txt", "f"));
        SaveArchive(Path.Combine(Work, "mods.rpf"), ("data/carcols.meta", "<Item />"), ("readme.txt", "hello"), ("inner.rpf", inner));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);

        EntryProperties loose = Read(bridge, "mods.rpf");
        Assert.Equal("archive", loose.Glyph);
        Dictionary<string, string> archive = Rows(loose, "ARCHIVE");
        Assert.Equal("3 files in 1 folder", archive["Contains"]);
        Assert.Contains(archive["Encryption"], new[] { "None", "Open", "AES", "NG" });

        // The one inside it, read from the archive.
        bridge.OpenArchive(Path.Combine(Work, "mods.rpf"));
        Settle(bridge);
        Assert.Equal("4 files in 2 folders", Rows(Read(bridge, "inner.rpf"), "ARCHIVE")["Contains"]);
    }

    [AvaloniaFact]
    public void AFileGoneSinceItWasListedReportsTheFailedRead()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes.txt"), "n");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        File.Delete(Path.Combine(Work, "notes.txt"));
        Task<EntryProperties?> reading = bridge.ReadSelectionProperties();
        Settle(bridge);
        Assert.Null(reading.Result);
        Assert.StartsWith("Could not read the properties", bridge.Status);
    }

    [AvaloniaFact]
    public void ChangingTheSelectionDuringTheReadDropsTheResult()
    {
        MakeGame();
        Write(Path.Combine(Work, "a.txt"), "a");
        Write(Path.Combine(Work, "b.txt"), "b");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(Row(bridge, "a.txt"));
        Task<EntryProperties?> reading = bridge.ReadSelectionProperties();
        bridge.SelectEntry(Row(bridge, "b.txt"));
        Settle(bridge);
        Assert.Null(reading.Result);
    }

    [AvaloniaFact]
    public void OnlyOneItemHasProperties()
    {
        MakeGame();
        Write(Path.Combine(Work, "a.txt"), "a");
        Write(Path.Combine(Work, "b.txt"), "b");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        var menu = new ExplorerContextMenu { DataContext = bridge };

        bridge.SelectEntry(Row(bridge, "a.txt"));
        menu.Refresh();
        Assert.True(menu.PropertiesItem.IsVisible);
        Assert.True(menu.PropertiesItem.IsEnabled);
        Assert.Equal("Alt+Enter", menu.PropertiesItem.ShortcutText);

        bridge.SelectEntry(Row(bridge, "b.txt"), KeyModifiers.Control);
        menu.Refresh();
        Assert.False(menu.PropertiesItem.IsVisible);
        Assert.False(bridge.CanShowProperties);
        Assert.Null(bridge.ReadSelectionProperties().Result);

        bridge.ClearEntrySelection();
        menu.Refresh();
        Assert.False(menu.PropertiesItem.IsVisible);
    }

    [AvaloniaFact]
    public void AltEnterOpensTheSheet()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes.txt"), "n");
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1100, Height = 700 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        ExplorerStateRenders.Pump(300);
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        window.KeyPress(Key.Enter, RawInputModifiers.Alt, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.Alt, PhysicalKey.Enter, null);
        Settle(bridge);
        ExplorerStateRenders.Pump(300);

        WorkspaceView workspace = window.GetVisualDescendantsOf<WorkspaceView>();
        PropertiesDialog sheet = Assert.IsType<PropertiesDialog>(workspace.PropertiesSheet);
        Assert.True(sheet.IsOpen);
        Assert.Equal("PROPERTIES", sheet.Heading);
        Assert.Equal("notes.txt", sheet.Properties.Name);
        // As tall as what it shows: a file's few rows, not its whole share of the window (560 here), and nothing to scroll.
        Assert.True(sheet.FitsContent);
        Assert.Equal(sheet.FittedHeight, sheet.Bounds.Height);
        Assert.InRange(sheet.Bounds.Height, 200, 420);
        var scroller = Assert.IsType<Avalonia.Controls.ScrollViewer>(sheet.Content);
        Assert.True(scroller.Extent.Height <= scroller.Viewport.Height);
        window.Close();
    }
}
