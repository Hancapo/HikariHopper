using HikariHopper.Core;
using Velopack;
using Velopack.Sources;

namespace HikariHopper.Infrastructure;

/// <summary>A newer version of the app: its version, its changelog section, and how much has to be downloaded.</summary>
/// <param name="Native">What the update source needs to download and apply it.</param>
internal sealed record AvailableUpdate(string Version, string Notes, long Size, object? Native = null);

/// <summary>Where updates come from: the app's GitHub releases, or a fake in tests.</summary>
internal interface IUpdateSource
{
    /// <summary>Whether this copy can update itself: a release build, unpacked from its zip.</summary>
    bool CanUpdate { get; }

    /// <summary>The newest version if it is newer than this one; null when this one is the newest.</summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancel);

    /// <summary>Downloads <paramref name="update"/>, reporting 0 to 100 as it goes, on any thread.</summary>
    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancel);

    /// <summary>Installs a downloaded <paramref name="update"/> once the app has quit, then starts it again.</summary>
    void ApplyOnExit(AvailableUpdate update);
}

/// <summary>The app's updates and their two settings: whether to check at startup, and the version to skip.</summary>
internal static class Updates
{
    public const string CheckOnStartupKey = "updates/checkOnStartup";
    public const string SkippedVersionKey = "updates/skippedVersion";

    /// <summary>Where updates come from; tests replace it, so they never reach GitHub.</summary>
    public static IUpdateSource Source { get; set; } = new GitHubUpdateSource();

    /// <summary>Whether the app looks for a new version when it starts; on by default.</summary>
    public static bool CheckOnStartup
    {
        get => AppSettings.GetInt(CheckOnStartupKey, 1) != 0;
        set => AppSettings.SetInt(CheckOnStartupKey, value ? 1 : 0);
    }

    /// <summary>The version the user chose to skip: the startup check doesn't offer it.</summary>
    public static string SkippedVersion
    {
        get => AppSettings.GetString(SkippedVersionKey);
        set => AppSettings.SetString(SkippedVersionKey, value);
    }

    /// <summary>
    /// The version's changelog section, as the update's notes carry it (<c>## [0.0.5] - date</c> and its groups);
    /// notes without that heading are read as the section's groups.
    /// </summary>
    public static ChangelogRelease Release(AvailableUpdate update)
    {
        if (Changelog.Parse(update.Notes) is [var release, ..])
            return release;
        return Changelog.Parse($"## [{update.Version}]\n\n{update.Notes}") is [var bare, ..]
            ? bare
            : new ChangelogRelease(update.Version, "", []);
    }
}

/// <summary>Updates from the repository's GitHub releases, through Velopack.</summary>
internal sealed class GitHubUpdateSource : IUpdateSource
{
    public const string Repository = "https://github.com/Hancapo/HikariHopper";

    private UpdateManager? manager;

    private UpdateManager Manager => manager ??= new UpdateManager(new GithubSource(Repository, null, false));

    // Dev builds never update, even one packed by hand: they would be replaced by the last release.
    public bool CanUpdate => BuildInfo.IsRelease && Manager.IsInstalled;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancel)
    {
        UpdateInfo? info = await Manager.CheckForUpdatesAsync().WaitAsync(cancel).ConfigureAwait(false);
        if (info is null)
            return null;
        VelopackAsset target = info.TargetFullRelease;
        // A copy that has updated before keeps the package it came from, and patches it with the deltas.
        long size = info.DeltasToTarget.Length > 0 ? info.DeltasToTarget.Sum(delta => delta.Size) : target.Size;
        return new AvailableUpdate(target.Version.ToString(), target.NotesMarkdown ?? "", size, info);
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancel) =>
        Manager.DownloadUpdatesAsync((UpdateInfo)update.Native!, progress, cancel);

    // Silent: Velopack's own windows are never shown; a failed install leaves the old version, which starts again.
    public void ApplyOnExit(AvailableUpdate update) =>
        Manager.WaitExitThenApplyUpdates(((UpdateInfo)update.Native!).TargetFullRelease, silent: true, restart: true);
}
