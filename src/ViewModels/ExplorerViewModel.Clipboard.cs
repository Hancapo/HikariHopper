using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>
/// Copying files and pasting them, through the system clipboard: so between folders and archives here, and to and
/// from Windows Explorer. An archived file has no file on disk, so a copy writes it to a temporary folder first.
/// </summary>
public sealed partial class ExplorerViewModel
{
    // Where the last copy out of an archive was written; it stays until the next copy, or the app exits.
    private DragStaging? clipboardStaging;

    /// <summary>Whether a paste can run here now: files on the clipboard, a location open and nothing else running.</summary>
    public bool CanPaste => HasWorkspace && !searching && !EntryOperationBusy && AppServices.Clipboard.HasFiles;

    /// <summary>Copy: puts the selected files and folders on the clipboard.</summary>
    public async Task<bool> CopySelection()
    {
        List<EntryRecord> selection = SelectedEntries();
        if (EntryOperationBusy || selection.Count == 0)
            return false;
        string noun = selection.Count == 1 ? selection[0].Name : $"{selection.Count} items";
        if (SearchShowsLocation)
            return CopySearchFiles(SelectedHits(), noun);
        if (!Provider.InArchive)
        {
            List<string> paths;
            try
            {
                paths = Provider.ExportEntries(selection, "");
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                SetStatus($"Could not copy: {error.Message}");
                return false;
            }
            return await PutOnClipboard(paths, noun);
        }
        clipboardStaging?.Cleanup();
        var staging = new DragStaging();
        clipboardStaging = staging;
        return StartReadOperation($"Copying {noun}…", () => Provider.ExportEntries(selection, staging.Name),
            result => _ = PutOnClipboard((List<string>)result!, noun), "copy");
    }

    /// <summary>Paste: copies the files and folders on the clipboard into the folder shown; a name already here gets " (2)".</summary>
    public async Task<bool> Paste()
    {
        if (EntryOperationBusy || !HasWorkspace || searching)
            return false;
        IReadOnlyList<string> files = await AppServices.Clipboard.GetFilesAsync();
        if (files.Count == 0)
        {
            SetStatus("Nothing to paste");
            return false;
        }
        EntryCreationTarget target;
        try
        {
            target = Provider.CreationTarget(currentPath);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not paste: {error.Message}");
            return false;
        }
        int count = files.Count;
        string noun = count == 1 ? "item" : "items";
        return StartEntryOperation(target, "paste", $"Pasting {count} {noun}…",
            () => EntryOperations.ImportAt(target, files, freeNames: true), EntryPasteCompleted);
    }

    private async Task<bool> PutOnClipboard(IReadOnlyList<string> paths, string noun)
    {
        bool copied = await AppServices.Clipboard.SetFilesAsync(paths);
        SetStatus(copied ? $"Copied {noun}" : "Could not copy to the clipboard");
        return copied;
    }

    private void EntryPasteCompleted(object target, object? result)
    {
        var names = (IReadOnlyList<string>)result!;
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
            SelectEntriesByName(names);
        }
        SetStatus($"Pasted {(names.Count == 1 ? names[0] : $"{names.Count} items")}");
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }
}
