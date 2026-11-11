using CommonFury.Hashing;
using FiveFury;
using HikariHopper.Core;

namespace HikariHopper.Tests;

// The cache folder is process-wide, as in the explorer's tests.
[Collection(ExplorerCollection.Name)]
public sealed class GameTexturesTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("hh-game-textures-").FullName;
    private readonly string previousCache = GameTextures.CacheFolder;

    public GameTexturesTests() => GameTextures.CacheFolder = Path.Combine(scratch, "cache");

    public void Dispose()
    {
        GameTextures.CacheFolder = previousCache;
        Directory.Delete(scratch, recursive: true);
    }

    private static uint H(string name) => new Hash32(name).Value;

    [Fact]
    public void A_FiveFury_source_path_is_a_path_from_the_game_folder()
    {
        const string root = @"C:\Games\GTAV";
        Assert.Equal("x64g.rpf/levels/gta5/generic/gtxd.rpf/globalroads.ytd",
            GameTextures.GamePath(root, @"C:\Games\GTAV\x64g.rpf!/levels/gta5/generic/gtxd.rpf!/globalroads.ytd"));
        Assert.Equal("update/x64/dlcpacks/mpheist4/dlc1.rpf/x64/levels/gta5/generic/mph4_gtxd.rpf/gdislxterrain+hi.ytd",
            GameTextures.GamePath(root, @"C:\Games\GTAV\update\x64\dlcpacks\mpheist4\dlc1.rpf!/x64/levels/gta5/generic/mph4_gtxd.rpf!/gdislxterrain+hi.ytd"));
        Assert.Null(GameTextures.GamePath(root, @"D:\Elsewhere\x64g.rpf!/a.ytd"));
    }

    [Fact]
    public void The_rpfs_the_game_doesnt_mount_itself_are_mounted_over_it_except_dlc_packs()
    {
        string Rpf(string path)
        {
            string full = Path.Combine(scratch, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, []);
            return full;
        }
        string x64a = Rpf("x64a.rpf"), update = Rpf(@"update\update.rpf");
        string touhou = Rpf(@"fix\touhou.rpf"), hola = Rpf(@"fix\hola.rpf"), mods = Rpf(@"mods\x64a.rpf");
        Rpf(@"update\x64\dlcpacks\unlisted\dlc.rpf");
        Rpf(@"x64\audio\sfx\RADIO_01_CLASS_ROCK.rpf");

        // The game mounts its own; the rest, in path order, but not DLC packs, which the game's list decides, nor its audio.
        var mounted = new HashSet<string>([x64a, update], StringComparer.OrdinalIgnoreCase);
        Assert.Equal([hola, touhou, mods], GameTextures.Tables.ExtraArchives(scratch, mounted));
    }

    [Fact]
    public void A_model_looks_in_its_archetypes_dictionaries_then_their_parents_nearest_first_then_the_detail_maps()
    {
        var tables = new GameTextures.Tables();
        foreach (string name in new[] { "hw1_02_grnd", "hwblockgroup1", "hollywoodrd", "globalroads", "hw1_lod", "mapdetail" })
            tables.Files[H(name)] = ($"x64g.rpf/{name}.ytd", 100);
        tables.Models[H("hw1_02_grnd2")] = [H("hw1_02_grnd"), H("hw1_lod")];
        tables.Parents[H("hw1_02_grnd")] = H("hwblockgroup1");
        tables.Parents[H("hwblockgroup1")] = H("hollywoodrd");
        tables.Parents[H("hollywoodrd")] = H("globalroads");
        tables.Parents[H("hw1_lod")] = H("globalroads");
        // A parent the game doesn't ship is skipped, and a loop ends.
        tables.Parents[H("globalroads")] = H("missing_parent");
        tables.Parents[H("missing_parent")] = H("globalroads");

        Assert.Equal(["hw1_02_grnd", "hw1_lod", "hwblockgroup1", "globalroads", "hollywoodrd", "mapdetail"],
            tables.For("hw1_02_grnd2.ydr").Select(d => Path.GetFileNameWithoutExtension(d.Path)));
        // The detail maps' dictionary is on no chain, and every drawable looks in it, last.
        Assert.Equal(["x64g.rpf/mapdetail.ytd"], tables.For("unknown.ydr").Select(d => d.Path));
        Assert.Empty(new GameTextures.Tables().For("unknown.ydr"));
    }

    [Fact]
    public void A_drawable_dictionary_or_fragment_looks_in_the_peds_resident_dictionary_last()
    {
        var tables = new GameTextures.Tables();
        foreach (string name in new[] { "mapdetail", "comp_peds_generic" })
            tables.Files[H(name)] = ($"x64e.rpf/{name}.ytd", 100);
        // Peds are drawable dictionaries (or fragments): their hair's noise maps are in the resident dictionary.
        Assert.Equal(["mapdetail", "comp_peds_generic"], tables.For("a_m_y_hipster_01.ydd").Select(d => Path.GetFileNameWithoutExtension(d.Path)));
        Assert.Equal(["mapdetail", "comp_peds_generic"], tables.For("a_c_chop.yft").Select(d => Path.GetFileNameWithoutExtension(d.Path)));
        Assert.Equal(["mapdetail"], tables.For("prop_bench_01a.ydr").Select(d => Path.GetFileNameWithoutExtension(d.Path)));
    }

    [Fact]
    public void A_vehicle_high_detail_model_shares_its_vehicle_archetype()
    {
        var tables = new GameTextures.Tables();
        tables.Files[H("adder")] = ("x64v.rpf/adder.ytd", 10);
        tables.Models[H("adder")] = [H("adder")];
        Assert.Equal(["x64v.rpf/adder.ytd"], tables.For("adder_hi.yft").Select(d => d.Path));
    }

    [Fact]
    public async Task Tables_that_cannot_be_made_leave_a_model_with_no_game_dictionaries()
    {
        int attempts = 0;
        // Whatever stops the tables, the model still opens, without the game's dictionaries; the attempt isn't repeated.
        var textures = new GameTextures(scratch, GameTarget.Legacy, () =>
        {
            Interlocked.Increment(ref attempts);
            throw new IndexOutOfRangeException("a broken archive");
        });
        Assert.Empty(await textures.DictionariesFor("hw1_02_grnd2.ydr"));
        Assert.Empty(await textures.DictionariesFor("prop_bench_01a.ydr"));
        Assert.False(textures.Ready);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void The_cached_tables_are_read_back_only_while_the_game_is_unchanged()
    {
        Directory.CreateDirectory(Path.Combine(scratch, "game"));
        string rpf = Path.Combine(scratch, "game", "x64a.rpf");
        File.WriteAllBytes(rpf, [1, 2, 3]);
        byte[] stamp = GameTextures.Stamp(Path.Combine(scratch, "game"), GameTarget.Legacy);

        var tables = new GameTextures.Tables();
        tables.Files[H("globalroads")] = ("x64a.rpf/globalroads.ytd", 42);
        tables.Parents[H("a")] = H("globalroads");
        tables.Models[H("bench")] = [H("a")];
        string cache = Path.Combine(scratch, "tables.bin");
        tables.Write(cache, stamp);

        GameTextures.Tables read = Assert.IsType<GameTextures.Tables>(GameTextures.Tables.Read(cache, stamp));
        Assert.Equal(("x64a.rpf/globalroads.ytd", 42L), read.Files[H("globalroads")]);
        Assert.Equal(H("globalroads"), read.Parents[H("a")]);
        Assert.Equal([H("a")], read.Models[H("bench")]);

        // An archive written since, another edition, or a new archive: the tables are made again.
        File.WriteAllBytes(rpf, [1, 2, 3, 4]);
        Assert.Null(GameTextures.Tables.Read(cache, GameTextures.Stamp(Path.Combine(scratch, "game"), GameTarget.Legacy)));
        Assert.NotEqual(stamp, GameTextures.Stamp(Path.Combine(scratch, "game"), GameTarget.Enhanced));
    }

    [Fact]
    public async Task Saving_a_files_contents_into_an_rpf_keeps_the_cached_tables()
    {
        string game = Directory.CreateDirectory(Path.Combine(scratch, "game")).FullName;
        string x64a = Path.Combine(game, "x64a.rpf"), update = Path.Combine(Directory.CreateDirectory(Path.Combine(game, "update")).FullName, "update.rpf");
        File.WriteAllBytes(x64a, [1]);
        File.WriteAllBytes(update, [2]);
        int builds = 0;
        GameTextures.Tables Build()
        {
            Interlocked.Increment(ref builds);
            var tables = new GameTextures.Tables();
            tables.Files[H("mapdetail")] = ("x64a.rpf/mapdetail.ytd", 42);
            return tables;
        }
        async Task<GameTextures> Loaded()
        {
            var textures = new GameTextures(game, GameTarget.Legacy, Build);
            await textures.Load();
            return textures;
        }
        void Write(string path, byte[] bytes)
        {
            DateTime before = File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
        }

        // Made once, then read from the cache.
        GameTextures first = await Loaded();
        Assert.Equal(1, builds);
        await Loaded();
        Assert.Equal(1, builds);

        // HikariHopper saves a texture dictionary into an RPF, twice: the cache holds for the game as it is now.
        Write(x64a, [1, 1]);
        first.Restamp(x64a);
        Write(x64a, [1, 1, 1]);
        first.Restamp(x64a);
        GameTextures second = await Loaded();
        Assert.Equal(1, builds);
        Assert.Equal(("x64a.rpf/mapdetail.ytd", 42L), Assert.Single((await second.DictionariesFor("x.ydr")).Select(d => (d.Path, d.Size))));

        // Another archive written meanwhile, by anything else, or one added: the tables are made again next time.
        Write(update, [2, 2]);
        Write(x64a, [1]);
        second.Restamp(x64a);
        GameTextures third = await Loaded();
        Assert.Equal(2, builds);
        File.WriteAllBytes(Path.Combine(game, "x64b.rpf"), [3]);
        Write(x64a, [1, 1]);
        third.Restamp(x64a);
        await Loaded();
        Assert.Equal(3, builds);

        // Before the tables are read, there's nothing to keep.
        Write(x64a, [1]);
        new GameTextures(game, GameTarget.Legacy, Build).Restamp(x64a);
        await Loaded();
        Assert.Equal(4, builds);
    }
}
