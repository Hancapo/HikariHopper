using Avalonia.Threading;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

// The long operation the tab is running (an import, an extract, a delete, any write into an RPF), as its loading
// overlay shows it, and asking it to stop.
public sealed partial class ExplorerViewModel
{
    private WorkProgress? work;
    private CancellationTokenSource? workCancel;
    private int workSession;

    /// <summary>
    /// The operation the tab's queue is running: what it is, how far it has got and whether it can stop; none when
    /// nothing runs (or only a game load, which has its own view).
    /// </summary>
    public WorkProgress? Work => work;

    /// <summary>Told as each file of an operation starts, and as an RPF's rewrite starts; tests hold it there.</summary>
    internal Action<string>? WorkStarting { get; set; }

    /// <summary>
    /// Asks the running operation to stop at its next file, or before an RPF it is rewriting is swapped in; what a
    /// stopped operation leaves, the status says. False when nothing runs, it can't stop, or it was asked already.
    /// </summary>
    public bool CancelWork()
    {
        if (work is not { Cancellable: true } || workCancel is null || workCancel.IsCancellationRequested)
            return false;
        workCancel.Cancel();
        SetStatus("Cancelling…");
        return true;
    }

    /// <summary>
    /// Starts showing an operation from its status text ("Importing 3 files…": IMPORTING over "3 files"), and gives
    /// the progress its work reports through.
    /// </summary>
    private OperationProgress BeginWork(string label, bool cancellable)
    {
        (string title, string detail) = WorkLabel(label);
        var cancel = workCancel = new CancellationTokenSource();
        int session = ++workSession;
        var latest = new LatestStep(step => WorkReached(session, step));
        var progress = new OperationProgress(cancel.Token, latest.Push, WorkStarting);
        work = new WorkProgress(title, 0, 0, detail, cancellable);
        OnPropertyChanged(nameof(Work));
        return progress;
    }

    private void WorkReached(int session, OperationStep step)
    {
        if (session != workSession || work is null)
            return;
        work = work with { Done = step.Done, Total = step.Total, Current = step.Current, Percent = step.Percent };
        OnPropertyChanged(nameof(Work));
    }

    /// <summary>Stops showing the operation, however it ended.</summary>
    private void EndWork()
    {
        workSession++;
        workCancel = null;
        if (work is null)
            return;
        work = null;
        OnPropertyChanged(nameof(Work));
    }

    /// <summary>
    /// Hands an operation's reports to the UI thread without flooding it: one post at a time, carrying the latest report
    /// when it runs, so thousands of small files cost a few updates and the last one is never lost.
    /// </summary>
    private sealed class LatestStep(Action<OperationStep> show)
    {
        private readonly Lock gate = new();
        private OperationStep latest;
        private bool posted;

        public void Push(OperationStep step)
        {
            lock (gate)
            {
                latest = step;
                if (posted)
                    return;
                posted = true;
            }
            Dispatcher.UIThread.Post(() =>
            {
                OperationStep now;
                lock (gate)
                {
                    now = latest;
                    posted = false;
                }
                show(now);
            });
        }
    }

    /// <summary>A status text's verb as the overlay's title, and the rest as what it is working on.</summary>
    internal static (string Title, string Detail) WorkLabel(string label)
    {
        string text = label.TrimEnd('…', '.', ' ');
        int space = text.IndexOf(' ');
        return space < 0 ? (text.ToUpperInvariant(), "") : (text[..space].ToUpperInvariant(), text[(space + 1)..]);
    }

    /// <summary>What a cancelled operation says: "Cancelled importing 3 files: nothing was changed".</summary>
    private static string CancelledStatus(string label, string outcome)
    {
        string text = label.TrimEnd('…', '.', ' ');
        return $"Cancelled {(text.Length > 0 ? char.ToLowerInvariant(text[0]) + text[1..] : text)}: {outcome}";
    }
}
