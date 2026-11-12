using System.ComponentModel;
using Avalonia.Threading;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

// Writes (create, import, delete), the single-threaded operation queue and cross-tab coordination.
public sealed partial class ExplorerViewModel
{
    private Task operationQueue = Task.CompletedTask;
    private bool entryOperationBusy;
    private bool peerEntryOperationBusy;
    private bool textureSaving;
    // Saves by the text viewer or the 3D viewer into an RPF of this tab's, under way.
    private int viewerSaves;

    /// <summary>True while this tab, a peer tab, a game load, a texture save or a viewer's save into an RPF is writing.</summary>
    public bool EntryOperationBusy => entryOperationBusy || peerEntryOperationBusy || gameLoading || TextureViewer.Saving || viewerSaves > 0;

    /// <summary>Busy because of this tab's own write (not a peer's).</summary>
    internal bool LocalEntryOperationBusy => entryOperationBusy || TextureViewer.Saving || viewerSaves > 0;

    /// <summary>The tail of the operation queue, completed once queued work has run (tests wait on it).</summary>
    internal Task OperationQueue => operationQueue;

    private void RaiseEntryOperationStateChanged() => OnPropertyChanged(nameof(EntryOperationBusy));

    private void OnTextureViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TextureViewerViewModel.Saving) or null or ""))
            return;
        bool saving = TextureViewer.Saving;
        if (saving != textureSaving)
        {
            textureSaving = saving;
            RaiseEntryOperationStateChanged();
        }
    }

    internal void SetPeerEntryOperationBusy(bool busy)
    {
        TextureViewer.SetExternalWriteBusy(busy || entryOperationBusy || gameLoading);
        if (busy == peerEntryOperationBusy)
            return;
        peerEntryOperationBusy = busy;
        RaiseEntryOperationStateChanged();
    }

    /// <summary>
    /// Runs <paramref name="operation"/> after every previously queued one (one at a time, FIFO) and
    /// delivers its result or error message on the UI thread.
    /// </summary>
    private void Enqueue(Func<object?> operation, Action<object?> completed, Action<string> failed,
        OperationProgress? progress = null, Action<OperationProgress>? cancelled = null)
    {
        operationQueue = operationQueue.ContinueWith(_ =>
        {
            object? result;
            try
            {
                // The file loops it runs report through this, and stop when asked.
                using (progress?.Use())
                    result = operation();
            }
            catch (OperationCanceledException) when (progress is not null && cancelled is not null)
            {
                Dispatcher.UIThread.Post(() => cancelled(progress));
                return;
            }
            catch (Exception error)
            {
                // Python only caught its expected error types; any failure here must still end the operation.
                string message = error.Message;
                Dispatcher.UIThread.Post(() => failed(message));
                return;
            }
            Dispatcher.UIThread.Post(() => completed(result));
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public bool CreateFolder(string name)
    {
        string normalized = CreationName(name);
        return normalized.Length > 0
            && StartEntryCreation(normalized, target => () => EntryOperations.CreateFolderAt(target, normalized));
    }

    public bool CreateEmptyRpf(string name)
    {
        string normalized = CreationName(name, "rpf");
        return normalized.Length > 0
            && StartEntryCreation(normalized, target => () => EntryOperations.CreateEmptyRpfAt(target, normalized));
    }

    public bool CreateRpfFromFolder(string name, string source)
    {
        string normalized = CreationName(name, "rpf");
        return normalized.Length > 0
            && StartEntryCreation(normalized, target => () => EntryOperations.CreateRpfFromFolderAt(target, normalized, source));
    }

    public bool CreateRpfFromZip(string name, string source)
    {
        string normalized = CreationName(name, "rpf");
        return normalized.Length > 0
            && StartEntryCreation(normalized, target => () => EntryOperations.CreateRpfFromZipAt(target, normalized, source));
    }

    public bool CreateYtd(string name)
    {
        string normalized = CreationName(name, "ytd");
        return normalized.Length > 0
            && StartEntryCreation(normalized, target => () => EntryOperations.CreateYtdAt(target, normalized));
    }

    /// <summary>
    /// Imports dropped files into the current folder. Items are local paths or <c>file://</c> URIs;
    /// pass <c>null</c> for a dropped item without a local path so the drop is rejected. When some of their names are
    /// already there and <paramref name="conflict"/> doesn't say what to do with them, nothing is imported yet:
    /// <see cref="ImportConflictsFound"/> asks, and <see cref="ResolveImport"/> carries on with the answer.
    /// </summary>
    public bool ImportDroppedFiles(IReadOnlyList<string?> paths, ImportConflict? conflict = null)
    {
        if (EntryOperationBusy)
            return false;
        IReadOnlyList<string> sources;
        EntryCreationTarget target;
        string[] clashes;
        try
        {
            sources = FileDrops.LocalDropPaths(paths);
            target = Provider.CreationTarget(currentPath);
            clashes = [.. sources.Select(source => EntryNames.NormalizeEntryName(Path.GetFileName(Path.TrimEndingDirectorySeparator(source))))
                .Where(name => currentEntryNames.Contains(name, StringComparer.OrdinalIgnoreCase))];
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not import files: {error.Message}");
            return false;
        }
        if (conflict is null && clashes.Length > 0)
        {
            ImportConflictsFound?.Invoke(this, new ImportConflicts(sources, clashes));
            return true;
        }
        int count = sources.Count;
        string noun = count == 1 ? "file" : "files";
        return StartEntryOperation(target, $"import {noun}", $"Importing {count} {noun}…",
            () => EntryOperations.ImportFilesAt(target, sources, conflict ?? ImportConflict.Fail), EntryImportCompleted);
    }

    /// <summary>Some dropped files' names are already in the folder: what to do with them is asked before anything is imported.</summary>
    public event EventHandler<ImportConflicts>? ImportConflictsFound;

    /// <summary>Carries on with a drop whose names clashed, replacing, skipping or keeping both, as answered.</summary>
    public bool ResolveImport(ImportConflicts pending, ImportConflict choice) => ImportDroppedFiles([.. pending.Sources], choice);

    private string CreationName(string name, string kind = "entry")
    {
        var (normalized, error) = ValidatedCreationName(name, kind);
        if (error.Length > 0)
        {
            SetStatus(error);
            return "";
        }
        return normalized;
    }

    /// <summary>The validation message for a new <paramref name="kind"/> (<c>entry</c>, <c>rpf</c>, <c>ytd</c>), or empty.</summary>
    public string CreationNameError(string name, string kind) => ValidatedCreationName(name, kind).Error;

    private (string Normalized, string Error) ValidatedCreationName(string name, string kind)
    {
        try
        {
            string normalized = kind switch
            {
                "rpf" => EntryNames.NormalizeRpfName(name),
                "ytd" => EntryNames.NormalizeYtdName(name),
                _ => EntryNames.NormalizeEntryName(name),
            };
            EntryNames.EnsureEntryNameAvailable(normalized, currentEntryNames);
            return (normalized, "");
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            return ("", error.Message);
        }
    }

    private bool StartEntryCreation(string label, Func<EntryCreationTarget, Func<string>> operationFactory)
    {
        if (EntryOperationBusy)
            return false;
        EntryCreationTarget target;
        try
        {
            target = Provider.CreationTarget(currentPath);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not create entry: {error.Message}");
            return false;
        }
        Func<string> operation = operationFactory(target);
        // Inside an RPF it is one rewrite, stopped safely before the swap; a new RPF on disk is written in place.
        return StartEntryOperation(target, $"create {label}", $"Creating {label}…", () => operation(), EntryCreationCompleted,
            cancellable: target.InArchive);
    }

    /// <param name="target">An <see cref="EntryCreationTarget"/> or <see cref="EntryDeletionTarget"/>.</param>
    /// <param name="cancelled">What the status says when it is cancelled: by default that nothing was changed, as a write
    /// into an RPF stops before the archive is swapped and a loose import undoes what it copied.</param>
    /// <param name="cancellable">Whether it offers Cancel at all.</param>
    private bool StartEntryOperation(object target, string failureLabel, string progressLabel,
        Func<object?> operation, Action<object, object?> completed, Func<OperationProgress, string>? cancelled = null,
        bool cancellable = true)
    {
        if (EntryOperationBusy)
            return false;
        entryOperationBusy = true;
        TextureViewer.SetExternalWriteBusy(true);
        RaiseEntryOperationStateChanged();
        SetStatus(progressLabel);
        try
        {
            if (LocationOf(target).ArchivePath is { } archivePath)
                PrepareArchiveWrite(archivePath);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            EntryOperationFailed(target, failureLabel, error.Message);
            return false;
        }
        OperationProgress progress = BeginWork(progressLabel, cancellable);
        Enqueue(operation, result =>
        {
            EndWork();
            completed(target, result);
        }, message =>
        {
            EndWork();
            EntryOperationFailed(target, failureLabel, message);
        }, progress, stopped =>
        {
            EndWork();
            EntryOperationCancelled(target, cancelled?.Invoke(stopped) ?? CancelledStatus(progressLabel, "nothing was changed"));
        });
        return true;
    }

    private void EntryOperationCancelled(object target, string status)
    {
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
        }
        SetStatus(status);
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }

    private static EntryCreationTarget LocationOf(object target) =>
        target is EntryDeletionTarget deletion ? deletion.Location : (EntryCreationTarget)target;

    private void EntryCreationCompleted(object target, object? result)
    {
        string name = (string)result!;
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
            SelectEntriesByName([name]);
        }
        SetStatus($"Created {name}");
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }

    private void EntryImportCompleted(object target, object? result)
    {
        var names = (IReadOnlyList<string>)result!;
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
            SelectEntriesByName(names);
        }
        int count = names.Count;
        SetStatus(count == 0 ? "Nothing imported: every file was already here" : $"Imported {count} {(count == 1 ? "file" : "files")}");
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }

    private void EntryOperationFailed(object target, string label, string message)
    {
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
        }
        SetStatus($"Could not {label}: {message}");
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }

    private void FinishEntryOperation()
    {
        entryOperationBusy = false;
        TextureViewer.SetExternalWriteBusy(peerEntryOperationBusy);
        RaiseEntryOperationStateChanged();
    }

    /// <summary>Reloads the written archive, or reports whether the written folder is the one shown.</summary>
    private bool RefreshOperationTarget(object target)
    {
        EntryCreationTarget location = LocationOf(target);
        try
        {
            if (location.ArchivePath is not null)
                return Provider.ReloadArchive(location.ArchivePath);
            EntryCreationTarget current = Provider.CreationTarget(currentPath);
            return SamePath(current.Directory, location.Directory);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            return false;
        }
    }

    /// <summary>Raises <see cref="DeleteConfirmationRequested"/> for a deletable selection.</summary>
    public void RequestDeleteSelection()
    {
        if (EntryOperationBusy || selectedRows.Count == 0)
            return;
        if (!SelectionDeletable)
        {
            SetStatus("No items are selected");
            return;
        }
        DeleteConfirmationRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes the selection: loose items go to the Recycle Bin, archived ones are removed from the RPF.</summary>
    public bool DeleteSelectedFiles()
    {
        if (EntryOperationBusy || !SelectionDeletable)
            return false;
        List<EntryRecord> selection = SelectedEntries();
        if (SearchShowsLocation)
            return DeleteSearchHits(SelectedHits());
        EntryDeletionTarget target;
        try
        {
            target = Provider.DeletionTarget(selection, currentPath);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not delete selection: {error.Message}");
            return false;
        }

        if (!target.InArchive)
            LooseFilesAboutToBeDeleted?.Invoke(this, target.LoosePaths);

        int count = selection.Count;
        string noun = count == 1 ? "item" : "items";
        Func<object?> operation = target.InArchive
            ? () => EntryOperations.DeleteArchiveFilesAt(target)
            : () => EntryOperations.MoveFilesToTrash(target);
        string progress = target.InArchive
            ? $"Deleting {count} {noun} from RPF…"
            : $"Moving {count} {noun} to the Recycle Bin…";
        // Moved to the Recycle Bin one at a time, what was moved stays there; an RPF stops before it is swapped.
        Func<OperationProgress, string>? cancelled = target.InArchive ? null
            : stopped => CancelledStatus(progress, $"{stopped.Succeeded} of {stopped.Total} {noun} were moved to the Recycle Bin");
        return StartEntryOperation(target, "delete selected items", progress, operation, EntryDeletionCompleted, cancelled);
    }

    private void EntryDeletionCompleted(object target, object? result)
    {
        int count = (int)result!;
        var deletion = (EntryDeletionTarget)target;
        if (RefreshOperationTarget(target))
        {
            ClearSelection();
            RefreshEntries();
        }
        string noun = count == 1 ? "item" : "items";
        SetStatus(deletion.InArchive ? $"Deleted {count} {noun} from RPF" : $"Moved {count} {noun} to the Recycle Bin");
        ContentChanged?.Invoke(this, target);
        FinishEntryOperation();
    }

    /// <summary>A peer tab is about to recycle <paramref name="paths"/>: release an archive opened from them.</summary>
    internal void PrepareLooseFileDeletion(IReadOnlyList<string> paths)
    {
        bool wasInArchive = Provider.InArchive;
        string archiveGamePath = Provider.ArchiveGamePath;
        if (!Provider.ReleaseOpenArchiveFor(paths))
            return;
        ClearArchiveExpansion();
        if (!wasInArchive)
            return;
        if (!Provider.HasGame)
        {
            CloseWorkspace();
            return;
        }
        ResetNavigation(PosixParent(archiveGamePath));
        ExpandCurrentBranch();
        PublishWorkspace();
        SetStatus("The open RPF is being deleted in another tab");
    }

    /// <summary>A peer tab changed files at <paramref name="target"/>; refresh what this tab shows of it.</summary>
    internal void RefreshExternalContent(object target)
    {
        if ((target is EntryDeletionTarget deletion ? deletion.Location : target) is not EntryCreationTarget location)
            return;
        if (location.InArchive)
        {
            string previousPrefix = Provider.ArchivePrefix;
            bool reloaded;
            try
            {
                reloaded = Provider.ReloadArchive(location.ArchivePath!);
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                return;
            }
            if (reloaded)
            {
                if (Provider.ArchivePrefix != previousPrefix)
                {
                    ClearArchiveExpansion();
                    ResetNavigation();
                    ExpandCurrentBranch();
                    PublishWorkspace();
                }
                ClearSelection();
                RefreshEntries();
            }
            return;
        }
        if (location.Directory is null || !Provider.HasGame)
            return;
        string gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Provider.GamePath));
        string changedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Directory));
        if (!SamePath(changedDirectory, gameRoot)
            && !changedDirectory.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        if (!Provider.InArchive)
        {
            EntryCreationTarget? current;
            try
            {
                current = Provider.CreationTarget(currentPath);
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                current = null;
            }
            if (current is null || SamePath(current.Directory, changedDirectory))
            {
                ClearSelection();
                RefreshEntries();
                return;
            }
        }
        RefreshTree();
    }

    private static bool SamePath(string? left, string? right)
    {
        if (left is null || right is null)
            return left == right;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    }

    private void OpenTextureDictionary(EntryRecord entry, Action<SavedDocument>? closedAfterSave = null)
    {
        byte[] data;
        string sourcePath;
        ArchiveEntryTarget? sourceTarget;
        try
        {
            data = Provider.ReadEntryBytes(entry);
            sourcePath = Provider.EntrySourcePath(entry);
            sourceTarget = Provider.InArchive ? Provider.GetArchiveEntryTarget(entry) : null;
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open texture dictionary: {error.Message}");
            return;
        }
        TextureViewer.OpenDictionary(entry.Name, sourcePath, data,
            sourceSaver: sourceTarget is not null ? sourceTarget.Save : null,
            sourcePrepare: sourceTarget is not null ? () => PrepareTextureWrite(sourceTarget) : null,
            sourceRootPath: sourceTarget?.RootPath ?? "", closedAfterSave: closedAfterSave);
        SetStatus($"Opening {entry.Name}");
    }

    private void PrepareArchiveWrite(string rootPath)
    {
        Provider.PrepareArchiveWrite(rootPath);
        ArchiveWriteRequested?.Invoke(this, rootPath);
    }

    /// <summary>Where a viewer saves an entry it changed: into its RPF, or over the loose file; none when neither can be had.</summary>
    private SaveTarget? SaveTargetOf(EntryRecord entry)
    {
        try
        {
            return SaveTargetOf(Provider.EntrySourcePath(entry), Provider.InArchive ? Provider.GetArchiveEntryTarget(entry) : null, entry.Name);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            return null;
        }
    }

    /// <summary>
    /// Where a viewer (the 3D viewer, the text viewer) saves a file: into its root RPF through <paramref name="target"/>, as the texture editor does,
    /// or over the loose file at <paramref name="source"/>; then what lists it is read again.
    /// </summary>
    private SaveTarget SaveTargetOf(string source, ArchiveEntryTarget? target, string name)
    {
        if (target is not null)
            return new SaveTarget(target.Save, () =>
            {
                PrepareTextureWrite(target);
                ViewerSaving(true);
            }, success =>
            {
                ViewerSaving(false);
                FileSaved(new EntryCreationTarget(ArchivePath: target.RootPath), success, name);
            });
        return new SaveTarget(bytes => CommonFury.IO.AtomicFile.Write(source, bytes),
            Finished: success => FileSaved(new EntryCreationTarget(Directory: Path.GetDirectoryName(source)), success, name));
    }

    // A viewer's save into an RPF counts as this tab writing, as a texture save does: nothing else here or in the other
    // tabs writes the RPF meanwhile.
    private void ViewerSaving(bool started)
    {
        viewerSaves += started ? 1 : -1;
        if (viewerSaves == (started ? 1 : 0))
            RaiseEntryOperationStateChanged();
    }

    // A file a viewer saved: what lists it, here and in the other tabs, is read again.
    private void FileSaved(EntryCreationTarget target, bool success, string name)
    {
        RefreshExternalContent(target);
        ContentChanged?.Invoke(this, target);
        if (SearchShowsLocation && searchIndex is not null)
            searchIndexTask = searchIndex.Refresh();
        if (success)
        {
            // Only a file's contents changed: the game's texture tables still hold, and are kept for next time.
            if (target.ArchivePath is { } archive)
                _ = GameTextures.Rewritten(archive);
            SetStatus($"Saved {name}");
        }
    }

    private void PrepareTextureWrite(ArchiveEntryTarget target)
    {
        target.Prepare();
        PrepareArchiveWrite(target.RootPath);
    }

    private void OnTextureSourceWriteFinished(object? sender, SourceWriteResult result)
    {
        var target = new EntryCreationTarget(ArchivePath: result.RootPath);
        RefreshExternalContent(target);
        ContentChanged?.Invoke(this, target);
        if (SearchShowsLocation && searchIndex is not null)
            searchIndexTask = searchIndex.Refresh();
        if (result.Success)
        {
            // Only a file's contents changed: the game's texture tables still hold, and are kept for next time.
            _ = GameTextures.Rewritten(result.RootPath);
            SetStatus($"Saved {(TextureViewer.IsDrawable ? "YDR" : "YTD")} inside RPF");
        }
    }

    /// <summary>Releases what the tab holds once it is closed (Python's object destruction).</summary>
    internal void Release()
    {
        TextureViewer.SourceWriteFinished -= OnTextureSourceWriteFinished;
        TextureViewer.PropertyChanged -= OnTextureViewerPropertyChanged;
        ReleaseDragStaging();
    }
}

/// <summary>The exception types the Python port caught as <c>OSError</c>/<c>ValueError</c>/<c>RuntimeError</c>.</summary>
internal static class OperationErrors
{
    public static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException
        or ArgumentException or InvalidDataException or InvalidOperationException or NotSupportedException
        or FormatException or OverflowException or KeyNotFoundException;
}
