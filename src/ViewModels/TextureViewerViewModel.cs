using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FiveFury;
using FiveFury.Textures;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

/// <summary>Port of <c>texture_viewer.py::TextureViewerBridge</c>: the YTD/YDR texture editor of one explorer tab.</summary>
/// <remarks>Background work runs on a two-thread priority queue; results return to the UI thread and are discarded when
/// the generation counter changed meanwhile. All state is touched on the UI thread only.</remarks>
public sealed partial class TextureViewerViewModel : ObservableObject, ITexturePreviewSource
{
    private const int UndoLimit = 24;
    private const int PriorityThumbnail = 0, PriorityMips = 1, PriorityPreview = 2, PriorityTransform = 3, PrioritySave = 4;

    // Python stateChanged / selectionChanged notify signals.
    private static readonly string[] StateProperties =
    [
        nameof(SourceName), nameof(IsDrawable), nameof(SourcePath), nameof(TextureCount), nameof(HasDocument),
        nameof(CanImportImages), nameof(CanPasteImage), nameof(PastePending), nameof(PasteCanReplace), nameof(PasteTargetName),
        nameof(ImportConfirmationPending), nameof(ImportConflictCount), nameof(ImportFileCount), nameof(Loading),
        nameof(PreviewLoading), nameof(OperationBusy), nameof(Saving), nameof(Modified), nameof(CanUndo), nameof(UndoLabel),
        nameof(CanSaveSource), nameof(MaximumDimension), nameof(Error), nameof(DuplicateTextureCount),
    ];
    private static readonly string[] SelectionProperties =
    [
        nameof(SelectedIndex), nameof(SelectedCount), nameof(CanRemoveSelection), nameof(CanOptimizeCompression),
        nameof(SelectedName), nameof(SelectedDimensions), nameof(SelectedWidth), nameof(SelectedHeight),
        nameof(SelectedNeedsPowerOfTwo), nameof(PreviewWidth), nameof(PreviewHeight), nameof(SelectedFormat),
        nameof(SelectedUsage), nameof(SelectedDataSize), nameof(MipCount), nameof(MipLevel), nameof(Channel),
        nameof(PreviewUrl), nameof(PreviewImage),
    ];
    // Conveniences that combine both groups for compiled bindings.
    private static readonly string[] DerivedProperties =
    [
        nameof(HasSelection), nameof(CanEditSingle), nameof(CanEditSelection), nameof(CanResizeToPowerOfTwo),
        nameof(CanOptimizeSelection), nameof(CanExtractSelection), nameof(CanRemove), nameof(CanRemoveDuplicates),
        nameof(CanSave), nameof(CanSaveAs), nameof(CanExtractAll), nameof(HasMipChain), nameof(HasPreview),
        nameof(ShowPreviewPlaceholder),
    ];

    private readonly PriorityWorkQueue pool = new(2);
    private readonly IClipboardService clipboard;
    private IFileDialogs? dialogs;
    private int generation;
    private string session = "";
    private TextureSet? dictionary;
    private YdrTextureDocument? ydrDocument;
    private Dictionary<string, string> textureRenames = new(StringComparer.Ordinal);
    private HashSet<string> deletedTextureOrigins = new(StringComparer.Ordinal);
    private string sourceName = "", sourcePath = "", sourceRootPath = "";
    private Action<byte[]>? sourceSaver;
    // Told what the document last saved when it closes (or another replaces it), if it saved anything.
    private Action<SavedDocument>? closedAfterSave;
    private SavedDocument? lastSave;
    // What the save running now wrote, set on the work thread before it reports back.
    private volatile SavedDocument? savedNow;
    private Action? sourcePrepare;
    private int selectedIndex = -1;
    private SortedSet<int> selectedRows = [];
    private int selectionAnchor = -1;
    private string channel = "rgba";
    private int mipLevel;
    private string previewUrl = "";
    private Bitmap? previewImage;
    private Texture? previewTexture;
    private int previewRevision;
    // The selected texture's decoded RGBA, per mip, and every view of it made so far (RGBA or one channel, per
    // mip): moving between mips or channels shows one at once when it is here, and a channel is made from the
    // RGBA instead of being decoded again.
    private readonly Dictionary<int, RgbaImage> sources = [];
    private readonly Dictionary<(string Channel, int Mip), Bitmap> views = [];
    private readonly HashSet<(string Channel, int Mip)> viewsRequested = [];
    private Texture? viewsTexture;
    private bool loading, previewLoading, operationBusy, saving, externalWriteBusy;
    private string savingRootPath = "";
    private readonly List<TextureUndoAction> undoStack = [];
    private int revision, savedRevision, nextRevision;
    private string error = "";
    private string status = "No texture dictionary loaded";
    private Action? pendingChange;
    private bool changeAfterSave;
    private PendingImageImport? pendingImageImport;
    private PasteRequest? pendingPaste;

    public TextureViewerViewModel() : this(AppServices.Clipboard)
    {
    }

    /// <summary>Uses <paramref name="clipboard"/> instead of <see cref="AppServices.Clipboard"/>.</summary>
    internal TextureViewerViewModel(IClipboardService clipboard)
    {
        this.clipboard = clipboard;
        session = Guid.NewGuid().ToString("N");
        clipboard.Changed += ClipboardChanged;
        FormatOptions = [.. TextureFormatNames.Editable.Select(name =>
            new TextureFormatOption(Formatting.FormatTextureFormat(name, includeExact: true), name))];
    }

    /// <summary>
    /// Raised when a document starts loading, so the window can be shown. A YDR is read first: it is only
    /// worth showing when it embeds textures.
    /// </summary>
    public event EventHandler? OpenRequested;

    /// <summary>A YDR was not opened because it embeds no textures; the argument says so, for the explorer.</summary>
    public event EventHandler<string>? OpenDeclined;

    /// <summary>Raised after a save into the source finishes (Qt <c>sourceWriteFinished(str, bool)</c>), only for saves
    /// that were given a source root path.</summary>
    public event EventHandler<SourceWriteResult>? SourceWriteFinished;

    /// <summary>Raised when any save finishes; the argument is its success.</summary>
    public event EventHandler<bool>? SaveFinished;

    /// <summary>Asks the UI to show the unsaved-changes dialog, answered with <see cref="ResolvePendingChanges"/>.</summary>
    public event EventHandler? UnsavedChangesRequested;

    /// <summary>Raised after <see cref="CloseDocument"/>, so the window can close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Asks the UI to confirm replacing textures with dropped images, answered with <see cref="ConfirmImageImport"/>.</summary>
    public event EventHandler? ImageImportConfirmationRequested;

    /// <summary>Asks the UI to show the paste dialog, answered with <see cref="PasteImage"/> or <see cref="CancelPasteImage"/>.</summary>
    public event EventHandler? PasteImageRequested;

    /// <summary>The textures in document order.</summary>
    public ObservableCollection<TextureItem> TexturesModel { get; } = [];

    public string SourceName => sourceName;

    /// <summary>True when the open document is a YDR rather than a YTD.</summary>
    public bool IsDrawable => sourceName.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase);

    public string SourcePath => sourcePath;

    public int TextureCount => TexturesModel.Count;

    public bool HasDocument => dictionary is not null && !loading;

    public bool CanImportImages => HasDocument && !OperationBusy && pendingImageImport is null && pendingPaste is null;

    public bool CanPasteImage => CanImportImages && clipboard.HasImage;

    public bool PastePending => pendingPaste is not null;

    public bool PasteCanReplace => pendingPaste?.Source is not null;

    public string PasteTargetName => PasteCanReplace ? pendingPaste!.Source!.Name ?? "" : "";

    public bool ImportConfirmationPending => pendingImageImport is not null;

    public int ImportConflictCount => pendingImageImport?.ConflictCount ?? 0;

    public int ImportFileCount => pendingImageImport?.Paths.Count ?? 0;

    public bool Loading => loading;

    public bool PreviewLoading => previewLoading;

    public bool OperationBusy => operationBusy || externalWriteBusy;

    /// <summary>True while a save is running; changes raise <c>PropertyChanged(nameof(Saving))</c>.</summary>
    public bool Saving => saving;

    public bool Modified => revision != savedRevision;

    public bool CanUndo => undoStack.Count > 0 && !operationBusy;

    public string UndoLabel => undoStack.Count > 0 ? undoStack[^1].Label : "";

    public bool CanSaveSource => sourceSaver is not null || sourcePath.Length > 0 && !sourcePath.Contains("::", StringComparison.Ordinal);

    /// <summary>The formats offered by the Change format dialog.</summary>
    public IReadOnlyList<TextureFormatOption> FormatOptions { get; }

    public int MaximumDimension => dictionary?.Target == GameTarget.Enhanced ? 0xFFFF : 0x7FFF;

    public string Error => error;

    public string Status => status;

    public int SelectedIndex => selectedIndex;

    public int SelectedCount => selectedRows.Count;

    public int DuplicateTextureCount => dictionary?.DuplicateCount ?? 0;

    public bool CanRemoveSelection => SelectedCount > 0;

    public bool CanOptimizeCompression => SelectionRecords().Any(record => TextureFormatNames.IsDxt(record.Item.Texture.Format));

    public string SelectedName => SelectedRecord()?.Name ?? "";

    public string SelectedDimensions => SelectedRecord()?.Dimensions ?? "";

    public int SelectedWidth => SelectedRecord()?.Texture.Width ?? 0;

    public int SelectedHeight => SelectedRecord()?.Texture.Height ?? 0;

    public bool SelectedNeedsPowerOfTwo => SelectionRecords()
        .Any(record => !TextureDimensions.IsPowerOfTwo(record.Item.Texture.Width, record.Item.Texture.Height));

    public int PreviewWidth => Math.Max(1, SelectedWidth >> mipLevel);

    public int PreviewHeight => Math.Max(1, SelectedHeight >> mipLevel);

    public string SelectedFormat => SelectedRecord() is { } record ? Formatting.FormatTextureFormat(record.ExactFormatName, includeExact: true) : "";

    public string SelectedUsage => SelectedRecord()?.Usage ?? "";

    public string SelectedDataSize => SelectedRecord() is { } record ? Formatting.FormatSize(record.DataSize) : "";

    public int MipCount => SelectedRecord()?.MipCount ?? 0;

    public int MipLevel => mipLevel;

    /// <summary><c>rgba</c>, <c>r</c>, <c>g</c>, <c>b</c> or <c>a</c>.</summary>
    public string Channel => channel;

    /// <summary>Key of the decoded preview; empty while none is available (QML <c>previewUrl</c>).</summary>
    public string PreviewUrl => previewUrl;

    /// <summary>The decoded preview of the current texture, mip and channel (premultiplied BGRA), or null when
    /// <see cref="PreviewUrl"/> is empty.</summary>
    public Bitmap? PreviewImage => previewImage;

    public bool HasSelection => selectedIndex >= 0;

    /// <summary>Rename, replace from image and copy image: exactly one texture and no running operation.</summary>
    public bool CanEditSingle => SelectedCount == 1 && !OperationBusy;

    /// <summary>Resize, mipmaps, format and alpha repair: a current texture and no running operation.</summary>
    public bool CanEditSelection => HasSelection && !OperationBusy;

    public bool CanResizeToPowerOfTwo => SelectedNeedsPowerOfTwo && !OperationBusy;

    public bool CanOptimizeSelection => CanOptimizeCompression && !OperationBusy;

    public bool CanExtractSelection => SelectedCount > 0 && !OperationBusy;

    public bool CanRemove => CanRemoveSelection && !OperationBusy;

    public bool CanRemoveDuplicates => DuplicateTextureCount > 0 && !OperationBusy;

    public bool CanSave => HasDocument && !OperationBusy && CanSaveSource;

    public bool CanSaveAs => HasDocument && !OperationBusy;

    public bool CanExtractAll => TextureCount > 0;

    public bool HasMipChain => MipCount > 1;

    public bool HasPreview => previewUrl.Length > 0;

    public bool ShowPreviewPlaceholder => previewLoading || selectedIndex < 0;

    /// <summary>Blocks edits while another explorer operation writes files (Python <c>set_external_write_busy</c>).</summary>
    public void SetExternalWriteBusy(bool busy)
    {
        if (busy == externalWriteBusy)
            return;
        externalWriteBusy = busy;
        EmitStateChanged();
    }

    /// <summary>
    /// Opens a YTD/YDR after resolving unsaved changes (Python <c>open_dictionary</c>). <paramref name="closedAfterSave"/>
    /// is told what it last saved when it closes, or another document replaces it, if it saved anything: a model
    /// showing its textures shows them as saved.
    /// </summary>
    public void OpenDictionary(string name, string sourcePath, byte[] data,
        Action<byte[]>? sourceSaver = null, Action? sourcePrepare = null, string sourceRootPath = "",
        Action<SavedDocument>? closedAfterSave = null)
    {
        byte[] copy = data.ToArray();
        RequestDocumentChange(() =>
        {
            OpenDictionaryNow(name, sourcePath, copy, sourceSaver, sourcePrepare, sourceRootPath);
            this.closedAfterSave = closedAfterSave;
        });
    }

    /// <summary>Runs <paramref name="action"/> once unsaved changes are resolved (Python <c>request_document_change</c>).
    /// While saving, it runs after the save succeeds; with unsaved edits it waits for <see cref="ResolvePendingChanges"/>.</summary>
    public void RequestDocumentChange(Action action)
    {
        if (pendingChange is not null)
            return;
        if (operationBusy && !saving)
        {
            SetStatus("Wait for the texture operation to finish");
            return;
        }
        if (saving)
        {
            pendingChange = action;
            changeAfterSave = true;
        }
        else if (Modified)
        {
            pendingChange = action;
            UnsavedChangesRequested?.Invoke(this, EventArgs.Empty);
        }
        else
            action();
    }

    /// <summary>Answers <see cref="UnsavedChangesRequested"/> with <c>cancel</c>, <c>discard</c> or <c>save</c>.
    /// Saving may show the Save As dialog.</summary>
    public async Task ResolvePendingChanges(string decision)
    {
        if (decision == "cancel")
        {
            pendingChange = null;
            changeAfterSave = false;
            return;
        }
        if (pendingChange is null || operationBusy)
            return;
        if (decision == "discard")
            CompleteDocumentChange();
        else if (decision == "save")
        {
            changeAfterSave = true;
            bool started = CanSaveSource ? SaveYtd() : await SaveYtdAs();
            if (!started)
                await ResolvePendingChanges("cancel");
        }
    }

    /// <summary>Closes the document after resolving unsaved changes.</summary>
    public void RequestClose() => RequestDocumentChange(CloseDocument);

    /// <summary>Closes the open document without prompting (Python <c>close_document</c>).</summary>
    public void CloseDocument()
    {
        ResetDocument();
        EmitStateChanged();
        EmitSelectionChanged();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops background work before the owning tab is destroyed (Python <c>shutdown</c>).</summary>
    public void Shutdown()
    {
        generation++;
        pool.Clear();
        clipboard.Changed -= ClipboardChanged;
    }

    /// <summary>Generation-tagged results still pending are discarded; for tests.</summary>
    internal int Generation => generation;

    internal TextureSet? Dictionary => dictionary;

    internal YdrTextureDocument? YdrDocument => ydrDocument;

    internal IReadOnlyList<TextureUndoAction> UndoStack => undoStack;

    internal IReadOnlySet<int> SelectedRows => selectedRows;

    internal Action? PendingChange => pendingChange;

    /// <summary>File pickers; <see cref="AppServices.Dialogs"/> unless a test replaces them.</summary>
    internal IFileDialogs Dialogs
    {
        get => dialogs ?? AppServices.Dialogs;
        set => dialogs = value;
    }

    /// <summary>Retained undo bytes before the oldest entries are dropped (Python <c>_UNDO_DATA_LIMIT</c>).</summary>
    internal long UndoDataLimit { get; set; } = 512L * 1024 * 1024;

    /// <summary>Waits for queued background work; for tests.</summary>
    internal bool WaitForBackgroundWork(TimeSpan timeout) => pool.WaitForDone(timeout);

    internal void SetSourceForTesting(string name, string path)
    {
        sourceName = name;
        sourcePath = path;
        EmitStateChanged();
    }

    private void CompleteDocumentChange()
    {
        Action? action = pendingChange;
        pendingChange = null;
        changeAfterSave = false;
        if (action is not null)
            // A continuation can remove the tab and the window that raised this decision.
            Dispatcher.UIThread.Post(action);
    }

    private void OpenDictionaryNow(string name, string path, byte[] data, Action<byte[]>? saver, Action? prepare, string rootPath)
    {
        ResetDocument();
        sourceName = name;
        sourcePath = path;
        sourceSaver = saver;
        sourcePrepare = prepare;
        sourceRootPath = rootPath;
        loading = true;
        SetStatus("Reading texture dictionary…");
        EmitStateChanged();
        EmitSelectionChanged();
        bool drawable = IsDrawable;
        if (!drawable)
            OpenRequested?.Invoke(this, EventArgs.Empty);

        int token = generation;
        pool.Start(() =>
        {
            try
            {
                if (drawable)
                {
                    YdrTextureDocument document = YdrTextureDocument.Read(data);
                    Post(() => DrawableLoaded(token, document));
                }
                else
                {
                    TextureSet loaded = YtdSaving.Read(data);
                    Post(() => DictionaryLoaded(token, loaded));
                }
            }
            catch (Exception exception)
            {
                Post(() => DictionaryFailed(token, exception.Message));
            }
        }, PriorityPreview);
    }

    private void ResetDocument()
    {
        // The document ends: whoever asked is told what it saved, once.
        (Action<SavedDocument>? told, SavedDocument? saved) = (closedAfterSave, lastSave);
        (closedAfterSave, lastSave) = (null, null);
        if (told is not null && saved is not null)
            told(saved);
        pendingImageImport = null;
        pendingPaste = null;
        generation++;
        pool.Clear();
        session = Guid.NewGuid().ToString("N");
        dictionary = null;
        ydrDocument = null;
        textureRenames = new(StringComparer.Ordinal);
        deletedTextureOrigins = new(StringComparer.Ordinal);
        sourceName = "";
        sourcePath = "";
        sourceSaver = null;
        sourcePrepare = null;
        sourceRootPath = "";
        selectedIndex = -1;
        selectedRows = [];
        selectionAnchor = -1;
        ApplyModelSelection();
        channel = "rgba";
        mipLevel = 0;
        ClearPreview();
        previewRevision = 0;
        loading = false;
        previewLoading = false;
        operationBusy = false;
        saving = false;
        undoStack.Clear();
        revision = 0;
        savedRevision = 0;
        nextRevision = 0;
        error = "";
        SetTextures([]);
        SetStatus("No texture dictionary loaded");
    }

    private void DrawableLoaded(int token, YdrTextureDocument document)
    {
        if (token != generation)
            return;
        // A drawable with no textures of its own has nothing to edit here: don't open it at all.
        if (document.Dictionary.Textures.IsEmpty)
        {
            string name = sourceName;
            ResetDocument();
            OpenDeclined?.Invoke(this, $"{name} has no embedded textures");
            return;
        }
        ydrDocument = document;
        OpenRequested?.Invoke(this, EventArgs.Empty);
        DictionaryLoaded(token, document.Dictionary);
    }

    /// <summary>Python <c>_dictionary_loaded</c>; tests call it with the current <see cref="Generation"/>.</summary>
    internal void DictionaryLoaded(int token, TextureSet loaded)
    {
        if (token != generation)
            return;
        dictionary = loaded;
        SetTextures(loaded.Textures);
        loading = false;
        error = "";
        if (TexturesModel.Count == 0)
        {
            SetStatus("Texture dictionary is empty");
            EmitStateChanged();
            return;
        }
        selectedIndex = 0;
        selectedRows = [0];
        selectionAnchor = 0;
        ApplyModelSelection();
        previewLoading = true;
        SetStatus($"Loaded {TexturesModel.Count} textures");
        EmitStateChanged();
        EmitSelectionChanged();
        RequestPreview();
        RequestAllThumbnails();
    }

    private void DictionaryFailed(int token, string message)
    {
        if (token != generation)
            return;
        loading = false;
        previewLoading = false;
        error = message;
        SetStatus($"Could not read texture dictionary: {message}");
        // A YDR that couldn't be read is shown now, so the error is seen.
        if (IsDrawable)
            OpenRequested?.Invoke(this, EventArgs.Empty);
        EmitStateChanged();
    }

    private void RequestPreview()
    {
        if (SelectedRecord() is not { } record)
            return;
        SetStatus($"Decoding {record.Name} · mip {mipLevel}…");
        StartImageTask(selectedIndex, record.Texture, channel, mipLevel, thumbnail: false);
    }

    private void RequestAllThumbnails()
    {
        for (int row = 0; row < TexturesModel.Count; row++)
            StartImageTask(row, TexturesModel[row].Texture, "rgba", 0, thumbnail: true);
    }

    private void StartImageTask(int row, Texture texture, string mode, int mip, bool thumbnail, bool prefetch = false)
    {
        int token = generation;
        Func<Texture, string, int, RgbaImage> decode = DecodeImage;
        // A mip already decoded only needs its channel made, not another decode.
        RgbaImage? decoded = !thumbnail && ReferenceEquals(texture, viewsTexture) && sources.TryGetValue(mip, out RgbaImage? kept) ? kept : null;
        pool.Start(() =>
        {
            Bitmap bitmap;
            RgbaImage source;
            try
            {
                source = decoded ?? decode(texture, "rgba", mip);
                bitmap = TextureBitmaps.Create(thumbnail ? TextureBitmaps.Thumbnail(source) : TextureOperations.ChannelView(source, mode));
            }
            catch (Exception exception)
            {
                Post(() => ImageFailed(token, row, thumbnail, mode, mip, exception.Message));
                return;
            }
            Post(() => ImageLoaded(token, row, texture, thumbnail, prefetch, mode, mip, bitmap, thumbnail ? null : source));
        }, thumbnail ? PriorityThumbnail : prefetch ? PriorityMips : PriorityPreview);
    }

    private void ImageLoaded(int token, int row, Texture texture, bool thumbnail, bool prefetch, string mode, int mip, Bitmap bitmap,
        RgbaImage? source)
    {
        if (token != generation)
        {
            bitmap.Dispose();
            return;
        }
        if (thumbnail)
        {
            if (row >= 0 && row < TexturesModel.Count)
                TexturesModel[row].SetThumbnail($"image://textureviewer/{session}/thumbnail/{row}", bitmap);
            else
                bitmap.Dispose();
            return;
        }
        // A view of another channel is kept too: it is one a channel switch can show at once.
        if (row != selectedIndex || !ReferenceEquals(SelectedRecord()?.Texture, texture))
        {
            bitmap.Dispose();
            return;
        }
        UseViews(texture);
        if (source is not null)
            sources.TryAdd(mip, source);
        // A view that finished after a switch away from both its channel and its mip is not kept, as PruneViews
        // would not keep it; its RGBA is.
        if (mode != channel && mip != mipLevel)
        {
            viewsRequested.Remove((mode, mip));
            bitmap.Dispose();
            return;
        }
        if (!views.TryAdd((mode, mip), bitmap))
        {
            bitmap.Dispose();
            bitmap = views[(mode, mip)];
        }
        if (mode == channel && mip == mipLevel && !ReferenceEquals(previewImage, bitmap))
            ShowMip(row, mode, mip, bitmap);
        if (!prefetch)
            Prefetch(row, texture, mode, mip);
    }

    /// <summary>
    /// Once a view is up, makes the texture's other mips in its channel and its mip in the other channels in the
    /// background, so moving to any of them is instant.
    /// </summary>
    private void Prefetch(int row, Texture texture, string mode, int mip)
    {
        for (int level = 0; level < texture.MipCount; level++)
            Request(mode, level);
        foreach (string other in TextureOperations.Channels)
            Request(other, mip);

        void Request(string viewChannel, int viewMip)
        {
            if (!views.ContainsKey((viewChannel, viewMip)) && viewsRequested.Add((viewChannel, viewMip)))
                StartImageTask(row, texture, viewChannel, viewMip, thumbnail: false, prefetch: true);
        }
    }

    /// <summary>
    /// Lets go of the views that are neither in the channel shown nor of the mip shown; the RGBA stays, so any of
    /// them is quick to make again.
    /// </summary>
    private void PruneViews()
    {
        foreach ((string Channel, int Mip) key in views.Keys.Where(key => key.Channel != channel && key.Mip != mipLevel).ToList())
        {
            if (views.Remove(key, out Bitmap? bitmap) && !ReferenceEquals(bitmap, previewImage))
                TextureBitmaps.DisposeLater(bitmap);
            viewsRequested.Remove(key);
        }
    }

    /// <summary>Shows a decoded mip of the selected texture as the preview.</summary>
    private void ShowMip(int row, string mode, int mip, Bitmap bitmap)
    {
        previewRevision++;
        SetPreview($"image://textureviewer/{session}/preview/{row}/{mode}/{mip}/{previewRevision}", bitmap);
        previewTexture = RecordAt(row)?.Texture;
        previewLoading = false;
        SetStatus($"Previewing {SelectedName} · mip {mip}");
        EmitStateChanged();
        EmitSelectionChanged();
    }

    /// <summary>The selected texture's view in <paramref name="mode"/> at <paramref name="mip"/>, if it is already made.</summary>
    private Bitmap? ReadyView(string mode, int mip) =>
        SelectedRecord() is { } record && ReferenceEquals(record.Texture, viewsTexture)
            && views.TryGetValue((mode, mip), out Bitmap? bitmap) ? bitmap : null;

    /// <summary>Keeps the decoded mips and views of <paramref name="texture"/>, releasing those of any other.</summary>
    private void UseViews(Texture? texture)
    {
        if (ReferenceEquals(texture, viewsTexture))
            return;
        foreach (Bitmap bitmap in views.Values)
        {
            // The one on screen is released by SetPreview when it is replaced.
            if (!ReferenceEquals(bitmap, previewImage))
                TextureBitmaps.DisposeLater(bitmap);
        }
        views.Clear();
        viewsRequested.Clear();
        sources.Clear();
        viewsTexture = texture;
    }

    private void ImageFailed(int token, int row, bool thumbnail, string mode, int mip, string message)
    {
        if (token != generation || thumbnail)
            return;
        if (row != selectedIndex || mode != channel || mip != mipLevel)
            return;
        previewLoading = false;
        SetStatus($"Could not decode {SelectedName}: {message}");
        EmitStateChanged();
    }

    /// <summary>Decodes a texture for previews, thumbnails and copying (Python <c>decode_texture_image</c>); replaceable
    /// by tests.</summary>
    internal Func<Texture, string, int, RgbaImage> DecodeImage { get; set; } = TextureOperations.DecodeImage;

    private void SetPreview(string url, Bitmap? bitmap)
    {
        Bitmap? previous = previewImage;
        previewUrl = url;
        previewImage = bitmap;
        if (previous is not null && !ReferenceEquals(previous, bitmap) && !views.ContainsValue(previous))
            TextureBitmaps.DisposeLater(previous);
    }

    private void ClearPreview()
    {
        SetPreview("", null);
        previewTexture = null;
        UseViews(null);
    }

    private void SetTextures(IEnumerable<Texture> textures)
    {
        foreach (TextureItem item in TexturesModel)
            TextureBitmaps.DisposeLater(item.Thumbnail);
        TexturesModel.Clear();
        foreach (Texture texture in textures)
            TexturesModel.Add(new TextureItem(texture));
        ApplyModelSelection();
    }

    /// <summary>
    /// Puts <paramref name="texture"/> in a row. Its thumbnail is made again by the caller, unless
    /// <paramref name="samePixels"/> says only something else changed (a rename), when the row keeps the one it has.
    /// </summary>
    private void ReplaceModelTexture(int row, Texture texture, bool samePixels = false)
    {
        if (row < 0 || row >= TexturesModel.Count)
            return;
        TextureItem previous = TexturesModel[row];
        var item = new TextureItem(texture) { TextureSelected = previous.TextureSelected };
        if (samePixels)
            item.SetThumbnail(previous.ThumbnailUrl, previous.Thumbnail);
        TexturesModel[row] = item;
        if (!samePixels)
            TextureBitmaps.DisposeLater(previous.Thumbnail);
    }

    private void InsertModelTexture(int row, Texture texture)
    {
        int resolved = Math.Clamp(row, 0, TexturesModel.Count);
        TexturesModel.Insert(resolved, new TextureItem(texture));
        // Rows moved, so every published thumbnail is stale until it is decoded again.
        foreach (TextureItem item in TexturesModel)
            item.SetThumbnail("", null);
        ApplyModelSelection();
    }

    private void ApplyModelSelection()
    {
        for (int row = 0; row < TexturesModel.Count; row++)
            TexturesModel[row].TextureSelected = selectedRows.Contains(row);
    }

    private TextureItem? SelectedRecord() => RecordAt(selectedIndex);

    private TextureItem? RecordAt(int row) => row >= 0 && row < TexturesModel.Count ? TexturesModel[row] : null;

    private IEnumerable<(int Row, TextureItem Item)> SelectionRecords() =>
        selectedRows.Where(row => row < TexturesModel.Count).Select(row => (row, TexturesModel[row]));

    private void SetStatus(string value)
    {
        if (value == status)
            return;
        status = value;
        OnPropertyChanged(nameof(Status));
    }

    private void EmitStateChanged()
    {
        foreach (string name in StateProperties)
            OnPropertyChanged(name);
        EmitDerived();
    }

    private void EmitSelectionChanged()
    {
        foreach (string name in SelectionProperties)
            OnPropertyChanged(name);
        EmitDerived();
    }

    private void EmitDerived()
    {
        foreach (string name in DerivedProperties)
            OnPropertyChanged(name);
    }

    private void ClipboardChanged(object? sender, EventArgs e) => EmitStateChanged();

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private sealed record PendingImageImport(int Generation, int Revision, IReadOnlyList<string> Paths, int ConflictCount);

    private sealed record PasteRequest(int Generation, int Revision, int Row, RgbaImage Image, Texture? Source);
}
