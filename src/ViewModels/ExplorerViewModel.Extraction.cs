using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>Extracting: the selection, or an RPF's whole content, into a folder the user picks.</summary>
public sealed partial class ExplorerViewModel
{
    // Where the last extraction went, where the next folder picker starts.
    private string lastExtractFolder = "";

    /// <summary>Whether the one selected item is an RPF whose content can be extracted.</summary>
    public bool CanExtractArchiveContent => SelectedEntries() is [var entry]
        && !entry.IsDirectory && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extract…: asks for a folder and copies the selected files and folders into it.</summary>
    public async Task<bool> ExtractSelection()
    {
        List<EntryRecord> selection = SelectedEntries();
        if (EntryOperationBusy || selection.Count == 0)
            return false;
        string? folder = await AppServices.Dialogs.PickFolderAsync("Extract to", StartFolder());
        if (string.IsNullOrEmpty(folder))
            return false;
        string noun = selection.Count == 1 ? selection[0].Name : $"{selection.Count} items";
        lastExtractFolder = folder;
        if (SearchShowsLocation)
            return ExtractSearchFiles(SelectedHits(), folder, noun);
        return StartReadOperation($"Extracting {noun}…", () => Provider.ExtractEntries(selection, folder),
            _ =>
            {
                SetStatus($"Extracted {noun} to {folder}");
                ShowExtracted(folder);
            }, "extract", cancelled: ExtractCancelled($"Extracting {noun}…"));
    }

    /// <summary>Extract all content…: asks for a folder and writes the selected RPF's content into a folder named after it there.</summary>
    public async Task<bool> ExtractArchiveContent()
    {
        if (EntryOperationBusy || !CanExtractArchiveContent)
            return false;
        EntryRecord archive = SelectedEntries()[0];
        string? folder = await AppServices.Dialogs.PickFolderAsync($"Extract the content of {archive.Name} to", StartFolder());
        if (string.IsNullOrEmpty(folder))
            return false;
        lastExtractFolder = folder;
        if (SearchShowsLocation && archive.NativeEntry is Core.Search.SearchHit hit)
            return ExtractSearchArchive(hit, folder);
        return StartReadOperation($"Extracting {archive.Name}…", () => Provider.ExtractArchiveContents(archive, folder),
            result =>
            {
                SetStatus($"Extracted {archive.Name} to {result}");
                ShowExtracted(folder);
            }, "extract", cancelled: ExtractCancelled($"Extracting {archive.Name}…"));
    }

    /// <summary>Lists the folder shown again when the extraction went into it, so what was written appears.</summary>
    private void ShowExtracted(string folder)
    {
        if (Provider.InArchive)
            return;
        try
        {
            if (Provider.CreationTarget(currentPath).Directory is { } shown
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(shown)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase))
            {
                ClearSelection();
                RefreshEntries();
            }
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
        }
    }

    private string StartFolder() => lastExtractFolder.Length > 0 && Directory.Exists(lastExtractFolder) ? lastExtractFolder : Provider.GamePath;

    /// <summary>
    /// Runs work that only reads here (extracting, copying out of an archive) on the operation queue; nothing may change
    /// while it does. <paramref name="verb"/> names it in a failure.
    /// </summary>
    /// <param name="cancelled">What the status says when it is cancelled, with failed's clean-up run; none offers no
    /// Cancel.</param>
    private bool StartReadOperation(string progress, Func<object?> work, Action<object?> done, string verb, Action? failed = null,
        Func<OperationProgress, string>? cancelled = null)
    {
        if (EntryOperationBusy)
            return false;
        entryOperationBusy = true;
        TextureViewer.SetExternalWriteBusy(true);
        RaiseEntryOperationStateChanged();
        SetStatus(progress);
        OperationProgress reports = BeginWork(progress, cancellable: cancelled is not null);
        Enqueue(work, result =>
        {
            EndWork();
            FinishEntryOperation();
            done(result);
        }, message =>
        {
            EndWork();
            SetStatus($"Could not {verb}: {message}");
            FinishEntryOperation();
            failed?.Invoke();
        }, reports, stopped =>
        {
            EndWork();
            SetStatus(cancelled!(stopped));
            FinishEntryOperation();
            failed?.Invoke();
        });
        return true;
    }

    /// <summary>What a cancelled extract says: the files it wrote stay where they are.</summary>
    private static Func<OperationProgress, string> ExtractCancelled(string progress) =>
        stopped => CancelledStatus(progress, $"{stopped.Done} of {stopped.Total} files were written");
}
