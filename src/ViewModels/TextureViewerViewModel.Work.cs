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

/// <summary>
/// How far a long task has got: <paramref name="Done"/> items of <paramref name="Total"/> (none for a task of one
/// step, which shows no bar), on <paramref name="Current"/> now, under a <paramref name="Title"/> in small capitals.
/// </summary>
public sealed record WorkProgress(string Title, int Done, int Total, string Current, bool Cancellable)
{
    /// <summary>The share done, 0 to 1; none for a task of one step.</summary>
    public double? Fraction => Total > 0 ? (double)Done / Total : null;

    /// <summary>Which item it is on, of how many: "3/12"; none for a task of one step.</summary>
    public string? Count => Total > 0 ? $"{Math.Min(Done + 1, Total)}/{Total}" : null;
}
