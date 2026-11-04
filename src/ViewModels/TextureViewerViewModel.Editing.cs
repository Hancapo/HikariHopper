using CommonFury;
using FiveFury.Textures;
using HikariHopper.Infrastructure;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

public sealed partial class TextureViewerViewModel
{
    private static readonly Dictionary<string, string> UndoLabels = new(StringComparer.Ordinal)
    {
        ["Resizing"] = "Resize texture",
        ["Recalculating mipmaps for"] = "Recalculate mipmaps",
        ["Changing format for"] = "Change texture format",
        ["Replacing"] = "Replace texture",
        ["Optimizing compression for"] = "Optimize compression",
    };

    /// <summary>DXT optimization transform (Python <c>optimize_texture_compression</c>); replaceable by tests.</summary>
    internal Func<Texture, Texture> OptimizeCompression { get; set; } = TextureCompression.Optimize;

    /// <summary>Clipboard image conversion (Python <c>texture_from_clipboard_image</c>); replaceable by tests.</summary>
    internal Func<RgbaImage, string, Texture> TextureFromClipboardImage { get; set; } = TextureImport.TextureFromClipboardImage;

    /// <summary>Resizes every selected texture; <paramref name="filterName"/> is a mip filter such as <c>mitchell</c>.</summary>
    public bool ResizeSelected(int width, int height, string filterName, int minMipSize, bool generateMipmaps)
    {
        if (!ValidDimensions(width, height))
            return false;
        ImageFilter filter = TextureOperations.MipFilter(filterName);
        int stop = TextureDimensions.MipStopSize(width, height, minMipSize);
        return StartTransform("Resizing", texture => TextureOperations.Resize(texture, width, height, filter, stop, generateMipmaps));
    }

    /// <summary>Rounds every selected texture to powers of two (<c>nearest</c>, <c>down</c> or <c>up</c>).</summary>
    public bool ResizeSelectionToPowerOfTwo(string mode, string filterName, int minMipSize, bool generateMipmaps)
    {
        if (!ValidPowerOfTwoSelection(mode))
        {
            SetStatus("A rounded dimension exceeds the limit; choose Round down");
            return false;
        }
        ImageFilter filter = TextureOperations.MipFilter(filterName);
        return StartTransform("Resizing", texture =>
            TextureOperations.ResizeToPowerOfTwo(texture, mode, filter, minMipSize, generateMipmaps));
    }

    public bool RecalculateSelectedMipmaps(string filterName, int minMipSize)
    {
        if (SelectedRecord() is null)
            return false;
        ImageFilter filter = TextureOperations.MipFilter(filterName);
        return StartTransform("Recalculating mipmaps for", texture => TextureOperations.RecalculateMipmaps(texture, filter, minMipSize));
    }

    /// <summary>Re-encodes DXT1/3/5 textures of the selection as DXT1 or DXT5, whichever their alpha needs.</summary>
    public bool OptimizeSelectedCompression()
    {
        var sources = SelectionRecords().Where(record => TextureFormatNames.IsDxt(record.Item.Texture.Format))
            .Select(record => (record.Row, record.Item.Texture)).ToArray();
        Func<Texture, Texture> optimize = OptimizeCompression;
        return StartTransform("Optimizing compression for", texture => optimize(texture), sources);
    }

    /// <summary>Converts the selection to one of <see cref="FormatOptions"/> (<c>BC1</c>…<c>A8R8G8B8</c>).</summary>
    public bool ChangeSelectedFormat(string formatName, double quality, string filterName, int minMipSize)
    {
        string normalized = formatName.ToUpperInvariant();
        if (!TextureFormatNames.Editable.Contains(normalized))
        {
            SetStatus($"Unsupported texture format: {formatName}");
            return false;
        }
        if (SelectedRecord() is null)
            return false;
        ImageFilter filter = TextureOperations.MipFilter(filterName);
        float clamped = (float)Math.Max(0.0, Math.Min(1.0, quality));
        return StartTransform("Changing format for", texture => TextureOperations.ChangeFormat(texture,
            TextureFormatNames.FromEditable(normalized, texture.Format), clamped, filter, minMipSize));
    }

    /// <summary>Asks for an image and replaces the single selected texture with it, keeping its size, format and mips.</summary>
    public async Task<bool> ReplaceSelectedFromImage()
    {
        if (SelectedRecord() is not { } record || OperationBusy || SelectedCount != 1)
            return false;
        IReadOnlyList<string> paths = await Dialogs.PickOpenFilesAsync("Replace texture from image",
            [TextureImport.ImageFileFilter]);
        if (paths.Count == 0 || SelectedRecord() != record || OperationBusy)
            return false;
        string path = paths[0];
        Texture texture = record.Texture;
        bool generate = texture.MipCount > 1;
        int stop = Math.Max(1, Math.Max(texture.Width, texture.Height) >> Math.Max(0, texture.MipCount - 1));
        return StartTransform("Replacing", source =>
            TextureOperations.ReplaceFromImage(source, path, texture.Width, texture.Height, generate, stop));
    }

    /// <summary>Asks for image files and imports them as new textures named after the files, as dropping them does.</summary>
    public async Task<bool> ImportImagesFromFiles()
    {
        if (!CanImportImages)
            return false;
        IReadOnlyList<string> paths = await Dialogs.PickOpenFilesAsync("Import images", [TextureImport.ImageFileFilter],
            allowMultiple: true);
        return paths.Count > 0 && ImportDroppedImages(paths);
    }

    /// <summary>Reports dropped images that could not be read, before anything was imported.</summary>
    public void ReportImageImportFailure(string reason) => SetStatus($"Could not import images: {reason}");

    /// <summary>Whether every path is a local image file the open document can import.</summary>
    public bool CanAcceptImageDrop(IReadOnlyList<string> paths)
    {
        if (!CanImportImages || paths.Count == 0)
            return false;
        return paths.All(path => IsLocalPath(path) && TextureImport.IsImagePath(path));
    }

    /// <summary>Imports dropped images as textures named after the files. Name collisions first raise
    /// <see cref="ImageImportConfirmationRequested"/>.</summary>
    public bool ImportDroppedImages(IReadOnlyList<string> paths)
    {
        if (!CanAcceptImageDrop(paths))
            return false;
        string[] resolved;
        try
        {
            resolved = [.. paths.Select(ResolveDropPath)];
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException
            or NotSupportedException)
        {
            SetStatus($"Could not import images: {exception.Message}");
            return false;
        }
        var seen = new HashSet<string>(dictionary!.Textures.Select(texture => TextureSet.Lower(texture.Name)), StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in resolved)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (seen.Contains(name))
                conflicts.Add(name);
            seen.Add(name);
        }
        if (conflicts.Count > 0)
        {
            pendingImageImport = new PendingImageImport(generation, revision, resolved, conflicts.Count);
            EmitStateChanged();
            ImageImportConfirmationRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return StartImageImport(resolved);
    }

    /// <summary>Answers <see cref="ImageImportConfirmationRequested"/>.</summary>
    public bool ConfirmImageImport(bool accepted)
    {
        PendingImageImport? pending = pendingImageImport;
        pendingImageImport = null;
        EmitStateChanged();
        if (pending is null || !accepted)
            return false;
        if (pending.Generation != generation || pending.Revision != revision || !CanImportImages)
        {
            SetStatus("The document changed; import the images again");
            return false;
        }
        return StartImageImport(pending.Paths);
    }

    /// <summary>Renames the single selected texture; names are unique ignoring case.</summary>
    public bool RenameSelected(string name)
    {
        TextureItem? record = SelectedRecord();
        string normalized = name.Trim();
        if (record is null || OperationBusy || SelectedCount != 1)
            return false;
        if (normalized.Length == 0)
        {
            SetStatus("Texture name cannot be empty");
            return false;
        }
        bool duplicate = TexturesModel.Where((_, row) => row != selectedIndex)
            .Any(candidate => string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            SetStatus($"A texture named {normalized} already exists");
            return false;
        }
        Texture renamed = TextureOperations.WithName(record.Texture, normalized);
        if (!ReplaceDictionaryTexture(selectedIndex, renamed))
            return false;
        PushUndo("Rename texture", TextureUndoKind.Replace, selectedIndex, [record.Texture]);
        if (ydrDocument is not null)
        {
            IReadOnlySet<string> deletedBefore = undoStack[^1].DeletedOrigins;
            foreach (Texture original in ydrDocument.Dictionary.Textures)
            {
                string originalName = original.Name ?? "";
                if (deletedBefore.Contains(originalName) || textureRenames.GetValueOrDefault(originalName, originalName) != record.Name)
                    continue;
                deletedTextureOrigins.Remove(originalName);
                if (normalized == originalName)
                    textureRenames.Remove(originalName);
                else
                    textureRenames[originalName] = normalized;
            }
        }
        ReplaceModelTexture(selectedIndex, renamed);
        SetStatus($"Renamed {record.Name} to {normalized}");
        EmitStateChanged();
        EmitSelectionChanged();
        return true;
    }

    /// <summary>Keeps the first texture of each name (ignoring case); one undo step.</summary>
    public bool RemoveDuplicatesByName()
    {
        if (dictionary is null || OperationBusy || DuplicateTextureCount == 0)
            return false;
        TextureSet original = dictionary;
        TextureSet cleaned = original.WithoutDuplicates();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < cleaned.Textures.Length; index++)
            positions[TextureSet.Lower(cleaned.Textures[index].Name)] = index;
        var selection = selectedRows.Select(row => positions[TextureSet.Lower(original.Textures[row].Name)]).ToArray();
        int current = selectedIndex >= 0 ? positions[TextureSet.Lower(original.Textures[selectedIndex].Name)] : -1;
        if (!ReplaceDictionary(cleaned.Textures))
            return false;
        PushUndo("Remove duplicates by name", TextureUndoKind.Snapshot, selectedIndex, original.Textures);
        RebuildTextureView(selection, current);
        int removed = original.Textures.Length - cleaned.Textures.Length;
        SetStatus($"Removed {removed} duplicate {(removed == 1 ? "texture" : "textures")}");
        return true;
    }

    /// <summary>Removes the selected textures; one undo step.</summary>
    public bool RemoveSelected()
    {
        if (SelectedRecord() is not { } record || OperationBusy)
            return false;
        int row = selectedIndex;
        TextureSet original = dictionary!;
        Texture[] textures = [.. original.Textures.Where((_, index) => !selectedRows.Contains(index))];
        int count = SelectedCount;
        // Removal preserves surviving objects verbatim. Pre-existing duplicate names or malformed neighbours must not
        // prevent repairing a document. Save still validates; undo restores even an invalid input.
        dictionary = new TextureSet(textures, original.Target);
        string label = count == 1 ? "Remove texture" : "Remove textures";
        if (count > 1 || textures.Length == 0)
            PushUndo(label, TextureUndoKind.Snapshot, row, original.Textures);
        else
            PushUndo(label, TextureUndoKind.Insert, row, [record.Texture]);
        int current = Math.Min(row, textures.Length - 1);
        RebuildTextureView(textures.Length > 0 ? [current] : [], current);
        SetStatus(textures.Length == 0 ? "Texture dictionary is empty"
            : count == 1 ? $"Removed {record.Name}" : $"Removed {count} textures");
        return true;
    }

    /// <summary>Reverts the most recent edit, restoring its selection and the modified state from before it.</summary>
    public bool Undo()
    {
        if (undoStack.Count == 0 || OperationBusy || dictionary is null)
            return false;
        TextureUndoAction action = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        List<Texture> textures = [.. dictionary.Textures];
        if (action.Kind == TextureUndoKind.Snapshot)
        {
            // Undo restores an imported state verbatim, including duplicate names. Serialization still validates the
            // document before writing it.
            dictionary = new TextureSet(action.Textures, dictionary.Target);
            RestoreYdrBookkeeping(action);
            revision = action.PreviousRevision;
            RebuildTextureView(action.Selection, action.Row);
            SetStatus($"Undid {action.Label.ToLowerInvariant()}");
            return true;
        }
        if (action.Kind == TextureUndoKind.Replace)
        {
            if (action.Row < 0 || action.Row >= textures.Count)
                return false;
            textures[action.Row] = action.Textures[0];
            if (!ReplaceDictionary(textures))
            {
                undoStack.Add(action);
                return false;
            }
        }
        else
        {
            textures.Insert(Math.Clamp(action.Row, 0, textures.Count), action.Textures[0]);
            // Restore the removed object without revalidating unrelated source problems.
            dictionary = new TextureSet(textures, dictionary.Target);
        }
        RestoreYdrBookkeeping(action);

        generation++;
        pool.Clear();
        if (action.Kind == TextureUndoKind.Replace)
            ReplaceModelTexture(action.Row, action.Textures[0]);
        else
            InsertModelTexture(action.Row, action.Textures[0]);
        revision = action.PreviousRevision;
        selectedIndex = action.Row;
        selectedRows = [action.Row];
        selectionAnchor = action.Row;
        ApplyModelSelection();
        mipLevel = 0;
        ClearPreview();
        previewLoading = true;
        SetStatus($"Undid {action.Label.ToLowerInvariant()}");
        EmitStateChanged();
        EmitSelectionChanged();
        RequestPreview();
        if (action.Kind == TextureUndoKind.Replace)
            StartImageTask(action.Row, action.Textures[0], "rgba", 0, thumbnail: true);
        else
            RequestAllThumbnails();
        return true;
    }

    /// <summary>Reads the clipboard image and raises <see cref="PasteImageRequested"/> to choose between adding it and
    /// replacing the single selected texture.</summary>
    public async Task<bool> RequestPasteImage()
    {
        if (!CanPasteImage)
            return false;
        int token = generation;
        RgbaImage? image = await clipboard.GetImageAsync();
        if (token != generation || !CanPasteImage)
            return false;
        if (image is null)
        {
            SetStatus("Clipboard does not contain a readable image");
            return false;
        }
        TextureItem? record = SelectedCount == 1 ? SelectedRecord() : null;
        pendingPaste = new PasteRequest(generation, revision, record is not null ? selectedIndex : -1, image, record?.Texture);
        EmitStateChanged();
        PasteImageRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void CancelPasteImage()
    {
        pendingPaste = null;
        EmitStateChanged();
    }

    /// <summary>Validation message for a new texture name, or empty when it is acceptable.</summary>
    public string PasteNameError(string name)
    {
        string normalized = name.Trim();
        if (normalized.Length == 0)
            return "Enter a texture name";
        if (normalized.Length > 255 || normalized.Any(character => character < 32 || character == 127))
            return "Use up to 255 characters without control characters";
        if (dictionary is not null && dictionary.Textures.Any(texture => TextureSet.Lower(texture.Name) == normalized.ToLowerInvariant()))
            return "A texture with this name already exists";
        return "";
    }

    /// <summary>Completes a pending paste: replaces the selected texture or adds one named <paramref name="name"/>.</summary>
    public bool PasteImage(bool replaceSelected, string name)
    {
        PasteRequest? request = pendingPaste;
        if (request is null || OperationBusy)
            return false;
        if (request.Generation != generation || request.Revision != revision || !HasDocument)
        {
            CancelPasteImage();
            SetStatus("The document changed; paste the image again");
            return false;
        }
        if (replaceSelected)
        {
            if (request.Source is null)
                return false;
            name = request.Source.Name ?? "";
        }
        else
        {
            string message = PasteNameError(name);
            if (message.Length > 0)
            {
                SetStatus(message);
                return false;
            }
            name = name.Trim();
        }
        pendingPaste = null;
        operationBusy = true;
        SetStatus($"Pasting {name}…");
        EmitStateChanged();
        Func<RgbaImage, string, Texture> convert = TextureFromClipboardImage;
        pool.Start(() =>
        {
            Texture texture;
            try
            {
                Texture decoded = convert(request.Image, name);
                texture = TextureOperations.Adopt(decoded, replaceSelected ? request.Source : null);
            }
            catch (Exception exception)
            {
                Post(() => TransformFailed(request.Generation, "Paste", exception.Message));
                return;
            }
            Post(() => PasteCompleted(request.Generation, replaceSelected ? request.Row : -1, texture));
        }, PriorityTransform);
        return true;
    }

    /// <summary>Copies the base mip of the single selected texture to the clipboard as RGBA.</summary>
    public bool CopySelectedImage()
    {
        if (SelectedRecord() is not { } record || SelectedCount != 1 || OperationBusy)
            return false;
        operationBusy = true;
        SetStatus($"Copying {record.Name}…");
        EmitStateChanged();
        // Reuse background decoding, but deliver the full RGBA base mip to the clipboard rather than to the preview.
        int token = generation;
        Texture texture = record.Texture;
        Func<Texture, string, int, RgbaImage> decode = DecodeImage;
        pool.Start(() =>
        {
            RgbaImage image;
            try
            {
                image = decode(texture, "rgba", 0);
            }
            catch (Exception exception)
            {
                Post(() => CopyImageFailed(token, exception.Message));
                return;
            }
            Post(() => _ = CopyImageCompleted(token, image));
        }, PriorityTransform);
        return true;
    }

    private async Task CopyImageCompleted(int token, RgbaImage image)
    {
        if (token != generation)
            return;
        operationBusy = false;
        EmitStateChanged();
        bool copied;
        try
        {
            copied = await clipboard.SetImageAsync(image);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            copied = false;
        }
        SetStatus(copied ? $"Copied {SelectedName} to clipboard" : "Could not copy image to clipboard");
        EmitStateChanged();
    }

    private void CopyImageFailed(int token, string message)
    {
        if (token != generation)
            return;
        operationBusy = false;
        SetStatus($"Could not copy image: {message}");
        EmitStateChanged();
    }

    private void PasteCompleted(int token, int row, Texture texture)
    {
        if (token != generation)
            return;
        operationBusy = false;
        TextureSet original = dictionary!;
        List<Texture> textures = [.. original.Textures];
        if (row < 0)
        {
            row = textures.Count;
            textures.Add(texture);
        }
        else
            textures[row] = texture;
        if (!ReplaceDictionary(textures))
        {
            EmitStateChanged();
            return;
        }
        PushUndo("Paste image", TextureUndoKind.Snapshot, selectedIndex, original.Textures);
        RebuildTextureView([row], row);
        SetStatus($"Pasted {texture.Name}");
    }

    private bool StartImageImport(IReadOnlyList<string> paths)
    {
        operationBusy = true;
        SetStatus($"Importing {paths.Count} images…");
        EmitStateChanged();
        int token = generation;
        Texture[] original = [.. dictionary!.Textures];
        pool.Start(() =>
        {
            var sources = new Dictionary<string, Texture>(StringComparer.Ordinal);
            foreach (Texture texture in original)
                sources.TryAdd(TextureSet.Lower(texture.Name), texture);
            // Ordered by first occurrence; a later file with the same name replaces the earlier texture in place.
            var order = new List<string>();
            var imported = new Dictionary<string, Texture>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                try
                {
                    Texture decoded = TextureImport.ReadTextureImage(path);
                    string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!imported.ContainsKey(key))
                        order.Add(key);
                    imported[key] = TextureOperations.Adopt(decoded, sources.GetValueOrDefault(key));
                }
                catch (Exception exception)
                {
                    string message = $"{Path.GetFileName(path)}: {exception.Message}";
                    Post(() => TransformFailed(token, "Import", message));
                    return;
                }
            }
            var result = order.Select(key => KeyValuePair.Create(key, imported[key])).ToArray();
            Post(() => ImageImportCompleted(token, result));
        }, PriorityTransform);
        return true;
    }

    private void ImageImportCompleted(int token, IReadOnlyList<KeyValuePair<string, Texture>> imported)
    {
        if (token != generation)
            return;
        operationBusy = false;
        TextureSet original = dictionary!;
        var textures = new List<Texture>();
        var selected = new SortedSet<int>();
        var keys = imported.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var remaining = imported.ToList();
        foreach (Texture texture in original.Textures)
        {
            string key = TextureSet.Lower(texture.Name);
            if (!keys.Contains(key))
                textures.Add(texture);
            else if (remaining.FindIndex(pair => pair.Key == key) is var index and >= 0)
            {
                selected.Add(textures.Count);
                textures.Add(remaining[index].Value);
                remaining.RemoveAt(index);
            }
        }
        foreach (var pair in remaining)
        {
            selected.Add(textures.Count);
            textures.Add(pair.Value);
        }
        if (!ReplaceDictionary(textures))
        {
            EmitStateChanged();
            return;
        }
        PushUndo("Import textures", TextureUndoKind.Snapshot, selectedIndex, original.Textures);
        RebuildTextureView(selected, selected.Min);
        SetStatus($"Imported {imported.Count} textures");
    }

    /// <summary>Runs <paramref name="transform"/> on each source in the background and commits all results as one
    /// undo step, or none when any texture fails (Python <c>_start_transform</c>).</summary>
    internal bool StartTransform(string operation, Func<Texture, Texture> transform,
        IReadOnlyList<(int Row, Texture Texture)>? sources = null)
    {
        if (SelectedRecord() is not { } record || OperationBusy || selectedRows.Count == 0)
            return false;
        sources ??= [.. SelectionRecords().Select(item => (item.Row, item.Item.Texture))];
        if (sources.Count == 0)
            return false;
        operationBusy = true;
        string label = SelectedCount == 1 ? record.Name : $"{SelectedCount} textures";
        SetStatus($"{operation} {label}…");
        EmitStateChanged();
        int token = generation;
        pool.Start(() =>
        {
            var results = new List<(int Row, Texture Texture)>();
            foreach ((int row, Texture source) in sources)
            {
                try
                {
                    Texture transformed = transform(source);
                    if (!ReferenceEquals(transformed, source))
                        results.Add((row, TextureOperations.CarryMetadata(transformed, source)));
                }
                catch (Exception exception)
                {
                    string message = $"{source.Name}: {exception.Message}";
                    Post(() => TransformFailed(token, operation, message));
                    return;
                }
            }
            Post(() => TransformCompleted(token, operation, results));
        }, PriorityTransform);
        return true;
    }

    private void TransformCompleted(int token, string operation, IReadOnlyList<(int Row, Texture Texture)> results)
    {
        if (token != generation)
            return;
        operationBusy = false;
        if (results.Count == 0)
        {
            SetStatus("No changes were needed");
            EmitStateChanged();
            return;
        }
        TextureSet original = dictionary!;
        List<Texture> textures = [.. original.Textures];
        foreach ((int row, Texture texture) in results)
            textures[row] = texture;
        if (!ReplaceDictionary(textures))
        {
            EmitStateChanged();
            return;
        }
        PushUndo(UndoLabels.GetValueOrDefault(operation, "Texture change"), TextureUndoKind.Snapshot, selectedIndex, original.Textures);
        RebuildTextureView(selectedRows, selectedIndex);
        SetStatus($"Finished {operation.ToLowerInvariant()} {results.Count} {(results.Count == 1 ? "texture" : "textures")}");
    }

    private void TransformFailed(int token, string operation, string message)
    {
        if (token != generation)
            return;
        operationBusy = false;
        SetStatus($"{operation} failed: {message}");
        EmitStateChanged();
    }

    private void RebuildTextureView(IEnumerable<int> rows, int current)
    {
        int[] selection = [.. rows];
        generation++;
        pool.Clear();
        SetTextures(dictionary!.Textures);
        selectionAnchor = current;
        SetTextureSelection(selection, current);
        RequestAllThumbnails();
    }

    private bool ReplaceDictionaryTexture(int row, Texture texture)
    {
        if (dictionary is null || row < 0 || row >= dictionary.Textures.Length)
            return false;
        List<Texture> textures = [.. dictionary.Textures];
        textures[row] = texture;
        return ReplaceDictionary(textures);
    }

    private bool ReplaceDictionary(IEnumerable<Texture> textures)
    {
        if (dictionary is null)
            return false;
        var candidate = new TextureSet(textures, dictionary.Target);
        try
        {
            candidate.Validate().ThrowIfInvalid();
        }
        catch (ValidationException exception)
        {
            SetStatus($"Texture change is invalid: {exception.Message}");
            return false;
        }
        dictionary = candidate;
        return true;
    }

    private void PushUndo(string label, TextureUndoKind kind, int row, IReadOnlyList<Texture> textures)
    {
        undoStack.Add(new TextureUndoAction(label, kind, row, [.. textures], revision, [.. selectedRows],
            [.. textureRenames], new HashSet<string>(deletedTextureOrigins, StringComparer.Ordinal)));
        if (ydrDocument is not null)
        {
            var currentNames = new HashSet<string>(dictionary!.Textures.Select(texture => texture.Name ?? ""), StringComparer.Ordinal);
            foreach (Texture original in ydrDocument.Dictionary.Textures)
            {
                string name = original.Name ?? "";
                if (!currentNames.Contains(textureRenames.GetValueOrDefault(name, name)))
                    deletedTextureOrigins.Add(name);
            }
        }
        nextRevision++;
        revision = nextRevision;
        long retained = undoStack.Sum(action => action.DataSize);
        while (undoStack.Count > 1 && (undoStack.Count > UndoLimit || retained > UndoDataLimit))
        {
            retained -= undoStack[0].DataSize;
            undoStack.RemoveAt(0);
        }
    }

    private void RestoreYdrBookkeeping(TextureUndoAction action)
    {
        textureRenames = new Dictionary<string, string>(action.Renames, StringComparer.Ordinal);
        deletedTextureOrigins = new HashSet<string>(action.DeletedOrigins, StringComparer.Ordinal);
    }

    private bool ValidDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            SetStatus("Texture dimensions must be positive");
            return false;
        }
        if (width > MaximumDimension || height > MaximumDimension)
        {
            SetStatus($"Texture dimensions cannot exceed {MaximumDimension}");
            return false;
        }
        return true;
    }

    private static bool IsLocalPath(string path) =>
        !string.IsNullOrEmpty(path) && !path.Contains("://", StringComparison.Ordinal) && Path.IsPathFullyQualified(path);

    private static string ResolveDropPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full))
            throw new ArgumentException($"Only files can be imported: {Path.GetFileName(full)}");
        if (!File.Exists(full))
            throw new FileNotFoundException($"No such file or directory: '{full}'");
        return full;
    }
}
