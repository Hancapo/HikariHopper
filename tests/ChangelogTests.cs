using HikariHopper.Core;

namespace HikariHopper.Tests;

public sealed class ChangelogTests
{
    [Fact]
    public void ReadsVersionsGroupsAndPlainEntries()
    {
        const string markdown = """
            # Changelog

            Intro text.

            ## [Unreleased]

            ### Added

            ### Fixed

            ## [0.0.2] - 2026-11-02

            ### Changed

            - Text uses `Avalonia`'s own rendering. ([#13](https://github.com/x/y/pull/13))
            - Releases are [native](https://example.com) builds
              and need no runtime. ([#14](https://github.com/x/y/pull/14), [#15](https://github.com/x/y/pull/15))

            ### Removed

            ## [0.0.1] - 2026-11-01

            ### Added

            - First release.

            [0.0.2]: https://github.com/x/y/compare/v0.0.1...v0.0.2
            """;
        IReadOnlyList<ChangelogRelease> releases = Changelog.Parse(markdown);

        // Unreleased with nothing in it, and empty groups, are left out.
        Assert.Equal(["0.0.2", "0.0.1"], releases.Select(release => release.Version));
        Assert.Equal("2026-11-02", releases[0].Date);
        ChangelogGroup changed = Assert.Single(releases[0].Groups);
        Assert.Equal("Changed", changed.Name);
        // No PR links, links as their text, code as text, a wrapped entry joined.
        Assert.Equal(["Text uses Avalonia's own rendering.", "Releases are native builds and need no runtime."], changed.Entries);
        Assert.Equal(["First release."], Assert.Single(releases[1].Groups).Entries);
    }

    [Fact]
    public void TheAppCarriesTheRepositorysOwnChangelog()
    {
        // The file built into the app is the repo's CHANGELOG.md, read the same way.
        string file = Path.Combine(RepositoryRoot(), "CHANGELOG.md");
        IReadOnlyList<ChangelogRelease> expected = Changelog.Parse(File.ReadAllText(file));
        IReadOnlyList<ChangelogRelease> built = Views.ChangelogDialog.Load();
        Assert.NotEmpty(built);
        Assert.Equal(expected.Select(release => release.Version), built.Select(release => release.Version));
        Assert.Contains(built, release => release.Version == "0.0.1");
        Assert.DoesNotContain(built.SelectMany(release => release.Groups).SelectMany(group => group.Entries), entry => entry.Contains("](http"));
    }

    /// <summary>The repository this test file is in: the tests run from a build folder elsewhere.</summary>
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(here))!;
}
