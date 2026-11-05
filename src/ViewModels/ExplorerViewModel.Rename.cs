using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>Renaming the one selected file or folder, on disk or in an RPF.</summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>Whether Rename can run now: one item selected, a location open and nothing else running.</summary>
    public bool CanRenameSelection => HasWorkspace && !EntryOperationBusy && SelectedEntries() is [_];

    /// <summary>The selected item's name, which the rename dialog starts from.</summary>
    public string RenameSource => SelectedEntries() is [var entry] ? entry.Name : "";

    /// <summary>Why <paramref name="name"/> can't be the selected item's new name; empty when it can.</summary>
    public string RenameNameError(string name)
    {
        try
        {
            string normalized = EntryNames.NormalizeEntryName(name);
            string current = RenameSource;
            if (!string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase))
                EntryNames.EnsureEntryNameAvailable(normalized, currentEntryNames);
            return "";
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            return error.Message;
        }
    }

    /// <summary>Renames the selected item to <paramref name="name"/>. The same name again does nothing.</summary>
    public bool RenameSelected(string name)
    {
        if (!CanRenameSelection || RenameNameError(name).Length > 0)
            return false;
        string current = RenameSource;
        string normalized = EntryNames.NormalizeEntryName(name);
        if (string.Equals(normalized, current, StringComparison.Ordinal))
            return true;
        EntryCreationTarget target;
        try
        {
            target = Provider.CreationTarget(currentPath);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not rename: {error.Message}");
            return false;
        }
        return StartEntryOperation(target, $"rename {current}", $"Renaming {current}…",
            () => EntryOperations.RenameAt(target, current, normalized), (operationTarget, result) =>
            {
                string renamed = (string)result!;
                if (RefreshOperationTarget(operationTarget))
                {
                    ClearSelection();
                    RefreshEntries();
                    SelectEntriesByName([renamed]);
                }
                SetStatus($"Renamed {current} to {renamed}");
                ContentChanged?.Invoke(this, operationTarget);
                FinishEntryOperation();
            });
    }
}
