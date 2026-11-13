using CommonFury;
using CommonFury.IO;
using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using HikariHopper.Infrastructure;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

public sealed partial class TextureViewerViewModel
{
    /// <summary>Writes bytes to a loose file atomically (Python <c>atomic_write_bytes</c>); replaceable by tests.</summary>
    internal Action<string, byte[]> WriteFile { get; set; } = (path, data) => AtomicFile.Write(path, data);

    /// <summary>
    /// Writes one texture of an extract to a DDS file in a folder: through the library's export, so each keeps its
    /// checks on the name. Replaceable by tests, to see an extract part way.
    /// </summary>
    internal Action<Texture, GameTarget, string> ExportTexture { get; set; } =
        (texture, target, folder) => TextureDirectory.ExportDds(new TextureSet([texture], target).ToDictionary(), folder);

    /// <summary>Saves into the source: through the archive saver for RPF entries, otherwise to the loose file.</summary>
    public bool SaveYtd()
    {
        if (dictionary is null || OperationBusy)
            return false;
        Func<TextureSet, byte[]> serialize = Serializer();
        TextureSet snapshot = dictionary;
        if (sourceSaver is { } saver)
            return StartSave(sourceName, () => saver(Serialized(snapshot, serialize)), prepare: sourcePrepare, rootPath: sourceRootPath);
        if (!CanSaveSource)
            return false;
        string destination = sourcePath;
        Action<string, byte[]> write = WriteFile;
        return StartSave(Path.GetFileName(destination), () => write(destination, Serialized(snapshot, serialize)));
    }

    /// <summary>Asks for a destination and saves a loose copy there, which then becomes the document's source.</summary>
    public async Task<bool> SaveYtdAs()
    {
        if (dictionary is null || OperationBusy)
            return false;
        bool drawable = IsDrawable;
        string extension = drawable ? ".ydr" : ".ytd";
        string suggested = sourceName.Length > 0 ? sourceName : "textures.ytd";
        string? path = await Dialogs.PickSaveFileAsync(drawable ? "Save drawable" : "Save texture dictionary",
            suggested, [drawable ? new FileFilter("Drawable", ["*.ydr"]) : new FileFilter("Texture Dictionary", ["*.ytd"])]);
        if (string.IsNullOrEmpty(path) || dictionary is null)
            return false;
        // Another extension is kept, with the file's added after it.
        string destination = SaveNames.WithExtension(path, extension);
        if (SaveNames.ReplacesUnpicked(path, destination))
        {
            SetStatus($"Could not save {Path.GetFileName(destination)}: a file of that name is already there. Pick it in Save as to replace it");
            return false;
        }
        Func<TextureSet, byte[]> serialize = Serializer();
        TextureSet snapshot = dictionary;
        Action<string, byte[]> write = WriteFile;
        return StartSave(Path.GetFileName(destination), () => write(destination, Serialized(snapshot, serialize)),
            sourceUpdate: (destination, Path.GetFileName(destination)));
    }

    /// <summary>The document's file as a save writes it, kept as what it saved once the write succeeds.</summary>
    private byte[] Serialized(TextureSet snapshot, Func<TextureSet, byte[]> serialize)
    {
        savedNow = null;
        byte[] data = serialize(snapshot);
        savedNow = new SavedDocument(snapshot.Textures, data);
        return data;
    }

    /// <summary>Extracts the selection: one texture to a chosen DDS file, several to DDS files in a chosen folder.</summary>
    public async Task ExtractSelected()
    {
        if (SelectedRecord() is not { } record || OperationBusy)
            return;
        if (SelectedCount > 1)
        {
            var selection = new TextureSet(SelectionRecords().Select(item => item.Item.Texture), dictionary!.Target);
            string? folder = await Dialogs.PickFolderAsync("Extract selected textures");
            if (string.IsNullOrEmpty(folder))
                return;
            ExtractTo(selection, folder);
            return;
        }
        string? path = await Dialogs.PickSaveFileAsync("Extract texture", $"{record.Name}.dds",
            [new FileFilter("DirectDraw Surface", ["*.dds"])]);
        if (string.IsNullOrEmpty(path))
            return;
        string destination = string.Equals(Path.GetExtension(path), ".dds", StringComparison.OrdinalIgnoreCase)
            ? path : Path.ChangeExtension(path, ".dds");
        try
        {
            AtomicFile.Write(destination, DdsCodec.Write(record.Texture.ToSurface()));
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            SetStatus($"Could not extract texture: {exception.Message}");
            return;
        }
        SetStatus($"Extracted {record.Name}");
    }

    /// <summary>Extracts every texture to DDS files in a chosen folder.</summary>
    public async Task ExtractAll()
    {
        if (dictionary is null)
            return;
        string? folder = await Dialogs.PickFolderAsync("Extract all textures");
        if (string.IsNullOrEmpty(folder) || dictionary is null)
            return;
        ExtractTo(dictionary, folder);
    }

    /// <summary>
    /// Writes each texture to a DDS file in <paramref name="folder"/> on the work pool, a bar counting them; Cancel stops
    /// it between textures, and those already written stay. The whole set is checked first, so names that would collide
    /// refuse the extract before any file is written.
    /// </summary>
    private void ExtractTo(TextureSet textures, string folder)
    {
        if (OperationBusy)
            return;
        Texture[] items = [.. textures.Textures];
        if (items.Length == 0)
        {
            SetStatus("Extracted 0 textures");
            return;
        }
        GameTarget target = textures.Target;
        Action<Texture, GameTarget, string> export = ExportTexture;
        operationBusy = true;
        CancellationToken cancel = BeginWork("EXTRACTING TEXTURES", items.Length, items[0].Name ?? "", cancellable: true);
        SetStatus($"Extracting {items.Length} textures…");
        EmitStateChanged();
        int token = generation;
        pool.Start(() =>
        {
            try
            {
                CheckExtract(items, target);
            }
            catch (Exception exception)
            {
                string message = exception.Message;
                Post(() => ExtractFinished(token, 0, items.Length, message, cancelled: false));
                return;
            }
            for (int index = 0; index < items.Length; index++)
            {
                int done = index;
                if (cancel.IsCancellationRequested)
                {
                    Post(() => ExtractFinished(token, done, items.Length, null, cancelled: true));
                    return;
                }
                string name = items[index].Name ?? "";
                Post(() => WorkReached(token, done, name));
                try
                {
                    export(items[index], target, folder);
                }
                catch (Exception exception)
                {
                    // Any failure ends the extract with its reason, so the editor never stays busy.
                    string message = exception.Message;
                    Post(() => ExtractFinished(token, done, items.Length, message, cancelled: false));
                    return;
                }
            }
            Post(() => ExtractFinished(token, items.Length, items.Length, null, cancelled: false));
        }, PriorityTransform);
    }

    /// <summary>
    /// The checks the library's export makes across a whole dictionary, which one texture at a time never meets: names
    /// whose lookup hashes repeat, and file names that are the same but for case.
    /// </summary>
    private static void CheckExtract(Texture[] items, GameTarget target)
    {
        new TextureSet(items, target).ToDictionary().Validate().ThrowIfInvalid();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Texture texture in items)
        {
            string file = texture.Name + ".dds";
            if (!files.Add(file))
                throw new ArgumentException($"Texture filenames collide at '{file}'.");
        }
    }

    private void ExtractFinished(int token, int written, int total, string? error, bool cancelled)
    {
        if (token != generation)
            return;
        operationBusy = false;
        EndWork();
        string kept = $"{written} of {total} textures were written";
        SetStatus(error is not null ? $"Could not extract textures: {error}{(written > 0 ? $" ({kept})" : "")}"
            : cancelled ? $"Extract cancelled: {kept}"
            : $"Extracted {total} textures");
        EmitStateChanged();
    }

    private Func<TextureSet, byte[]> Serializer()
    {
        if (ydrDocument is not { } document)
            return YtdSaving.Serialize;
        var renames = new Dictionary<string, string>(textureRenames, StringComparer.Ordinal);
        var deleted = new HashSet<string>(deletedTextureOrigins, StringComparer.Ordinal);
        return snapshot => document.Serialize(snapshot, renames, deleted);
    }

    /// <summary>Python <c>_start_save</c>: runs <paramref name="saveAction"/> in the background after
    /// <paramref name="prepare"/> releases source handles on the UI thread.</summary>
    internal bool StartSave(string label, Action saveAction, (string Path, string Name)? sourceUpdate = null,
        Action? prepare = null, string rootPath = "")
    {
        if (dictionary is null || OperationBusy)
            return false;
        operationBusy = true;
        saving = true;
        savingRootPath = rootPath;
        // One write: the overlay shows the train, with nothing to count and nothing to cancel.
        BeginWork("SAVING", 0, label, cancellable: false);
        SetStatus($"Saving {label}…");
        EmitStateChanged();
        try
        {
            prepare?.Invoke();
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            SaveFailed(generation, label, exception.Message);
            return false;
        }
        int token = generation;
        pool.Start(() =>
        {
            try
            {
                saveAction();
            }
            catch (Exception exception)
            {
                Post(() => SaveFailed(token, label, exception.Message));
                return;
            }
            Post(() => SaveCompleted(token, label, sourceUpdate));
        }, PrioritySave);
        return true;
    }

    /// <summary>Python <c>_save_completed</c>.</summary>
    internal void SaveCompleted(int token, string label, (string Path, string Name)? sourceUpdate)
    {
        if (token != generation)
            return;
        FinishSourceWrite(true);
        operationBusy = false;
        saving = false;
        EndWork();
        if (sourceUpdate is { } update)
        {
            (sourcePath, sourceName) = update;
            sourceSaver = null;
            sourcePrepare = null;
            sourceRootPath = "";
        }
        savedRevision = revision;
        if (savedNow is { } saved)
            lastSave = saved;
        savedNow = null;
        SetStatus($"Saved {label}");
        EmitStateChanged();
        SaveFinished?.Invoke(this, true);
        if (changeAfterSave)
            CompleteDocumentChange();
    }

    private void SaveFailed(int token, string label, string message)
    {
        if (token != generation)
            return;
        FinishSourceWrite(false);
        operationBusy = false;
        saving = false;
        EndWork();
        SetStatus($"Could not save {label}: {message}");
        EmitStateChanged();
        SaveFinished?.Invoke(this, false);
        _ = ResolvePendingChanges("cancel");
    }

    private void FinishSourceWrite(bool success)
    {
        string rootPath = savingRootPath;
        savingRootPath = "";
        if (rootPath.Length > 0)
            SourceWriteFinished?.Invoke(this, new SourceWriteResult(rootPath, success));
    }

    private static bool IsFileError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or ArgumentException or InvalidDataException or InvalidOperationException or NotSupportedException
        or ValidationException;
}
