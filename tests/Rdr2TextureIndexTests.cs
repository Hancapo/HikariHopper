using HikariHopper.Core;
using HikariHopper.Rendering;
using Jenkins = RedFury.Hashing.Jenkins;

namespace HikariHopper.Tests;

/// <summary>Where Red Dead Redemption 2 keeps its models' textures (<see cref="Rdr2TextureIndex"/>), after RedFury's catalog.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2TextureIndexTests
{
    private static uint H(string name) => Jenkins.Hash(name);

    private static Rdr2TextureIndex.Held Ytd(string name, int at) => new(H(name), [at], 100 + at);

    [Fact]
    public void A_models_dictionaries_come_high_definition_first_then_its_archetypes_and_its_own_each_with_its_parents()
    {
        var common = new Rdr2TextureIndex.Part("common_0.rpf", 1, 1,
            [Ytd("shared_parent", 0), Ytd("arch_txd", 1), Ytd("barrel", 2), Ytd("barrel_hd", 3), Ytd("unrelated", 4)],
            [new Rdr2TextureIndex.Ytyp(H("props"), [(H("barrel"), H("arch_txd"))])],
            [(H("arch_txd"), H("shared_parent"))],
            // The manifest binds an HD dictionary to the drawable (RedFury's YmfAssetType 1).
            [(H("barrel"), 1, H("barrel_hd"))]);
        var found = Rdr2TextureIndex.DictionariesFor([common], H("barrel"), 2);
        Assert.Equal([3, 1, 0, 2], found.Select(dictionary => dictionary.Indices[0]));
        Assert.All(found, dictionary => Assert.Equal("common_0.rpf", dictionary.Archive));
    }

    [Fact]
    public void A_later_archive_overrides_an_earlier_ones_dictionary_parent_and_ytyp()
    {
        var base0 = new Rdr2TextureIndex.Part("levels_0.rpf", 1, 1, [Ytd("barrel", 0), Ytd("old_txd", 1), Ytd("old_parent", 2)],
            [new Rdr2TextureIndex.Ytyp(H("props"), [(H("barrel"), H("old_txd"))])], [(H("old_txd"), H("old_parent"))], []);
        var update = new Rdr2TextureIndex.Part("update_1.rpf", 1, 1, [Ytd("barrel", 5), Ytd("new_txd", 6)],
            [new Rdr2TextureIndex.Ytyp(H("props"), [(H("barrel"), H("new_txd"))])], [], []);
        var found = Rdr2TextureIndex.DictionariesFor([base0, update], H("barrel"), 2);
        // The update's YTYP of the same name replaces the base's, and its dictionary of the same name the base's.
        Assert.Equal([("update_1.rpf", 6), ("update_1.rpf", 5)], found.Select(dictionary => (dictionary.Archive, dictionary.Indices[0])));
    }

    [Fact]
    public void Its_cache_reads_back_as_it_was_written()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hh-rdr2-index-{Guid.NewGuid():N}.bin");
        var part = new Rdr2TextureIndex.Part("levels_0.rpf", 1234, 5678, [new(H("a"), [1, 2, 3], 99)],
            [new Rdr2TextureIndex.Ytyp(H("y"), [(H("b"), H("c"))])], [(H("c"), H("d"))], [(H("b"), 1, H("e"))]);
        try
        {
            Rdr2TextureIndex.WriteCache(path, [part]);
            Rdr2TextureIndex.Part read = Assert.Single(Rdr2TextureIndex.ReadCache(path).Values);
            Assert.Equal((part.Archive, part.Length, part.Written), (read.Archive, read.Length, read.Written));
            Assert.Equal([1, 2, 3], Assert.Single(read.Dictionaries).Indices);
            Assert.Equal(99, read.Dictionaries[0].Size);
            Assert.Equal(part.Ytyps[0].Bindings, read.Ytyps[0].Bindings);
            Assert.Equal(part.Parents, read.Parents);
            Assert.Equal(part.HighDefinition, read.HighDefinition);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_map_prop_from_the_installed_game_finds_its_textures_in_the_games_dictionaries()
    {
        string game = TestGames.Rdr2();
        // A prop naming textures it doesn't carry.
        (string name, byte[] data) = Rdr2ModelTests.Find(".ydr", (_, data) => RedFury.Ydr.Ydr.Read(data).Drawable.UnresolvedTextures().Count() > 3)
            ?? throw new InvalidOperationException("None of the archives searched holds a prop without its textures");
        Rdr2TextureIndex.ForgetAll();
        Rdr2TextureIndex index = Rdr2TextureIndex.ForGame(game, Rdr2Keys.Bundled.Crypto);
        var found = index.DictionariesFor(Jenkins.Hash(Path.GetFileNameWithoutExtension(name)), 2).GetAwaiter().GetResult();
        Assert.NotEmpty(found);
        // Cached for the next time.
        Assert.True(File.Exists(index.CachePath));
        NearbyDictionary[] nearby = [.. found.Select(dictionary => new NearbyDictionary(dictionary.Archive, dictionary.Size,
            () => index.ReadDictionary(dictionary.Archive, dictionary.Indices)))];
        int Missing(DrawableScene scene) => scene.Materials.Sum(material => material.MissingTextures.Count);
        DrawableScene bare = ModelContent.Read(name, data).Surfaces!;
        DrawableScene textured = ModelContent.Read(name, data, null, nearby).Surfaces!;
        Assert.True(Missing(textured) < Missing(bare), $"{name}: {Missing(bare)} missing, then {Missing(textured)}");
    }
}
