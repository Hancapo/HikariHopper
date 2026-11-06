using System.Net.Http;
using HikariHopper.Core;

namespace HikariHopper.Infrastructure;

/// <summary>
/// Where the changelog the app shows comes from: the repository's <c>CHANGELOG.md</c>, so wording fixed after a
/// release shows in the builds already out; the copy last read from it when it can't be reached; and the one built
/// into the app when it never has been.
/// </summary>
internal static class ChangelogSource
{
    /// <summary>The repository's changelog, as it is on master.</summary>
    public static readonly Uri Repository = new("https://raw.githubusercontent.com/Hancapo/HikariHopper/master/CHANGELOG.md");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);
    private static Task<string?>? latest;

    /// <summary>Where the last copy read from the repository is kept; tests point it elsewhere.</summary>
    public static string CachePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariHopper", "CHANGELOG.md");

    /// <summary>How the repository's copy is read; tests replace it, so they never reach the network.</summary>
    public static Func<CancellationToken, Task<string?>> Download { get; set; } = DownloadAsync;

    /// <summary>The last copy read from the repository, or null if there is none.</summary>
    public static string? Cached()
    {
        try
        {
            return File.Exists(CachePath) ? File.ReadAllText(CachePath) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The repository's changelog, read once per run and kept for the next one; null when it can't be reached or
    /// what comes back isn't a changelog.
    /// </summary>
    public static Task<string?> LatestAsync() => latest ??= ReadAndKeep();

    /// <summary>Forgets this run's read, so the next one asks again (for tests).</summary>
    internal static void Forget() => latest = null;

    /// <summary>
    /// The newer of two changelogs, by the latest version each has released: the one read from the repository
    /// unless the app was built from a newer one, as after an update made while offline.
    /// </summary>
    public static string Newer(string? cached, string builtIn)
    {
        if (cached is null || Changelog.Parse(cached).Count == 0)
            return builtIn;
        return LatestVersion(cached) >= LatestVersion(builtIn) ? cached : builtIn;
    }

    private static Version LatestVersion(string markdown) =>
        Changelog.Parse(markdown).Select(release => Version.TryParse(release.Version, out Version? version) ? version : null)
            .OfType<Version>().DefaultIfEmpty(new Version(0, 0)).Max()!;

    private static async Task<string?> ReadAndKeep()
    {
        string? text;
        try
        {
            using var cancel = new CancellationTokenSource(Timeout);
            text = await Download(cancel.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
        // Something that isn't a changelog (a proxy's sign-in page, say) is neither shown nor kept.
        if (text is null || Changelog.Parse(text).Count == 0)
            return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            await File.WriteAllTextAsync(CachePath, text).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        return text;
    }

    private static async Task<string?> DownloadAsync(CancellationToken cancel)
    {
        using var client = new HttpClient();
        string version = typeof(ChangelogSource).Assembly.GetName().Version?.ToString(3) ?? "0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"HikariHopper/{version}");
        using HttpResponseMessage response = await client.GetAsync(Repository, cancel).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false) : null;
    }
}
