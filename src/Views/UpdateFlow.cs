using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HikariHopper.Infrastructure;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Updating the app, from the check to the restart: quietly when it starts, and by hand from Help. A new version
/// is offered in an <see cref="UpdateSheet"/>; updating downloads it there, then quits the app so it is installed,
/// and starts it again.
/// </summary>
internal sealed class UpdateFlow
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    private readonly Control anchor;
    private readonly Func<ExplorerTabsViewModel?> tabs;
    private UpdateSheet? sheet;
    private AvailableUpdate? offered;
    private CancellationTokenSource? download;

    /// <param name="anchor">What the sheet is shown over.</param>
    /// <param name="tabs">The tabs whose unsaved edits and writes hold the restart back.</param>
    public UpdateFlow(Control anchor, Func<ExplorerTabsViewModel?> tabs)
    {
        this.anchor = anchor;
        this.tabs = tabs;
    }

    /// <summary>How the app quits once an update is downloaded; tests replace it.</summary>
    internal static Action Quit { get; set; } =
        () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    /// <summary>The sheet, once it has been shown.</summary>
    internal UpdateSheet? Sheet => sheet;

    /// <summary>The last check, done; for tests.</summary>
    internal Task Checked { get; private set; } = Task.CompletedTask;

    /// <summary>The last update started from the sheet, downloaded (or cancelled, or failed); for tests.</summary>
    internal Task Updating { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The startup check: when it is on and this copy can update, a version newer than this one, and not the one
    /// skipped, is offered. Nothing shows otherwise, not even a failed check.
    /// </summary>
    public Task CheckAtStartup() => Checked = AtStartup();

    /// <summary>
    /// A check by hand, from Help or Settings: the sheet shows the check under way, and then what it found. It is
    /// shown over <paramref name="over"/>, a sheet already up, or else over the window.
    /// </summary>
    public Task CheckByHand(Control? over = null) => Checked = ByHand(over ?? anchor);

    private async Task AtStartup()
    {
        if (!Updates.CheckOnStartup || !Updates.Source.CanUpdate)
            return;
        AvailableUpdate? update;
        try
        {
            update = await Check();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return;
        }
        // A sheet already up, from Help, has the check of its own.
        if (update is null || update.Version == Updates.SkippedVersion || sheet?.IsOpen == true)
            return;
        Offer(update);
        MakeSheet().Present(anchor);
    }

    private async Task ByHand(Control over)
    {
        UpdateSheet shown = MakeSheet();
        // A download under way is shown as it is.
        if (download is not null)
        {
            shown.Present(over);
            return;
        }
        if (!Updates.Source.CanUpdate)
        {
            shown.ShowUnavailable();
            shown.Present(over);
            return;
        }
        shown.ShowChecking();
        shown.Present(over);
        AvailableUpdate? update;
        try
        {
            update = await Check();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            shown.ShowFailed();
            return;
        }
        if (update is null)
            shown.ShowUpToDate();
        else
            Offer(update);
    }

    private static async Task<AvailableUpdate?> Check()
    {
        using var cancel = new CancellationTokenSource(CheckTimeout);
        return await Updates.Source.CheckAsync(cancel.Token);
    }

    private UpdateSheet MakeSheet()
    {
        if (sheet is not null)
            return sheet;
        sheet = new UpdateSheet(StartPage.AppVersion);
        sheet.UpdateRequested += (_, _) => Updating = Update();
        sheet.SkipRequested += (_, _) =>
        {
            if (offered is not null)
                Updates.SkippedVersion = offered.Version;
            sheet.Close();
        };
        sheet.CancelRequested += (_, _) => download?.Cancel();
        // Closed during the download, the update is called off: it would otherwise restart the app unasked.
        sheet.Closed += (_, _) => download?.Cancel();
        return sheet;
    }

    private void Offer(AvailableUpdate update, string? note = null)
    {
        offered = update;
        MakeSheet().ShowAvailable(update.Version, Updates.Release(update), update.Size, note);
    }

    private async Task Update()
    {
        if (offered is not { } update || download is not null)
            return;
        if (Blocker() is { } blocker)
        {
            Offer(update, blocker);
            return;
        }
        download = new CancellationTokenSource();
        UpdateSheet shown = MakeSheet();
        shown.ShowProgress(0, update.Size);
        try
        {
            await Updates.Source.DownloadAsync(update,
                percent => Dispatcher.UIThread.Post(() =>
                {
                    if (shown.State == "downloading")
                        shown.ShowProgress(percent / 100.0, update.Size);
                }),
                download.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            bool cancelled = download.IsCancellationRequested;
            download.Dispose();
            download = null;
            Offer(update, cancelled ? null : "The download failed");
            return;
        }
        bool called = download.IsCancellationRequested;
        download.Dispose();
        download = null;
        if (called)
        {
            Offer(update);
            return;
        }
        // Something started while it downloaded (an archive write, say) holds the restart back too.
        if (Blocker() is { } late)
        {
            Offer(update, late);
            return;
        }
        try
        {
            Updates.Source.ApplyOnExit(update);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The updater didn't start (Update.exe missing, say): the app stays, and the sheet offers the update again.
            Offer(update, "The updater couldn't start");
            return;
        }
        Quit();
    }

    /// <summary>Why the app can't quit for the update yet: an unsaved texture edit, or a write under way.</summary>
    internal string? Blocker()
    {
        if (tabs() is not { } open)
            return null;
        foreach (ExplorerViewModel tab in open.Tabs)
        {
            if (tab.EntryOperationBusy || tab.TextureViewer.OperationBusy)
                return "Wait for the changes being saved to finish";
            if (tab.TextureViewer.Modified)
                return "Save or discard your texture edits first";
        }
        return null;
    }
}
