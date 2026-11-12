namespace HikariHopper.ViewModels;

/// <summary>
/// How far a long task has got, as the loading overlay shows it: <paramref name="Done"/> items of
/// <paramref name="Total"/> (none for a task of one step, which shows no bar), or with <paramref name="Percent"/> per
/// mille of what it expects to write; on <paramref name="Current"/> now, under a <paramref name="Title"/> in small
/// capitals; and whether it can stop.
/// </summary>
public sealed record WorkProgress(string Title, int Done, int Total, string Current, bool Cancellable, bool Percent = false)
{
    /// <summary>The share done, 0 to 1; none for a task of one step.</summary>
    public double? Fraction => Total > 0 ? (double)Done / Total : null;

    /// <summary>What the bar says: which item it is on, of how many ("3/12"), or how much is written ("45%"); none for a
    /// task of one step.</summary>
    public string? Count => Total <= 0 ? null : Percent ? $"{Done * 100 / Total}%" : $"{Math.Min(Done + 1, Total)}/{Total}";
}
