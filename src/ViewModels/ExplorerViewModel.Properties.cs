using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>The Properties of the one selected file or folder.</summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>Whether Properties can be read now: one item selected and nothing else running.</summary>
    public bool CanShowProperties => !EntryOperationBusy && SelectedEntries() is [_];

    /// <summary>
    /// Reads the selected item's properties on the operation queue (some need the file read); null when there is no
    /// one item to describe, or it couldn't be read (the status bar then says why).
    /// </summary>
    public Task<EntryProperties?> ReadSelectionProperties()
    {
        if (!CanShowProperties)
            return Task.FromResult<EntryProperties?>(null);
        EntryRecord entry = SelectedEntries()[0];
        string before = Status;
        var result = new TaskCompletionSource<EntryProperties?>();
        bool started = StartReadOperation($"Reading {entry.Name}…", () => Provider.DescribeEntry(entry), described =>
        {
            SetStatus(before);
            // Selected something else meanwhile: these are no longer the selection's.
            bool still = SelectedEntries() is [var current] && current.Equals(entry);
            result.TrySetResult(still ? (EntryProperties?)described : null);
        }, "read the properties", () => result.TrySetResult(null));
        return started ? result.Task : Task.FromResult<EntryProperties?>(null);
    }
}
