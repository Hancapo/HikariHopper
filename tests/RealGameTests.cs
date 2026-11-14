using FiveFury.Assets;
using FiveFury.Rpf;
using HikariHopper.Core;

namespace HikariHopper.Tests;

/// <summary>Checks against a real Legacy installation (<see cref="TestGames"/>); skipped without one.</summary>
public sealed class RealGameTests
{
    private static string LegacyRoot() => TestGames.Legacy();

    [Fact]
    public void LoadsKeysAndBrowsesEncryptedAndNestedArchives()
    {
        string root = LegacyRoot();
        var provider = new RpfProvider();
        provider.OpenGame(root);
        Assert.True(provider.HasGame);
        Assert.Contains(provider.Entries(), entry => entry.Name.Equals("x64a.rpf", StringComparison.OrdinalIgnoreCase));

        EntryRecord? nested = null;
        foreach (FileInfo candidate in new DirectoryInfo(root).GetFiles("x64*.rpf").OrderBy(file => file.Length))
        {
            provider.OpenArchive(candidate.FullName);
            Assert.Equal(candidate.Name, provider.ArchiveGamePath);
            Assert.NotEmpty(provider.Entries());
            if ((nested = FindNestedRpf(provider, ".")) is not null)
                break;
        }
        Assert.NotNull(nested);
        provider.OpenNested(nested);
        Assert.True(provider.ArchiveIsNested);
        Assert.Equal(nested.Path, provider.ArchivePrefix);
        Assert.NotEmpty(provider.Entries());

        EntryRecord file = provider.Entries().First(entry => !entry.IsDirectory);
        Assert.NotEmpty(provider.ReadEntryBytes(file));
        Assert.NotNull(provider.ArchiveEntryFromRoot(file.Path));
        provider.Close();
    }

    [Fact]
    public void RewritesACopiedEncryptedArchiveInPlace()
    {
        string root = LegacyRoot();
        string scratch = Directory.CreateTempSubdirectory("hikari-rpf-").FullName;
        try
        {
            string copy = Path.Combine(scratch, "x64a.rpf");
            File.Copy(Path.Combine(root, "x64a.rpf"), copy);
            var crypto = RpfProvider.LoadGame(root).Crypto;
            var target = new EntryCreationTarget(ArchivePath: copy, Crypto: crypto, Game: FiveFury.GameTarget.Legacy);
            EntryOperations.CreateFolderAt(target, "hikari_test");
            EntryOperations.CreateYtdAt(target, "empty");

            ArchiveNode reopened = ArchiveNode.OpenRoot(copy, crypto);
            try
            {
                Assert.IsType<RpfDirectoryEntry>(reopened.Archive.Find("hikari_test"));
                var ytd = Assert.IsType<RpfResourceFileEntry>(reopened.Archive.Find("empty.ytd"));
                Assert.Empty(FiveFury.Ytd.Ytd.Read(ytd.ReadStandalone()).Textures);
            }
            finally { reopened.Close(); }
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static EntryRecord? FindNestedRpf(RpfProvider provider, string directory)
    {
        foreach (EntryRecord entry in provider.Entries(directory))
        {
            if (!entry.IsDirectory && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                return entry;
            if (entry.IsDirectory && FindNestedRpf(provider, RpfProvider.EntryLocalPath(entry)) is { } found)
                return found;
        }
        return null;
    }

    [Fact]
    public void FiveFurysArchiveIndexGivesTheSameTextureTables()
    {
        string root = LegacyRoot()!;
        LoadedGame game = RpfProvider.LoadGame(root);
        string scratch = Directory.CreateTempSubdirectory("hikari-index-").FullName;
        try
        {
            byte[] Written(GameTextures.Tables tables)
            {
                string path = Path.Combine(scratch, "tables.bin");
                tables.Write(path, [1]);
                return File.ReadAllBytes(path);
            }
            byte[] without = Written(GameTextures.Tables.Build(root, game.Target, game.Crypto));
            string index = Path.Combine(scratch, "Index");
            // Made with the index empty, which fills it, then with every archive taken from it.
            Assert.Equal(without, Written(GameTextures.Tables.Build(root, game.Target, game.Crypto, new GameIndexCache(index))));
            Assert.NotEmpty(Directory.EnumerateFiles(index, "*", SearchOption.AllDirectories));
            Assert.Equal(without, Written(GameTextures.Tables.Build(root, game.Target, game.Crypto, new GameIndexCache(index))));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void ADlcsMapPiecesHaveTheirArchetypesDictionaryAndItsGtxdParents()
    {
        string root = LegacyRoot()!;
        LoadedGame game = RpfProvider.LoadGame(root);
        GameTextures.Tables tables = GameTextures.Tables.Build(root, game.Target, game.Crypto);
        // Cayo Perico's island, which its DLC's map update adds to the game's map: its archetype names the
        // dictionary, and the island's gtxd.meta its parents.
        string[] found = [.. tables.For("h4_mph4_ter02_track_02.ydr").Select(dictionary => Path.GetFileName(dictionary.Path))];
        Assert.Equal(["h4_mph4_ter02_dirttracks.ytd", "gdislxtertracks.ytd", "gdislxterrain.ytd", "mapdetail.ytd"], found);
    }
}
