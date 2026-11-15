using HikariHopper.Core;
using HikariHopper.ViewModels;
using RedFury.Resources;
using RedFury.Rpf;
using Hash32 = CommonFury.Hashing.Hash32;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2's RPF8 archives, read through the format-neutral layer.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rpf8ArchiveTests : ExplorerTestBase
{
    private static readonly byte[] ResourceData = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    // An RPF8 with a binary file, a drawable resource and an RPF nested in it holding one file; none of their names is
    // one RedFury knows, so they show as their hashes.
    private string Archive()
    {
        var nested = new RpfBuilder();
        nested.Files.Add(RpfFile.FromBytes(new Hash32(0x0000BEEF), "inside"u8));
        var archive = new RpfBuilder();
        archive.Files.Add(RpfFile.FromBytes(new Hash32(0x12345678), "hello"u8));
        archive.Files.Add(RpfFile.FromResource(new Hash32(0x00C0FFEE), new Rsc8Resource(new Rsc8Header(0xA2, 0, 255, 0, 0x1A, 0x12), ResourceData), 2));
        archive.Files.Add(RpfFile.FromArchive(new Hash32(0x0000000A), nested));
        string path = Path.Combine(Work, "x.rpf");
        File.WriteAllBytes(path, archive.Write());
        return path;
    }

    [Fact]
    public void An_RPF8_lists_reads_and_nests_through_the_neutral_layer()
    {
        ArchiveNode root = ArchiveNode.OpenRoot(Archive(), null);
        try
        {
            // Told from RPF7 by its first bytes, and opened with the keys HikariHopper ships when none are given.
            Assert.IsType<Rpf8ArchiveNode>(root);
            Assert.Same(Rdr2Keys.Bundled, root.Keys);
            Assert.Equal("RPF8", root.Format);
            Assert.Equal("RSC8", root.ResourceFormat);
            Assert.Equal("PC", root.Platform);
            Assert.Equal("None", root.Encryption);
            Assert.Equal(3, root.EnumerateEntries().Count());
            Assert.Equal(3, root.RootDirectory.Children.Count);

            ArchiveEntry hello = root.RootDirectory.Children.Single(entry => entry.Name.StartsWith("0x12345678", StringComparison.Ordinal));
            Assert.True(hello.IsBinaryFile);
            Assert.False(hello.Compressed);
            Assert.Equal(5, hello.Size);
            Assert.Equal("hello"u8.ToArray(), hello.Read());
            Assert.Same(hello, root.EntryAt(hello.Path));

            ArchiveEntry drawable = root.EntryAt("0x00C0FFEE.ydr")!;
            Assert.True(drawable.IsResource);
            Assert.Equal(0xA2, drawable.ResourceVersion);
            // Read as a standalone RSC8 file, which RedFury reads back to the same data.
            Assert.Equal(ResourceData, Rsc8Resource.Read(drawable.Read()).Data.ToArray());

            ArchiveEntry inner = root.EntryAt("0x0000000A.rpf")!;
            Assert.True(inner.IsBinaryFile);
            ArchiveNode nested = root.OpenNested(inner);
            Assert.Same(nested, root.OpenNested(inner));
            Assert.Equal("0x0000000A.rpf", nested.Prefix);
            var (owner, note) = root.FindEntry($"0x0000000A.rpf/{nested.RootDirectory.Children[0].Name}")!.Value;
            Assert.Same(nested, owner);
            Assert.Equal("inside"u8.ToArray(), note.Read());
            Assert.Throws<InvalidDataException>(() => root.OpenNested(drawable));
        }
        finally
        {
            root.Close();
        }
        Assert.True(root.IsClosed);
    }

    [Fact]
    public void The_installed_games_archives_open_with_their_folders_and_names()
    {
        string game = TestGames.Rdr2();
        ArchiveNode root = ArchiveNode.OpenRoot(Path.Combine(game, "common_0.rpf"), Rdr2Keys.Bundled);
        try
        {
            Assert.Equal("RPF8", root.Format);
            // Folders rebuilt from the names RedFury knows, and files read from them.
            Assert.Contains(root.RootDirectory.Children, entry => entry.IsDirectory);
            ArchiveEntry[] files = [.. root.EnumerateEntries().Where(entry => !entry.IsDirectory)];
            Assert.Contains(files, entry => !entry.Name.StartsWith("0x", StringComparison.Ordinal));
            ArchiveEntry plain = files.First(entry => entry.IsBinaryFile && entry.Size is > 0 and < 1_000_000);
            Assert.Equal(plain.Size, plain.Read().Length);
        }
        finally
        {
            root.Close();
        }
    }

    [Fact]
    public void The_explorer_browses_into_the_installed_games_archives()
    {
        string game = TestGames.Rdr2();
        ExplorerViewModel.GameLoader = path => RpfProvider.LoadGame(path);
        var tab = new ExplorerViewModel();
        tab.OpenGame(game);
        Assert.Equal(GameSettings.Rdr2Edition, tab.GameEditionId);
        tab.GoToAddress("common_0.rpf");
        Assert.True(tab.Provider.InArchive);
        Assert.Equal("RPF8", tab.Provider.Info.Version);
        EntryRecord folder = tab.Provider.Entries().First(entry => entry.IsDirectory);
        tab.GoToAddress($"common_0.rpf/{folder.Name}");
        Assert.NotEmpty(tab.Provider.Entries());
    }
}
