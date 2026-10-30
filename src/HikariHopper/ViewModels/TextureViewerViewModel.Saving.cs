using CommonFury;
using CommonFury.IO;
using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Infrastructure;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

public sealed partial class TextureViewerViewModel
{
    /// <summary>Writes bytes to a loose file atomically (Python <c>atomic_write_bytes</c>); replaceable by tests.</summary>
    internal Action<string, byte[]> WriteFile { get; set; } = (path, data) => AtomicFile.Write(path, data);

    /// <summary>Saves into the source: through the archive saver for RPF entries, otherwise to the loose file.</summary>
    public bool SaveYtd()
    {
        if (dictionary is null || OperationBusy)
            return false;
        Func<TextureSet, byte[]> serialize = Serializer();
        TextureSet snapshot = dictionary;
        if (sourceSaver is { } saver)
            return StartSave(sourceName, () => saver(serialize(snapshot)), prepare: sourcePrepare, rootPath: sourceRootPath);
        if (!CanSaveSource)
            return false;
        string destination = sourcePath;
        Action<string, byte[]> write = WriteFile;
        return StartSave(Path.GetFileName(destination), () => write(destination, serialize(snapshot)));
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
        string destination = string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
            ? path : Path.ChangeExtension(path, extension);
        Func<TextureSet, byte[]> serialize = Serializer();
        TextureSet snapshot = dictionary;
        Action<string, byte[]> write = WriteFile;
        return StartSave(Path.GetFileName(destination), () => write(destination, serialize(snapshot)),
            sourceUpdate: (destination, Path.GetFileName(destination)));
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

    private void ExtractTo(TextureSet textures, string folder)
    {
        IReadOnlyList<string> extracted;
        try
        {
            extracted = TextureDirectory.ExportDds(textures.ToDictionary(), folder);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            SetStatus($"Could not extract textures: {exception.Message}");
            return;
        }
        SetStatus($"Extracted {extracted.Count} textures");
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
        if (sourceUpdate is { } update)
        {
            (sourcePath, sourceName) = update;
            sourceSaver = null;
            sourcePrepare = null;
            sourceRootPath = "";
        }
        savedRevision = revision;
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
