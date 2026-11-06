using FiveFury.Rpf;
using HikariHopper.Core.Search;

namespace HikariHopper.Tests;

/// <summary>The whole-game index: files on disk and inside RPFs, nested ones too, cached and refreshed.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class SearchIndexTests : ExplorerTestBase
{
    private string cache = "";

    private SearchIndex Index()
    {
        cache = Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Work, "..", "index-cache"))).FullName;
        SearchIndex.CacheFolder = cache;
        SearchIndex.ForgetAll();
        return SearchIndex.ForGame(Work, null);
    }

    private static IEnumerable<string> Paths(SearchIndex index, string text, string scope = "", bool? wildcards = null, bool regex = false, bool matchCase = false) =>
        index.State.Search(SearchPattern.Parse(text, matchCase, wildcards, regex, out _)!, scope, 1000).Select(hit => hit.Path).Order();

    private void Game()
    {
        MakeGame();
        Write(Path.Combine(Work, "mods", "readme.txt"), "r");
        Write(Path.Combine(Work, "update", "notes.txt"), "n");
        RpfArchive inner = Archive("v_street.rpf", ("prop_bench_01a.ydr", "b"), ("prop_bin_01a.ydr", "b"), ("textures/bench.ytd", "t"));
        SaveArchive(Path.Combine(Work, "x64i.rpf"), ("levels/gta5/props/v_street.rpf", inner), ("levels/gta5/readme.txt", "r"));
        SaveArchive(Path.Combine(Work, "update", "update.rpf"), ("common/data/carcols.ymt", "c"), ("common/data/handling.meta", "h"));
    }

    [AvaloniaFact]
    public async Task IndexesLooseFilesAndEverythingInsideRpfs()
    {
        Game();
        SearchIndex index = Index();
        await index.Refresh();
        Assert.True(index.State.Complete);
        Assert.Equal((2, 2), (index.State.ArchivesDone, index.State.ArchivesTotal));

        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/prop_bench_01a.ydr", "x64i.rpf/levels/gta5/props/v_street.rpf/prop_bin_01a.ydr"],
            Paths(index, "*.ydr"));
        Assert.Equal(["mods/readme.txt", "x64i.rpf/levels/gta5/readme.txt"], Paths(index, "readme"));
        // The RPFs are files too, on disk and nested.
        Assert.Equal(["update/update.rpf", "x64i.rpf", "x64i.rpf/levels/gta5/props/v_street.rpf"], Paths(index, ".rpf"));
        // In one folder and below, through an RPF as a folder.
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/textures/bench.ytd"], Paths(index, "bench", "x64i.rpf/levels/gta5/props/v_street.rpf/textures"));
        Assert.Equal(["update/update.rpf/common/data/carcols.ymt", "update/update.rpf/common/data/handling.meta"], Paths(index, "*", "update/update.rpf"));
    }

    [AvaloniaFact]
    public async Task ARefreshReadsOnlyTheRpfsThatChanged()
    {
        Game();
        SearchIndex first = Index();
        await first.Refresh();
        Assert.Equal(2, first.ArchivesRead);

        // A new session: the cache supplies both archives, and nothing is read.
        SearchIndex.ForgetAll();
        SearchIndex second = SearchIndex.ForGame(Work, null);
        await second.Refresh();
        Assert.Equal(0, second.ArchivesRead);
        Assert.Single(Paths(second, "carcols"));

        // One archive changes, a loose file appears and one goes: only that archive is read again.
        SaveArchive(Path.Combine(Work, "update", "update.rpf"), ("common/data/carvariations.meta", "v"));
        File.SetLastWriteTimeUtc(Path.Combine(Work, "update", "update.rpf"), DateTime.UtcNow.AddMinutes(1));
        Write(Path.Combine(Work, "mods", "carcols_backup.meta"), "b");
        File.Delete(Path.Combine(Work, "update", "notes.txt"));
        await second.Refresh();
        Assert.Equal(1, second.ArchivesRead);
        Assert.Equal(["mods/carcols_backup.meta"], Paths(second, "carcols"));
        Assert.Single(Paths(second, "carvariations"));
        Assert.Empty(Paths(second, "notes"));
    }

    [AvaloniaFact]
    public async Task AnRpfOutsideAGameIsIndexedOnItsOwn()
    {
        MakeGame();
        string addon = SaveArchive(Path.GetFullPath(Path.Combine(Work, "..", "downloads", "addon.rpf")), ("stream/car.yft", "c"), ("stream/car.ytd", "t"));
        Index();
        SearchIndex index = SearchIndex.ForArchive(addon, null);
        await index.Refresh();
        Assert.Equal(["addon.rpf/stream/car.yft", "addon.rpf/stream/car.ytd"], Paths(index, "car.*"));
    }

    [AvaloniaFact]
    public async Task AnRpfThatCantBeReadIsSkipped()
    {
        Game();
        Write(Path.Combine(Work, "broken.rpf"), "not an archive");
        SearchIndex index = Index();
        await index.Refresh();
        Assert.True(index.State.Complete);
        Assert.Equal(["broken.rpf"], Paths(index, "broken"));
        Assert.Single(Paths(index, "handling"));
    }
}

/// <summary>Reading a search's text: plain, wildcards (on by themselves with * or ?), or a regular expression.</summary>
public sealed class SearchPatternTests
{
    private static SearchPattern Pattern(string text, bool matchCase = false, bool? wildcards = null, bool regex = false) =>
        SearchPattern.Parse(text, matchCase, wildcards, regex, out _)!;

    [Fact]
    public void PlainTextIsFoundAnywhereInTheName()
    {
        SearchPattern pattern = Pattern(".ydr");
        Assert.Equal(SearchMode.Text, pattern.Mode);
        Assert.Equal([(14, 4)], pattern.Match("prop_bench_01a.ydr"));
        Assert.Equal([(14, 4)], pattern.Match("PROP_BENCH_01A.YDR"));
        Assert.Null(Pattern(".ydr", matchCase: true).Match("PROP_BENCH_01A.YDR"));
        Assert.Null(pattern.Match("prop_bench_01a.ytd"));
    }

    [Fact]
    public void AStarOrQuestionMarkTurnsWildcardsOnAndTheWholeNameMustMatch()
    {
        SearchPattern pattern = Pattern("*.ydr");
        Assert.Equal(SearchMode.Wildcards, pattern.Mode);
        // The literal part is what is marked.
        Assert.Equal([(14, 4)], pattern.Match("prop_bench_01a.ydr"));
        Assert.Null(pattern.Match("prop_bench_01a.ydr.bak"));
        Assert.Equal([(0, 5), (10, 3)], Pattern("prop_*_01?").Match("prop_bench_01a"));
        // Switched off, a * is just a character.
        Assert.Equal(SearchMode.Text, Pattern("*.ydr", wildcards: false).Mode);
        Assert.Null(Pattern("*.ydr", wildcards: false).Match("a.ydr"));
        // Switched on without any, the whole name must be the text.
        Assert.NotNull(Pattern("carcols.meta", wildcards: true).Match("CarCols.meta"));
        Assert.Null(Pattern("carcols.meta", wildcards: true).Match("carcols.meta.bak"));
    }

    [Fact]
    public void ARegularExpressionMatchesWhatItSays()
    {
        SearchPattern pattern = Pattern(@"^prop_(bench|bin)_0\d", matchCase: true, regex: true);
        Assert.Equal(SearchMode.Regex, pattern.Mode);
        Assert.Equal([(0, 13)], pattern.Match("prop_bench_01a.ydr"));
        Assert.NotNull(pattern.Match("prop_bin_02a.ydr"));
        Assert.Null(pattern.Match("PROP_BIN_02A.ydr"));
        Assert.Null(pattern.Match("prop_bollard_01a.ydr"));
    }

    [Fact]
    public void AnInvalidRegularExpressionSaysWhatIsWrong()
    {
        Assert.Null(SearchPattern.Parse(@"^prop_(bench|bin_0\d", false, null, true, out string error));
        Assert.StartsWith("Missing ) to close a group", error);
        Assert.Null(SearchPattern.Parse("[a-", false, null, true, out error));
        Assert.StartsWith("Missing ] to close a set", error);
        Assert.Null(SearchPattern.Parse("", false, null, false, out error));
        Assert.Equal("", error);
    }
}
