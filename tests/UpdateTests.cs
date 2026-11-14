using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HikariHopper.Infrastructure;
using HikariHopper.Tests.Textures;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>A source of updates the tests decide: what a check finds, and how a download goes.</summary>
internal sealed class FakeUpdateSource : IUpdateSource
{
    public bool CanUpdate { get; set; } = true;

    /// <summary>What a check finds; null for none.</summary>
    public AvailableUpdate? Found { get; set; }

    /// <summary>Thrown by a check, as when GitHub can't be reached.</summary>
    public Exception? CheckError { get; set; }

    /// <summary>Holds the download open until set, when given; it honours cancellation.</summary>
    public TaskCompletionSource? DownloadGate { get; set; }

    public int Checks { get; private set; }

    public AvailableUpdate? Applied { get; private set; }

    public Task<AvailableUpdate?> CheckAsync(CancellationToken cancel)
    {
        Checks++;
        return CheckError is null ? Task.FromResult(Found) : Task.FromException<AvailableUpdate?>(CheckError);
    }

    /// <summary>Thrown by a download, as when the connection drops.</summary>
    public Exception? DownloadError { get; set; }

    public async Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancel)
    {
        if (DownloadError is not null)
            throw DownloadError;
        progress(50);
        if (DownloadGate is not null)
            await DownloadGate.Task.WaitAsync(cancel);
        progress(100);
    }

    /// <summary>Thrown when the updater is started, as when Update.exe is missing.</summary>
    public Exception? ApplyError { get; set; }

    public void ApplyOnExit(AvailableUpdate update)
    {
        if (ApplyError is not null)
            throw ApplyError;
        Applied = update;
    }

    private int cleanUps;

    /// <summary>How many times what updating left behind was cleaned up.</summary>
    public int CleanUps => Volatile.Read(ref cleanUps);

    public void CleanUp() => Interlocked.Increment(ref cleanUps);
}

[Collection(ExplorerCollection.Name)]
public sealed class UpdateTests : ExplorerTestBase, IDisposable
{
    private const string Notes = """
        ## [0.0.5] - 2026-11-20

        ### Added

        - HikariHopper updates itself.
        """;

    private readonly IUpdateSource previousSource = Updates.Source;
    private readonly Action previousQuit = UpdateFlow.Quit;
    private readonly FakeUpdateSource source = new();
    private int quits;

    public UpdateTests()
    {
        Updates.Source = source;
        UpdateFlow.Quit = () => quits++;
    }

    public new void Dispose()
    {
        Updates.Source = previousSource;
        UpdateFlow.Quit = previousQuit;
        base.Dispose();
    }

    private static AvailableUpdate Version005 => new("0.0.5", Notes, 2_310_000);

    [AvaloniaFact]
    public async Task ANewVersionIsOfferedWhenTheAppStarts()
    {
        source.Found = Version005;
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);

        UpdateSheet sheet = updates.Sheet!;
        Assert.True(sheet.IsOpen);
        Assert.Equal("available", sheet.State);
        Assert.Equal("0.0.5", sheet.Offered);
        Assert.Equal("Released 2026-11-20 · 2.2 MB download", sheet.Detail);
        Assert.Contains("HikariHopper updates itself.", Texts(sheet));
    }

    [AvaloniaFact]
    public async Task TheStartupCheckStaysQuietUnlessThereIsAVersionToOffer()
    {
        // Turned off in Settings: no check at all.
        Updates.CheckOnStartup = false;
        source.Found = Version005;
        UpdateFlow off = Open(out Window first);
        await Settled(off.Checked);
        Assert.Equal(0, source.Checks);
        Assert.Null(off.Sheet);
        // What the last update left behind is cleaned up all the same.
        await Settled(off.CleanedUp);
        Assert.Equal(1, source.CleanUps);
        first.Close();

        // The version skipped, none newer, a failed check, or a copy that can't update: nothing shown.
        Updates.CheckOnStartup = true;
        Updates.SkippedVersion = "0.0.5";
        foreach (Action arrange in new Action[]
        {
            () => { },
            () => { Updates.SkippedVersion = ""; source.Found = null; },
            () => { source.Found = Version005; source.CheckError = new HttpRequestException("offline"); },
            () => { source.CheckError = null; source.CanUpdate = false; },
        })
        {
            arrange();
            UpdateFlow quiet = Open(out Window window);
            await Settled(quiet.Checked);
            Assert.Null(quiet.Sheet);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CheckingByHandSaysWhatItFound()
    {
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);

        await Settled(updates.CheckByHand());
        Assert.True(updates.Sheet!.IsOpen);
        Assert.Equal("up to date", updates.Sheet.State);
        Assert.Contains("Up to date", Texts(updates.Sheet));
        Assert.Contains(StartPage.AppVersion, Texts(updates.Sheet));

        source.CheckError = new HttpRequestException("offline");
        await Settled(updates.CheckByHand());
        Assert.Equal("failed", updates.Sheet.State);

        // By hand, even a skipped version is offered.
        source.CheckError = null;
        source.Found = Version005;
        Updates.SkippedVersion = "0.0.5";
        await Settled(updates.CheckByHand());
        Assert.Equal("available", updates.Sheet.State);

        source.CanUpdate = false;
        await Settled(updates.CheckByHand());
        Assert.Equal("unavailable", updates.Sheet.State);
        Assert.Equal("Only release builds update themselves", updates.Sheet.Detail);
    }

    [AvaloniaFact]
    public async Task SkippingAVersionKeepsItFromBeingOfferedAgain()
    {
        source.Found = Version005;
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);
        Click(updates.Sheet!, "Skip this version");
        Pump();
        Assert.False(updates.Sheet!.IsOpen);
        Assert.Equal("0.0.5", Updates.SkippedVersion);

        await Settled(updates.CheckAtStartup());
        Assert.False(updates.Sheet.IsOpen);
    }

    [AvaloniaFact]
    public async Task UpdatingDownloadsTheVersionAndRestartsTheAppToInstallIt()
    {
        source.Found = Version005;
        source.DownloadGate = new TaskCompletionSource();
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);
        UpdateSheet sheet = updates.Sheet!;

        Click(sheet, "Update and restart");
        Pump();
        Assert.Equal("downloading", sheet.State);
        Assert.Equal("Downloading · 50% of 2.2 MB", sheet.Detail);
        Assert.Null(source.Applied);

        source.DownloadGate.SetResult();
        await Settled(updates.Updating);
        Assert.Same(source.Found, source.Applied);
        Assert.Equal(1, quits);
    }

    [AvaloniaFact]
    public async Task AnUpdaterThatCantStartLeavesTheAppRunningAndTheUpdateOffered()
    {
        source.Found = Version005;
        source.ApplyError = new FileNotFoundException("Update.exe");
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);

        Click(updates.Sheet!, "Update and restart");
        await Settled(updates.Updating);
        Assert.Equal("available", updates.Sheet!.State);
        Assert.Equal("The updater couldn't start", updates.Sheet.Detail);
        Assert.Equal(0, quits);

        // It can be tried again.
        source.ApplyError = null;
        Click(updates.Sheet, "Update and restart");
        await Settled(updates.Updating);
        Assert.Same(source.Found, source.Applied);
        Assert.Equal(1, quits);
    }

    [AvaloniaFact]
    public async Task ADownloadCalledOffInstallsNothing()
    {
        source.Found = Version005;
        source.DownloadGate = new TaskCompletionSource();
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);
        UpdateSheet sheet = updates.Sheet!;

        await Settled(updates.CleanedUp);
        int cleanUps = source.CleanUps;
        Click(sheet, "Update and restart");
        Pump();
        Click(sheet, "Cancel");
        await Settled(updates.Updating);
        Assert.Equal("available", sheet.State);
        // What it had downloaded is cleaned up.
        await Settled(updates.CleanedUp);
        Assert.Equal(cleanUps + 1, source.CleanUps);

        // Closing the sheet during the download calls it off too.
        Click(sheet, "Update and restart");
        Pump();
        Assert.Equal("downloading", sheet.State);
        sheet.Close();
        await Settled(updates.Updating);
        await Settled(updates.CleanedUp);
        Assert.Equal(cleanUps + 2, source.CleanUps);

        Assert.Null(source.Applied);
        Assert.Equal(0, quits);
    }

    [AvaloniaFact]
    public async Task AFailedDownloadCleansUpWhatItLeft()
    {
        source.Found = Version005;
        source.DownloadError = new HttpRequestException("offline");
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);
        await Settled(updates.CleanedUp);
        int cleanUps = source.CleanUps;

        Click(updates.Sheet!, "Update and restart");
        await Settled(updates.Updating);
        Assert.Equal("The download failed", updates.Sheet!.Detail);
        await Settled(updates.CleanedUp);
        Assert.Equal(cleanUps + 1, source.CleanUps);
    }

    // An updated copy's packages folder: the package it was updated to, an older one, a download cut short, Velopack's
    // work folder with what it unpacked, and the files Velopack keeps there.
    private string Packages()
    {
        string packages = Directory.CreateDirectory(Path.Combine(Work, "app", "packages")).FullName;
        foreach (string name in new[] { "HikariHopper-0.2.1-full.nupkg", "HikariHopper-0.2.0-full.nupkg", "HikariHopper-0.2.2-delta.nupkg.partial", ".betaId", ".velopack_lock" })
            File.WriteAllText(Path.Combine(packages, name), name);
        string unpacked = Directory.CreateDirectory(Path.Combine(packages, "VelopackTemp", "base", "lib")).FullName;
        File.WriteAllText(Path.Combine(unpacked, "HikariHopper.exe"), "exe");
        return packages;
    }

    private static string[] Left(string packages) =>
        [.. Directory.EnumerateFileSystemEntries(packages).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)!];

    [Fact]
    public void UpdatingLeavesOnlyThePackageTheNextUpdateIsBuiltFrom()
    {
        string packages = Packages();
        Assert.True(Updates.RemoveLeftovers(packages, Path.Combine(packages, "VelopackTemp"), "HikariHopper-0.2.1-full.nupkg"));
        Assert.Equal([".betaId", ".velopack_lock", "HikariHopper-0.2.1-full.nupkg"], Left(packages));

        // Nothing there to clean: nothing happens.
        Assert.True(Updates.RemoveLeftovers(packages, Path.Combine(packages, "VelopackTemp"), "HikariHopper-0.2.1-full.nupkg"));
        Assert.True(Updates.RemoveLeftovers(Path.Combine(Work, "none"), null, null));
    }

    [Fact]
    public void NothingIsCleanedWhileAnotherCopyUpdatesNorOutsideThePackagesFolder()
    {
        // Another copy downloading holds the lock file open: everything stays.
        string packages = Packages();
        string[] before = Left(packages);
        using (new FileStream(Path.Combine(packages, ".velopack_lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Assert.False(Updates.RemoveLeftovers(packages, Path.Combine(packages, "VelopackTemp"), "HikariHopper-0.2.1-full.nupkg"));
        Assert.Equal(before, Left(packages));

        // A work folder outside it, as a wrong path would name, isn't touched.
        string outside = Directory.CreateDirectory(Path.Combine(Work, "elsewhere")).FullName;
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        Assert.True(Updates.RemoveLeftovers(packages, outside, "HikariHopper-0.2.1-full.nupkg"));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [AvaloniaFact]
    public async Task UnsavedTextureEditsHoldTheRestartBack()
    {
        source.Found = Version005;
        UpdateFlow updates = Open(out _, out ExplorerSession session);
        await Settled(updates.Checked);
        var viewer = session.Tabs.ActiveBridge.TextureViewer;
        viewer.DictionaryLoaded(viewer.Generation, TextureTestKit.Dictionary("red", "blue"));
        TextureTestKit.Settle(viewer);
        Assert.True(viewer.RenameSelected("green"));
        TextureTestKit.Settle(viewer);
        Assert.True(viewer.Modified);

        Click(updates.Sheet!, "Update and restart");
        await Settled(updates.Updating);
        Assert.Equal("available", updates.Sheet!.State);
        Assert.Equal("Save or discard your texture edits first", updates.Sheet.Detail);
        Assert.Null(source.Applied);
        Assert.Equal(0, quits);
    }

    private static UpdateFlow Open(out Window window) => Open(out window, out _);

    private static UpdateFlow Open(out Window window, out ExplorerSession session)
    {
        session = new ExplorerSession();
        var main = new MainWindow { Width = 1100, Height = 700 };
        session.Attach(main);
        main.Show();
        Pump();
        window = main;
        return ((ExplorerScreen)main.ExplorerScreenHost.Content!).Menu.Updates;
    }

    private static async Task Settled(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        await task;
        Pump();
    }

    private static void Pump() => TestWait.Settle();

    private static List<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "")];

    private static void Click(UpdateSheet sheet, string text)
    {
        var button = sheet.Footer!.GetSelfAndLogicalDescendants().OfType<Views.Controls.ChromeToolButton>()
            .Single(candidate => candidate.Text == text);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }
}
