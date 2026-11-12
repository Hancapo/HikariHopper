namespace HikariHopper.ViewModels;

// The long task the editor is running, as its loading overlay shows it, and asking it to stop.
public sealed partial class TextureViewerViewModel
{
    private WorkProgress? work;
    private CancellationTokenSource? workCancel;

    /// <summary>
    /// The long task running now (an import, a transform of the selection, an extract or a save): what it is, how far
    /// it has got and whether it can stop; none when no such task runs.
    /// </summary>
    public WorkProgress? Work => work;

    /// <summary>
    /// Asks the running task to stop once the item in hand is done; what a stopped task leaves is its own to say. False
    /// when nothing runs, the task can't stop, or it has been asked already.
    /// </summary>
    public bool CancelWork()
    {
        if (work is not { Cancellable: true } || workCancel is null || workCancel.IsCancellationRequested)
            return false;
        workCancel.Cancel();
        SetStatus("Cancelling…");
        return true;
    }

    /// <summary>Starts showing a task, and gives the token it checks between items.</summary>
    private CancellationToken BeginWork(string title, int total, string current, bool cancellable)
    {
        workCancel = new CancellationTokenSource();
        work = new WorkProgress(title, 0, total, current, cancellable);
        OnPropertyChanged(nameof(Work));
        return workCancel.Token;
    }

    /// <summary>Moves a task on to item <paramref name="done"/>, posted from the work pool.</summary>
    private void WorkReached(int token, int done, string current)
    {
        if (token != generation || work is null)
            return;
        work = work with { Done = done, Current = current };
        OnPropertyChanged(nameof(Work));
    }

    /// <summary>Stops showing the task, however it ended.</summary>
    private void EndWork()
    {
        if (work is null)
            return;
        work = null;
        workCancel = null;
        OnPropertyChanged(nameof(Work));
    }
}
