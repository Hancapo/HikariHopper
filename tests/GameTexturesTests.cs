using CommonFury.Hashing;
using FiveFury;
using HikariHopper.Core;

namespace HikariHopper.Tests;

public sealed class GameTexturesTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("hh-game-textures-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

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
    public void A_model_looks_in_its_archetypes_dictionaries_then_their_parents_nearest_first()
    {
        var tables = new GameTextures.Tables();
        foreach (string name in new[] { "hw1_02_grnd", "hwblockgroup1", "hollywoodrd", "globalroads", "hw1_lod" })
            tables.Files[H(name)] = ($"x64g.rpf/{name}.ytd", 100);
        tables.Models[H("hw1_02_grnd2")] = [H("hw1_02_grnd"), H("hw1_lod")];
        tables.Parents[H("hw1_02_grnd")] = H("hwblockgroup1");
        tables.Parents[H("hwblockgroup1")] = H("hollywoodrd");
        tables.Parents[H("hollywoodrd")] = H("globalroads");
        tables.Parents[H("hw1_lod")] = H("globalroads");
        // A parent the game doesn't ship is skipped, and a loop ends.
        tables.Parents[H("globalroads")] = H("missing_parent");
        tables.Parents[H("missing_parent")] = H("globalroads");

        Assert.Equal(["hw1_02_grnd", "hw1_lod", "hwblockgroup1", "globalroads", "hollywoodrd"],
            tables.For("hw1_02_grnd2.ydr").Select(d => Path.GetFileNameWithoutExtension(d.Path)));
        Assert.Empty(tables.For("unknown.ydr"));
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
}
