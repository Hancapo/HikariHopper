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

    public async Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancel)
    {
        progress(50);
        if (DownloadGate is not null)
            await DownloadGate.Task.WaitAsync(cancel);
        progress(100);
    }

    public void ApplyOnExit(AvailableUpdate update) => Applied = update;
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
    public async Task ADownloadCalledOffInstallsNothing()
    {
        source.Found = Version005;
        source.DownloadGate = new TaskCompletionSource();
        UpdateFlow updates = Open(out _);
        await Settled(updates.Checked);
        UpdateSheet sheet = updates.Sheet!;

        Click(sheet, "Update and restart");
        Pump();
        Click(sheet, "Cancel");
        await Settled(updates.Updating);
        Assert.Equal("available", sheet.State);

        // Closing the sheet during the download calls it off too.
        Click(sheet, "Update and restart");
        Pump();
        Assert.Equal("downloading", sheet.State);
        sheet.Close();
        await Settled(updates.Updating);

        Assert.Null(source.Applied);
        Assert.Equal(0, quits);
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

    private static void Pump() => ExplorerStateRenders.Pump(300);

    private static List<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "")];

    private static void Click(UpdateSheet sheet, string text)
    {
        var button = sheet.Footer!.GetSelfAndLogicalDescendants().OfType<Views.Controls.ChromeToolButton>()
            .Single(candidate => candidate.Text == text);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }
}
