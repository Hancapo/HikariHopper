using FiveFury;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Rendering;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

public sealed class ParsedDictionariesTests
{
    // A dictionary of textures of these names, each 8 bytes of data.
    private static byte[] Ytd(params string[] names)
    {
        var dictionary = new TextureDictionary();
        foreach (string name in names)
            dictionary.Textures.Add(new Texture(name, 4, 4, GameFormat.BC1Unorm, 1, new byte[8]));
        return new Ytd(dictionary, GameTarget.Legacy).Write();
    }

    private static string Key() => $"test|{Guid.NewGuid():N}";

    [Fact]
    public void A_dictionary_is_parsed_once_for_every_model_that_reads_it()
    {
        byte[] shared = Ytd("road_d", "road_n");
        string key = Key();
        int reads = 0;
        NearbyDictionary Shared() => new("globalroads.ytd", shared.Length, () => { reads++; return shared; }, key);
        // Two models looking up the same texture, each its own lookup: the dictionary is read and parsed for the first.
        Texture? first = new TextureLookup([], [Shared()]).Find("road_d");
        Texture? second = new TextureLookup([], [Shared()]).Find("road_d");
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, reads);
        // One without a key is read each time.
        int unkeyed = 0;
        NearbyDictionary Plain() => new("beside.ytd", shared.Length, () => { unkeyed++; return shared; });
        new TextureLookup([], [Plain()]).Find("road_n");
        new TextureLookup([], [Plain()]).Find("road_n");
        Assert.Equal(2, unkeyed);
    }

    [Fact]
    public void A_dictionary_that_changes_on_disk_has_a_new_key_and_is_read_again()
    {
        string folder = Directory.CreateTempSubdirectory("hh-parsed-").FullName;
        try
        {
            string rpf = Path.Combine(folder, "x64a.rpf");
            File.WriteAllBytes(rpf, [1, 2, 3]);
            string? before = NearbyDictionary.KeyFor(rpf, "levels/gta5/generic/gtxd.rpf/globalroads.ytd");
            Assert.NotNull(before);
            Assert.Equal(before, NearbyDictionary.KeyForGameFile(folder, "x64a.rpf/levels/gta5/generic/gtxd.rpf/globalroads.ytd"));
            Assert.NotEqual(before, NearbyDictionary.KeyFor(rpf, "levels/gta5/generic/gtxd.rpf/vehshare.ytd"));
            // Written again (a mod saved into it): its size or time changes, and so does the key.
            File.WriteAllBytes(rpf, [1, 2, 3, 4]);
            Assert.NotEqual(before, NearbyDictionary.KeyFor(rpf, "levels/gta5/generic/gtxd.rpf/globalroads.ytd"));
            // A loose dictionary is named by its own file; one that isn't there has no key.
            File.WriteAllBytes(Path.Combine(folder, "loose.ytd"), [1]);
            Assert.NotNull(NearbyDictionary.KeyForGameFile(folder, "loose.ytd"));
            Assert.Null(NearbyDictionary.KeyFor(Path.Combine(folder, "gone.rpf"), "a.ytd"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void What_is_kept_fits_the_budget_the_least_recently_used_going_first()
    {
        long budget = ParsedDictionaries.Budget;
        // Room for two of these three-texture dictionaries (24 bytes each).
        ParsedDictionaries.Budget = 50;
        try
        {
            string a = Key(), b = Key(), c = Key();
            int parsed = 0;
            TextureDictionary Parse(string name) { parsed++; return FiveFury.Ytd.Ytd.Read(Ytd(name + "1", name + "2", name + "3")).Dictionary; }
            ParsedDictionaries.Get(a, () => Parse("a"));
            ParsedDictionaries.Get(b, () => Parse("b"));
            // a used again, so b is the least recently used when c comes.
            ParsedDictionaries.Get(a, () => Parse("a"));
            ParsedDictionaries.Get(c, () => Parse("c"));
            Assert.Equal(3, parsed);
            ParsedDictionaries.Get(a, () => Parse("a"));
            ParsedDictionaries.Get(c, () => Parse("c"));
            Assert.Equal(3, parsed);
            ParsedDictionaries.Get(b, () => Parse("b"));
            Assert.Equal(4, parsed);
            // Bigger than the whole budget, the one read last is still kept.
            ParsedDictionaries.Budget = 1;
            string big = Key();
            ParsedDictionaries.Get(big, () => Parse("big"));
            ParsedDictionaries.Get(big, () => Parse("big"));
            Assert.Equal(5, parsed);
        }
        finally
        {
            ParsedDictionaries.Budget = budget;
        }
    }

    [Fact]
    public async Task One_that_fails_to_parse_is_not_kept_and_two_asking_at_once_parse_once()
    {
        string broken = Key();
        Assert.Throws<InvalidDataException>(() => ParsedDictionaries.Get(broken, () => throw new InvalidDataException("not a ytd")));
        int retried = 0;
        ParsedDictionaries.Get(broken, () => { retried++; return new TextureDictionary(); });
        Assert.Equal(1, retried);

        string key = Key();
        int parsed = 0;
        using var gate = new ManualResetEventSlim();
        Task<TextureDictionary>[] asks = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() => ParsedDictionaries.Get(key, () =>
        {
            Interlocked.Increment(ref parsed);
            gate.Wait(TimeSpan.FromSeconds(5));
            return FiveFury.Ytd.Ytd.Read(Ytd("t")).Dictionary;
        })))];
        await Task.Delay(100);
        gate.Set();
        TextureDictionary[] got = await Task.WhenAll(asks);
        Assert.Equal(1, parsed);
        Assert.All(got, dictionary => Assert.Same(got[0], dictionary));
    }
}
