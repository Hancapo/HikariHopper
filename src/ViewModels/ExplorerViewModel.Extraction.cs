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
        return StartExtraction(folder, $"Extracting {noun}…", () => Provider.ExtractEntries(selection, folder),
            _ => $"Extracted {noun} to {folder}");
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
        return StartExtraction(folder, $"Extracting {archive.Name}…", () => Provider.ExtractArchiveContents(archive, folder),
            result => $"Extracted {archive.Name} to {result}");
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

    /// <summary>Runs an extraction on the operation queue: it only reads here, but nothing may change while it does.</summary>
    private bool StartExtraction(string folder, string progress, Func<object?> work, Func<object?, string> done)
    {
        if (EntryOperationBusy)
            return false;
        lastExtractFolder = folder;
        entryOperationBusy = true;
        TextureViewer.SetExternalWriteBusy(true);
        RaiseEntryOperationStateChanged();
        SetStatus(progress);
        Enqueue(work, result =>
        {
            SetStatus(done(result));
            FinishEntryOperation();
            ShowExtracted(folder);
        }, message =>
        {
            SetStatus($"Could not extract: {message}");
            FinishEntryOperation();
        });
        return true;
    }
}
