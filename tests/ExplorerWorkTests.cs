using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>The explorer's long operations on the loading overlay: their progress, and what Cancel leaves.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ExplorerWorkTests : ExplorerTestBase
{
    // A game tab showing an empty game folder.
    private ExplorerViewModel OpenBareGame()
    {
        MakeGame();
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        File.Delete(Path.Combine(Work, "GTA5_Enhanced.exe"));
        bridge.Refresh();
        return bridge;
    }

    // Pumps the UI until the condition holds, or fails after a few seconds.
    private static void Until(Func<bool> condition, string what)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Timed out waiting for {what}");
            Dispatcher.UIThread.RunJobs();
        }
    }

    // The overlay over a tab, in a window, shown as soon as an operation starts.
    private static (Window Window, ExplorerWorkView View) Cover(ExplorerViewModel bridge)
    {
        ExplorerWorkView.RevealDelay = TimeSpan.Zero;
        var view = new ExplorerWorkView { DataContext = bridge };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        return (window, view);
    }

    [AvaloniaFact]
    public void AnImportIntoAFolderShowsItsFilesAndCancelUndoesIt()
    {
        ExplorerViewModel bridge = OpenBareGame();
        string[] drops = [.. new[] { "alpha.txt", "beta.txt", "gamma.txt" }.Select(name => Write(Path.Combine(Work, "..", "drops", name), name))];
        using var gate = new SemaphoreSlim(0);
        bridge.WorkStarting = _ => Assert.True(gate.Wait(TimeSpan.FromSeconds(10)));
        (Window window, ExplorerWorkView view) = Cover(bridge);
        try
        {
            Assert.True(bridge.ImportDroppedFiles(drops));
            Until(() => bridge.Work?.Count == "1/3", "the first file");
            Assert.True(view.IsVisible);
            Assert.Equal("IMPORTING", view.Overlay.Title);
            Assert.Equal("alpha.txt", view.Overlay.Detail);
            Assert.Equal("1/3", view.Overlay.ProgressText);
            gate.Release();
            Until(() => bridge.Work?.Count == "2/3", "the second file");
            Assert.Equal("beta.txt", view.Overlay.Detail);

            // Cancelled, the files it copied are taken back out.
            view.Overlay.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            gate.Release(5);
            Settle(bridge);
            Assert.Null(bridge.Work);
            Assert.False(view.IsVisible);
            Assert.False(bridge.EntryOperationBusy);
            Assert.All(new[] { "alpha.txt", "beta.txt", "gamma.txt" }, name => Assert.False(File.Exists(Path.Combine(Work, name))));
            Assert.Equal("Cancelled importing 3 files: nothing was changed", bridge.Status);
        }
        finally
        {
            window.Close();
            ExplorerWorkView.RevealDelay = TimeSpan.FromMilliseconds(400);
        }
    }

    [AvaloniaFact]
    public void ACancelDuringTheLastFileOfAnImportStillUndoesIt()
    {
        ExplorerViewModel bridge = OpenBareGame();
        string[] drops = [.. new[] { "alpha.txt", "beta.txt" }.Select(name => Write(Path.Combine(Work, "..", "drops", name), name))];
        using var gate = new SemaphoreSlim(0);
        bridge.WorkStarting = _ => Assert.True(gate.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(bridge.ImportDroppedFiles(drops));
        gate.Release();
        Until(() => bridge.Work?.Count == "2/2", "the last file");
        // Cancelled while the last file is in hand: it is copied, then everything is taken back out.
        Assert.True(bridge.CancelWork());
        gate.Release(5);
        Settle(bridge);
        Assert.All(new[] { "alpha.txt", "beta.txt" }, name => Assert.False(File.Exists(Path.Combine(Work, name))));
        Assert.Equal("Cancelled importing 2 files: nothing was changed", bridge.Status);
    }

    [AvaloniaFact]
    public void ACancelledImportThatReplacesFilesPutsTheOldOnesBack()
    {
        ExplorerViewModel bridge = OpenBareGame();
        Write(Path.Combine(Work, "alpha.txt"), "old alpha");
        Write(Path.Combine(Work, "beta.txt"), "old beta");
        bridge.Refresh();
        string[] drops = [.. new[] { "alpha.txt", "beta.txt", "gamma.txt" }.Select(name => Write(Path.Combine(Work, "..", "drops", name), "new " + name))];
        using var gate = new SemaphoreSlim(0);
        bridge.WorkStarting = _ => Assert.True(gate.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(bridge.ImportDroppedFiles(drops, ImportConflict.Replace));
        gate.Release();
        Until(() => bridge.Work?.Count == "2/3", "the second file");
        // Cancelled once alpha.txt and beta.txt are replaced: both are as they were, and nothing else is left.
        Assert.True(bridge.CancelWork());
        gate.Release(5);
        Settle(bridge);
        Assert.Equal("old alpha", File.ReadAllText(Path.Combine(Work, "alpha.txt")));
        Assert.Equal("old beta", File.ReadAllText(Path.Combine(Work, "beta.txt")));
        Assert.False(File.Exists(Path.Combine(Work, "gamma.txt")));
        Assert.Empty(Directory.GetFiles(Work, "*.hikari-*"));
        Assert.Equal("Cancelled importing 3 files: nothing was changed", bridge.Status);

        // Not cancelled, the new ones stay and the old ones' backups go.
        bridge.WorkStarting = null;
        Assert.True(bridge.ImportDroppedFiles(drops, ImportConflict.Replace));
        Settle(bridge);
        Assert.Equal("new alpha.txt", File.ReadAllText(Path.Combine(Work, "alpha.txt")));
        Assert.Equal("new gamma.txt", File.ReadAllText(Path.Combine(Work, "gamma.txt")));
        Assert.Empty(Directory.GetFiles(Work, "*.hikari-*"));
    }

    [AvaloniaFact]
    public async Task AnExtractShowsItsFilesAndCancelKeepsWhatItWrote()
    {
        MakeGame();
        string archive = SaveArchive(Path.Combine(Work, "mods.rpf"), ("one.txt", "1"), ("two.txt", "2"), ("three.txt", "3"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(archive);
        bridge.SelectAllEntries();
        string output = Path.GetFullPath(Path.Combine(Work, "..", "out-cancel"));
        Dialogs.Folders.Enqueue(output);
        using var gate = new SemaphoreSlim(0);
        bridge.WorkStarting = _ => Assert.True(gate.Wait(TimeSpan.FromSeconds(10)));

        Assert.True(await bridge.ExtractSelection());
        Until(() => bridge.Work?.Count == "1/3", "the first file");
        Assert.Equal("EXTRACTING", bridge.Work!.Title);
        Assert.True(bridge.Work.Cancellable);
        gate.Release();
        Until(() => bridge.Work?.Count == "2/3", "the second file");
        Assert.True(bridge.CancelWork());
        Assert.False(bridge.CancelWork());
        gate.Release(5);
        Settle(bridge);
        // The file in hand is finished; the third is never started.
        Assert.Equal(2, Directory.GetFiles(output).Length);
        Assert.Equal("Cancelled extracting 3 items: 2 of 3 files were written", bridge.Status);
        Assert.Null(bridge.Work);
    }

    [AvaloniaFact]
    public void ARewriteOfAnRpfShowsItsShareAndCancelLeavesTheArchiveAsItWas()
    {
        MakeGame();
        string archive = SaveArchive(Path.Combine(Work, "outer.rpf"), ("mods/keep.txt", "keep"));
        byte[] before = File.ReadAllBytes(archive);
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(archive);
        bridge.Navigate("mods");
        using var gate = new SemaphoreSlim(0);
        bridge.WorkStarting = _ => Assert.True(gate.Wait(TimeSpan.FromSeconds(10)));

        Assert.True(bridge.CreateFolder("stream"));
        Until(() => bridge.Work?.Percent == true, "the rewrite");
        Assert.Equal("CREATING", bridge.Work!.Title);
        Assert.Equal("outer.rpf", bridge.Work.Current);
        Assert.Equal("0%", bridge.Work.Count);
        Assert.True(bridge.CancelWork());
        gate.Release(5);
        Settle(bridge);
        Assert.Equal(before, File.ReadAllBytes(archive));
        Assert.Empty(Directory.GetFiles(Work, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal("Cancelled creating stream: nothing was changed", bridge.Status);
        Assert.Equal(["keep.txt"], ListedNames(bridge));
    }

    [AvaloniaFact]
    public void A_tracked_write_reports_its_share_and_stops_when_asked()
    {
        using var cancel = new CancellationTokenSource();
        var steps = new List<OperationStep>();
        var progress = new OperationProgress(cancel.Token, steps.Add);
        using var sink = new MemoryStream();
        Stream tracked = progress.Track(sink, 100, "outer.rpf");
        tracked.Write(new byte[40]);
        Assert.Equal(new OperationStep(0, 1000, "outer.rpf", Percent: true), steps[0]);
        // Never all of it while the write goes on: the result may come out larger than expected.
        tracked.Write(new byte[200]);
        Assert.Equal(990, steps[^1].Done);
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => tracked.Write(new byte[1]));
        Assert.Equal(240, sink.Length);
        // Anywhere else, the work reports nowhere and never stops.
        Assert.Same(sink, OperationProgress.None.Track(sink, 100, "x"));
        OperationProgress.None.Check();
    }

    [AvaloniaFact]
    public void A_status_text_gives_the_overlay_its_title_and_detail()
    {
        Assert.Equal(("IMPORTING", "3 files"), ExplorerViewModel.WorkLabel("Importing 3 files…"));
        Assert.Equal(("MOVING", "2 items to the Recycle Bin"), ExplorerViewModel.WorkLabel("Moving 2 items to the Recycle Bin…"));
        Assert.Equal(("EXTRACTING", "mods.rpf"), ExplorerViewModel.WorkLabel("Extracting mods.rpf…"));
        Assert.Equal(new WorkProgress("X", 450, 1000, "", true, Percent: true).Count, "45%");
        Assert.Equal(new WorkProgress("X", 2, 3, "", true).Count, "3/3");
        Assert.Null(new WorkProgress("X", 0, 0, "", false).Count);
    }
}
