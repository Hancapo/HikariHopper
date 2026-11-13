using FiveFury;
using FiveFury.Rpf;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Core;
using HikariHopper.Rendering;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

// The cache folder is process-wide, as in the explorer's tests.
[Collection(ExplorerCollection.Name)]
public sealed class GameTextureDirectoryTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("hh-names-").FullName;
    private readonly string cacheFolder = GameTextures.CacheFolder;

    public GameTextureDirectoryTests()
    {
        GameTextures.CacheFolder = Path.Combine(scratch, "cache");
        GameTextureDirectory.ForgetAll();
    }

    public void Dispose()
    {
        GameTextures.CacheFolder = cacheFolder;
        GameTextureDirectory.ForgetAll();
        Directory.Delete(scratch, recursive: true);
    }

    // A dictionary of textures of these names; with pixels, each texture's are random, so the dictionary stores large.
    private static byte[] Ytd(int pixels, params string[] names)
    {
        var dictionary = new TextureDictionary();
        var random = new Random(names.Length);
        foreach (string name in names)
        {
            int size = Math.Max(4, (int)Math.Sqrt(pixels * 2));
            byte[] data = new byte[Math.Max(8, size * size / 2)];
            random.NextBytes(data);
            dictionary.Textures.Add(new Texture(name, size, size, GameFormat.BC1Unorm, 1, data));
        }
        return new Ytd(dictionary, GameTarget.Legacy).Write();
    }

    private string Game() => Directory.CreateDirectory(Path.Combine(scratch, "game")).FullName;

    private static void Save(RpfArchive archive, string root, string path)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        archive.Save(file);
    }

    private static RpfArchive Archive(string name, params (string Path, byte[] Ytd)[] dictionaries)
    {
        var archive = new RpfArchive(name);
        foreach (var (path, ytd) in dictionaries)
            archive.AddResource(path, ytd);
        return archive;
    }

    private static LoadedGame Loaded(string root) => new(root, GameTarget.Legacy, null!);

    [Fact]
    public async Task Every_dictionary_of_the_games_rpfs_is_found_by_the_names_it_holds_the_nearest_first()
    {
        string root = Game();
        Save(Archive("x64a.rpf",
            ("levels/gta5/generic/vehshare.ytd", Ytd(0, "vehicle_generic_detail2", "shared")),
            ("props/near.ytd", Ytd(0, "shared")),
            // Big enough to be stored over the part read for the names, which still holds them.
            ("props/big.ytd", Ytd(256 * 1024, "big_one", "big_two"))), root, "x64a.rpf");
        // A DLC pack, its vehicle mods in an RPF of their own inside it.
        RpfArchive mods = Archive("car_mods.rpf", ("car_mods.ytd", Ytd(0, "shared", "car_livery")));
        var pack = new RpfArchive("dlc.rpf");
        pack.AddBinary("x64/vehiclemods/car_mods.rpf", mods.Write());
        Save(pack, root, "update/x64/dlcpacks/pack/dlc.rpf");
        // The game's audio holds no textures and isn't read.
        Save(Archive("sfx.rpf", ("odd.ytd", Ytd(0, "audio_texture"))), root, "x64/audio/sfx.rpf");

        GameTextureDirectory directory = GameTextureDirectory.ForGame(Loaded(root));
        Assert.Null(directory.Holding("shared", null));
        await directory.Load();
        Assert.True(directory.Ready);
        string[] Holding(string name, string? near) => [.. directory.Holding(name, near)!.Select(dictionary => dictionary.Path)];

        // Whatever its case, by the folders it shares with the model's path.
        Assert.Equal(["x64a.rpf/props/near.ytd", "x64a.rpf/levels/gta5/generic/vehshare.ytd", "update/x64/dlcpacks/pack/dlc.rpf/x64/vehiclemods/car_mods.rpf/car_mods.ytd"],
            Holding("SHARED", "x64a.rpf/props/bench.ydr"));
        Assert.Equal("update/x64/dlcpacks/pack/dlc.rpf/x64/vehiclemods/car_mods.rpf/car_mods.ytd",
            Holding("shared", "update/x64/dlcpacks/pack/dlc.rpf/x64/vehiclemods/car_mods.rpf/car_bonnet.yft")[0]);
        // Nearer none of them: the game's update over its own.
        Assert.StartsWith("update/", Holding("shared", "x64b.rpf/other.ydr")[0]);
        Assert.Equal(["x64a.rpf/levels/gta5/generic/vehshare.ytd"], Holding("vehicle_generic_detail2", "update/x64/dlcpacks/pack/dlc.rpf/x64/vehiclemods/car_mods.rpf/car_bonnet.yft"));
        Assert.Equal(["x64a.rpf/props/big.ytd"], Holding("big_two", null));
        Assert.Empty(Holding("audio_texture", null));
        Assert.Empty(Holding("nowhere", null));
        Assert.True(directory.Holding("big_one", null)![0].Size > 256 * 1024);
    }

    [Fact]
    public async Task Each_rpf_is_read_again_only_once_it_changes()
    {
        string root = Game();
        Save(Archive("x64a.rpf", ("a.ytd", Ytd(0, "first"))), root, "x64a.rpf");
        Save(Archive("x64b.rpf", ("b.ytd", Ytd(0, "second"))), root, "x64b.rpf");
        await GameTextureDirectory.ForGame(Loaded(root)).Load();

        // The next run: x64a.rpf's bytes are other ones of the same size and time, which only reading it would tell.
        string a = Path.Combine(root, "x64a.rpf");
        DateTime written = File.GetLastWriteTimeUtc(a);
        File.WriteAllBytes(a, new byte[new FileInfo(a).Length]);
        File.SetLastWriteTimeUtc(a, written);
        // x64b.rpf saved again with another dictionary in it.
        Save(Archive("x64b.rpf", ("b.ytd", Ytd(0, "second", "third"))), root, "x64b.rpf");
        GameTextureDirectory.ForgetAll();
        GameTextureDirectory next = GameTextureDirectory.ForGame(Loaded(root));
        await next.Load();
        Assert.Single(next.Holding("first", null)!);
        Assert.Single(next.Holding("third", null)!);

        // An RPF HikariHopper writes is read again the next time the directory is asked for; the others aren't.
        Save(Archive("x64b.rpf", ("b.ytd", Ytd(0, "fourth"))), root, "x64b.rpf");
        GameTextureDirectory.Changed(Path.Combine(root, "x64b.rpf"));
        Assert.Null(next.Holding("fourth", null));
        await next.Load();
        Assert.Single(next.Holding("fourth", null)!);
        Assert.Empty(next.Holding("third", null)!);
        Assert.Single(next.Holding("first", null)!);
    }

    private static NearbyDictionary Dictionary(string name, List<string> reads, params string[] textures)
    {
        byte[] ytd = Ytd(0, textures);
        return new NearbyDictionary(name, ytd.Length, () => { reads.Add(name); return ytd; });
    }

    [Fact]
    public void A_missing_texture_is_looked_for_where_the_directory_says_and_beside_the_model_only_when_it_cant_say()
    {
        var reads = new List<string>();
        NearbyDictionary game = Dictionary("parent.ytd", reads, "interior");
        NearbyDictionary held = Dictionary("vehshare.ytd", reads, "vehicle_generic_detail2");
        NearbyDictionary beside = Dictionary("beside.ytd", reads, "vehicle_generic_detail2", "only_beside");
        IReadOnlyList<NearbyDictionary>? Directory(string name) => name == "vehicle_generic_detail2" ? [held] : [];
        var lookup = new TextureLookup([], [game], null, Directory, [beside]);
        // The game's dictionaries for it first, then the directory's: what's beside the model isn't read.
        Assert.NotNull(lookup.Find("vehicle_generic_detail2"));
        Assert.Equal(["parent.ytd", "vehshare.ytd"], reads);
        // No dictionary of the game holds one: nor do those beside it.
        Assert.Null(lookup.Find("only_beside"));
        Assert.Equal(["parent.ytd", "vehshare.ytd"], reads);

        // When the directory can't say (it isn't ready, or the model isn't in the game's RPFs), they're searched.
        reads.Clear();
        lookup = new TextureLookup([], [game], null, _ => null, [beside]);
        Assert.NotNull(lookup.Find("only_beside"));
        Assert.Equal(["parent.ytd", "beside.ytd"], reads);

        // Nor when none it names can be read.
        reads.Clear();
        NearbyDictionary broken = new("gone.ytd", 10, () => throw new IOException("gone"));
        lookup = new TextureLookup([], [], null, _ => [broken], [beside]);
        Assert.NotNull(lookup.Find("only_beside"));
        Assert.Equal(["beside.ytd"], reads);
    }

    [Fact]
    public void A_drawable_outside_the_games_rpfs_or_outside_a_game_has_no_directory_to_ask()
    {
        LoadedGame game = Loaded(Game());
        Assert.Null(ViewModels.ExplorerViewModel.GameHolding(null, "x64a.rpf/props/bench.ydr"));
        Assert.Null(ViewModels.ExplorerViewModel.GameHolding(game, null));
        Assert.Null(ViewModels.ExplorerViewModel.GameHolding(game, "mods/bench.ydr"));
        Assert.Null(ViewModels.ExplorerViewModel.GameHolding(game, "x64a.rpf/props/bench.ytd"));
        Assert.NotNull(ViewModels.ExplorerViewModel.GameHolding(game, "x64a.rpf/props/bench.ydr"));
    }
}

[Collection(ExplorerCollection.Name)]
public sealed class RealGameTextureDirectoryTests
{
    [Fact]
    public async Task A_vehicle_mod_part_finds_the_shared_vehicle_textures_of_the_base_game()
    {
        string root = TestGames.Legacy();
        string cacheFolder = GameTextures.CacheFolder;
        GameTextures.CacheFolder = Directory.CreateTempSubdirectory("hh-names-real-").FullName;
        try
        {
            GameTextureDirectory.ForgetAll();
            GameTextureDirectory directory = GameTextureDirectory.ForGame(RpfProvider.LoadGame(root));
            await directory.Load();
            const string part = "update/x64/dlcpacks/mpsum/dlc.rpf/x64/levels/mpsum/vehiclemods/manana2_mods.rpf/man2_audio5.yft";
            IReadOnlyList<GameDictionary> holding = directory.Holding("vehicle_generic_detail2", part)!;
            Assert.NotEmpty(holding);
            Assert.Contains(holding, dictionary => dictionary.Path.EndsWith("vehshare.ytd", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(directory.Holding("no_texture_is_named_this", part)!);
        }
        finally
        {
            GameTextureDirectory.ForgetAll();
            string made = GameTextures.CacheFolder;
            GameTextures.CacheFolder = cacheFolder;
            Directory.Delete(made, recursive: true);
        }
    }
}
