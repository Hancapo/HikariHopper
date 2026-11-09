using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Ports of test_texture_viewer.py, test_texture_multiselection.py, test_texture_duplicates.py and
/// test_power_of_two_resize.py (view-model side).</summary>
public sealed class TextureViewerTests
{
    private static TextureViewerViewModel FiveTextures()
    {
        (int, int)[] sizes = [(30, 50), (16, 32), (100, 70), (8, 16), (16, 64)];
        return Loaded(new TextureSet(sizes.Select((size, index) =>
            Bgra($"texture_{index}", size.Item1, size.Item2, [0, 0, 255, 128])), GameTarget.Legacy));
    }

    [AvaloniaFact]
    public Task TextureItemExposesViewerMetadata() => Headless.Run(() =>
    {
        var item = new TextureItem(Red("paint"));
        Assert.Equal("paint", item.Name);
        Assert.Equal("2 × 2", item.Dimensions);
        Assert.Equal("ARGB 32-bit", item.FormatName);
        Assert.Equal("DEFAULT", item.Usage);
        Assert.Equal(1, item.MipCount);
        Assert.Equal(16, item.DataSize);
        Assert.Equal("16 B", item.DataSizeLabel);
        Assert.Equal("", item.ThumbnailUrl);
        Assert.Null(item.Thumbnail);
    });

    [AvaloniaFact]
    public Task LoadingPublishesPreviewAndThumbnailBitmaps() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Opened("textures.ytd", Bytes(Dictionary("paint", "detail")));
        Assert.True(viewer.HasDocument);
        Assert.Equal("Previewing detail · mip 0", viewer.Status); // Rows follow the file's hash order.
        Assert.StartsWith("image://textureviewer/", viewer.PreviewUrl);
        Bitmap preview = Assert.IsAssignableFrom<Bitmap>(viewer.PreviewImage);
        Assert.Equal(new Avalonia.PixelSize(2, 2), preview.PixelSize);
        Assert.All(viewer.TexturesModel, item =>
        {
            Assert.EndsWith(item == viewer.TexturesModel[0] ? "/thumbnail/0" : "/thumbnail/1", item.ThumbnailUrl);
            Assert.Equal(new Avalonia.PixelSize(96, 96), item.Thumbnail!.PixelSize);
        });
        Assert.Equal([255, 0, 0, 255], FirstPixel(preview)); // Red, as RGBA.
        viewer.SetChannel("G");
        Settle(viewer);
        Assert.Equal("g", viewer.Channel);
        Assert.Contains("/preview/0/g/0/", viewer.PreviewUrl);
        viewer.SetChannel("rgb");
        Assert.Equal("g", viewer.Channel);
    });

    [AvaloniaFact]
    public Task SwitchingChannelsShowsTheViewAtOnceWithoutDecodingAgain() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Opened("textures.ytd", Bytes(Dictionary("paint", "detail")));
        Settle(viewer); // The other channels of the mip on screen are made in the background.
        int decodes = 0;
        viewer.DecodeImage = (texture, mode, mip) =>
        {
            Interlocked.Increment(ref decodes);
            return TextureOperations.DecodeImage(texture, mode, mip);
        };
        foreach (string channel in new[] { "r", "g", "b", "a", "rgba" })
        {
            viewer.SetChannel(channel);
            Assert.False(viewer.PreviewLoading);
            Assert.Contains($"/preview/0/{channel}/0/", viewer.PreviewUrl);
        }
        viewer.SetChannel("g");
        Assert.Equal([0, 0, 0, 255], FirstPixel(Assert.IsAssignableFrom<Bitmap>(viewer.PreviewImage))); // Red has no green.
        viewer.SetChannel("r");
        Assert.Equal([255, 255, 255, 255], FirstPixel(Assert.IsAssignableFrom<Bitmap>(viewer.PreviewImage)));
        Settle(viewer);
        Assert.Equal(0, decodes); // Every view came from the RGBA decoded once.
    });

    /// <summary>A BGRA texture of one colour with its full mip chain.</summary>
    private static Texture Mipped(string name, int width, int height)
    {
        int mips = 1, pixels = width * height;
        for (int w = width, h = height; w > 1 || h > 1; mips++)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            pixels += w * h;
        }
        return new Texture(name, width, height, TextureFormat.B8G8R8A8Unorm, mips, Repeat([0, 0, 255, 255], pixels));
    }

    [Fact]
    public void A_thumbnail_is_made_from_the_smallest_mip_that_covers_it()
    {
        // 96 across: 2048 halves to 128 four times; 256×128 (a 96×48 thumbnail) once; one without mips is its only one.
        Assert.Equal(4, TextureBitmaps.ThumbnailMip(2048, 2048, 12));
        Assert.Equal(1, TextureBitmaps.ThumbnailMip(256, 128, 9));
        Assert.Equal(0, TextureBitmaps.ThumbnailMip(100, 70, 1));
        Assert.Equal(0, TextureBitmaps.ThumbnailMip(4, 4, 3));
        Assert.Equal((96, 48), TextureBitmaps.ThumbnailDimensions(256, 128));
        Assert.Equal((24, 96), TextureBitmaps.ThumbnailDimensions(16, 64));
    }

    [AvaloniaFact]
    public Task Thumbnails_decode_a_small_mip_rather_than_the_whole_texture() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Viewer();
        var decoded = new List<(string Name, int Mip)>();
        viewer.DecodeImage = (texture, mode, mip) =>
        {
            lock (decoded)
                decoded.Add((texture.Name, mip));
            return TextureOperations.DecodeImage(texture, mode, mip);
        };
        viewer.OpenDictionary("textures.ytd", "", Bytes(new TextureSet([Mipped("big", 512, 512), Mipped("wide", 256, 128)], GameTarget.Legacy)));
        Settle(viewer);
        Assert.Contains(("big", 2), decoded);
        Assert.Contains(("wide", 1), decoded);
        Assert.DoesNotContain(("wide", 0), decoded); // The first is the one previewed, at its full size.
        Assert.Equal(new Avalonia.PixelSize(96, 96), viewer.TexturesModel[0].Thumbnail!.PixelSize);
        Assert.Equal(new Avalonia.PixelSize(96, 48), viewer.TexturesModel[1].Thumbnail!.PixelSize);
    });

    [AvaloniaFact]
    public Task A_preview_bitmap_holds_the_textures_RGBA_as_it_is() => Headless.Run(() =>
    {
        // Copied, not worked out per pixel: unpremultiplied RGBA, the alpha kept apart.
        using var bitmap = TextureBitmaps.Create(new RgbaImage(2, 1, [10, 20, 30, 64, 200, 100, 50, 255]));
        Assert.Equal(Avalonia.Platform.PixelFormat.Rgba8888, bitmap.Format);
        Assert.Equal(Avalonia.Platform.AlphaFormat.Unpremul, bitmap.AlphaFormat);
        Assert.Equal([10, 20, 30, 64], FirstPixel(bitmap));
    });

    [AvaloniaFact]
    public Task RenamingKeepsTheTexturesThumbnail() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Opened("textures.ytd", Bytes(Dictionary("paint", "detail")));
        TextureItem before = viewer.TexturesModel[viewer.SelectedIndex];
        Bitmap thumbnail = Assert.IsAssignableFrom<Bitmap>(before.Thumbnail);
        string url = before.ThumbnailUrl;
        Assert.NotEqual("", url);

        // Only the name changes, so the row keeps its picture, at once and after things settle.
        Assert.True(viewer.RenameSelected("renamed"));
        TextureItem after = viewer.TexturesModel[viewer.SelectedIndex];
        Assert.Equal("renamed", after.Name);
        Assert.Same(thumbnail, after.Thumbnail);
        Assert.Equal(url, after.ThumbnailUrl);
        Settle(viewer);
        Assert.Same(thumbnail, viewer.TexturesModel[viewer.SelectedIndex].Thumbnail);
    });

    [AvaloniaFact]
    public Task UndoesRenameAndRemoveOperations() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(new TextureSet([Red("paint"), Red("detail")], GameTarget.Legacy));
        Assert.True(viewer.RenameSelected("body"));
        Assert.Equal("body", viewer.SelectedName);
        Assert.Equal("Renamed paint to body", viewer.Status);
        Assert.True(viewer.Modified);
        Assert.True(viewer.CanUndo);
        Assert.Equal("Rename texture", viewer.UndoLabel);
        Assert.False(viewer.RenameSelected("DETAIL"));
        Assert.Equal("A texture named DETAIL already exists", viewer.Status);
        Assert.False(viewer.RenameSelected("  "));
        Assert.Equal("Texture name cannot be empty", viewer.Status);
        Assert.True(viewer.RemoveSelected());
        Assert.Equal(1, viewer.TextureCount);
        Assert.Equal("detail", viewer.SelectedName);
        Assert.Equal("Remove texture", viewer.UndoLabel);

        List<string> statuses = StatusHistory(viewer);
        Assert.True(viewer.Undo());
        Assert.Contains("Undid remove texture", statuses);
        Assert.Equal("Decoding body · mip 0…", viewer.Status); // The restored selection's preview follows.
        Assert.Equal(2, viewer.TextureCount);
        Assert.Equal("body", viewer.SelectedName);
        Assert.True(viewer.Modified);

        Assert.True(viewer.Undo());
        Assert.Equal("paint", viewer.SelectedName);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
        viewer.Shutdown();
    });

    [AvaloniaFact]
    public Task UndoAfterSaveCreatesPendingChangesAgain() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(new TextureSet([Red("paint")], GameTarget.Legacy));
        Assert.True(viewer.RenameSelected("body"));
        viewer.SaveCompleted(viewer.Generation, "textures.ytd", null);
        Assert.False(viewer.Modified);
        Assert.Equal("Saved textures.ytd", viewer.Status);
        Assert.True(viewer.Undo());
        Assert.Equal("paint", viewer.SelectedName);
        Assert.True(viewer.Modified);
    });

    [AvaloniaFact]
    public Task SavingIsDistinctFromPendingChanges() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(new TextureSet([Red("paint")], GameTarget.Legacy));
        Assert.True(viewer.RenameSelected("body"));
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = new List<bool>();
        viewer.SaveFinished += (_, success) => finished.Add(success);
        Assert.True(viewer.StartSave("textures.ytd", () => { started.Set(); release.Wait(5000); }));
        Assert.True(started.Wait(5000));
        Assert.True(viewer.Saving);
        Assert.True(viewer.Modified);
        Assert.Equal("Saving textures.ytd…", viewer.Status);
        release.Set();
        Settle(viewer);
        Assert.False(viewer.Saving);
        Assert.False(viewer.Modified);
        Assert.Equal([true], finished);
    });

    [AvaloniaFact]
    public Task ControlShiftAndContextSelection() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        viewer.SelectTexture(1);
        viewer.SelectTexture(3, KeyModifiers.Control);
        Assert.Equal([1, 3], Selected(viewer));
        viewer.SelectContextTexture(1);
        Assert.Equal([1, 3], Selected(viewer));
        Assert.Equal(1, viewer.SelectedIndex);
        viewer.SelectTexture(4, KeyModifiers.Shift);
        Assert.Equal([3, 4], Selected(viewer));
        viewer.SelectTexture(1, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal([1, 2, 3, 4], Selected(viewer));
        viewer.SelectContextTexture(0);
        Assert.Equal([0], Selected(viewer));
        viewer.SelectTexture(0, KeyModifiers.Control);
        Assert.Equal(0, viewer.SelectedCount);
        Assert.Equal(-1, viewer.SelectedIndex);
        Assert.Equal("", viewer.PreviewUrl);
        Assert.Null(viewer.PreviewImage);
        Assert.False(viewer.RemoveSelected());
        viewer.SelectAllTextures();
        Assert.Equal([0, 1, 2, 3, 4], Selected(viewer));
        Assert.True(viewer.CanRemoveSelection);
        Assert.True(viewer.RemoveSelected());
        Assert.Equal(0, viewer.TextureCount);
        Assert.True(viewer.HasDocument);
    });

    [AvaloniaTheory]
    [InlineData("resize")]
    [InlineData("power")]
    [InlineData("mips")]
    [InlineData("format")]
    public Task BatchOperationsAndSingleUndo(string operation) => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        Texture[] originals = [.. viewer.Dictionary!.Textures];
        viewer.SelectTexture(0);
        viewer.SelectTexture(2, KeyModifiers.Control);
        List<string> statuses = StatusHistory(viewer);
        Assert.True(operation switch
        {
            "resize" => viewer.ResizeSelected(32, 64, "mitchell", 1, true),
            "power" => viewer.ResizeSelectionToPowerOfTwo("nearest", "mitchell", 1, true),
            "mips" => viewer.RecalculateSelectedMipmaps("mitchell", 1),
            _ => viewer.ChangeSelectedFormat("BC1", 0.7, "mitchell", 1),
        });
        Settle(viewer);
        Assert.True(viewer.Modified, viewer.Status);
        Assert.Contains("Finished " + operation switch
        {
            "resize" or "power" => "resizing",
            "mips" => "recalculating mipmaps for",
            _ => "changing format for",
        } + " 2 textures", statuses);
        Assert.Single(viewer.UndoStack);
        Assert.Equal([0, 2], Selected(viewer));
        foreach (int row in new[] { 1, 3, 4 })
            Assert.Same(originals[row], viewer.Dictionary!.Textures[row]);
        foreach (int row in new[] { 0, 2 })
        {
            Texture result = viewer.Dictionary!.Textures[row];
            Assert.Equal(originals[row].Name, result.Name);
            switch (operation)
            {
                case "resize":
                    Assert.Equal((32, 64), (result.Width, result.Height));
                    break;
                case "power":
                    Assert.Equal(row == 0 ? (32, 64) : (128, 64), (result.Width, result.Height));
                    break;
                case "mips":
                    Assert.Equal(TextureDimensions.MipCountForDimensions(result.Width, result.Height, 1), result.MipCount);
                    break;
                default:
                    Assert.Equal(TextureFormat.BC1Unorm, result.Format);
                    break;
            }
        }
        Assert.Equal(5, Ytd.Read(YtdSaving.Serialize(viewer.Dictionary!)).Textures.Count);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal([0, 2], Selected(viewer));
        Assert.False(viewer.Modified);
        Assert.Equal(originals, viewer.Dictionary!.Textures);
    });

    [AvaloniaFact]
    public Task PowerOfTwoSkipsValidTextures() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        viewer.SelectTexture(0);
        viewer.SelectTexture(1, KeyModifiers.Control);
        Texture unchanged = viewer.Dictionary!.Textures[1];
        Assert.True(viewer.ResizeSelectionToPowerOfTwo("down", "mitchell", 1, true));
        Settle(viewer);
        Assert.Same(unchanged, viewer.Dictionary!.Textures[1]);
        Assert.Equal((16, 32), (viewer.Dictionary.Textures[0].Width, viewer.Dictionary.Textures[0].Height));
    });

    [AvaloniaFact]
    public Task FailedBatchDoesNotCommitPartialResults() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        Texture[] originals = [.. viewer.Dictionary!.Textures];
        viewer.SelectTexture(0);
        viewer.SelectTexture(2, KeyModifiers.Control);
        Settle(viewer);
        Assert.True(viewer.StartTransform("Resizing", texture => texture.Name == "texture_2"
            ? throw new ArgumentException("simulated conversion failure")
            : TextureOperations.Resize(texture, 16, 16, ImageFilter.Mitchell, 1, false)));
        Settle(viewer);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
        Assert.Equal("Resizing failed: texture_2: simulated conversion failure", viewer.Status);
        Assert.Equal(originals, viewer.Dictionary!.Textures);
    });

    [AvaloniaFact]
    public Task SelectionIsStableDuringBatch() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        viewer.SelectTexture(1);
        viewer.SelectTexture(3, KeyModifiers.Control);
        Assert.True(viewer.StartTransform("Resizing", texture =>
        {
            started.Set();
            Assert.True(release.Wait(5000));
            return TextureOperations.Resize(texture, 4, 4, ImageFilter.Mitchell, 1, false);
        }));
        Assert.True(started.Wait(5000));
        try
        {
            Assert.Equal("Resizing 2 textures…", viewer.Status);
            viewer.SelectTexture(0);
            viewer.SelectAllTextures();
            Assert.Equal([1, 3], Selected(viewer));
            Assert.False(viewer.RemoveSelected());
            Assert.False(viewer.CanEditSelection);
        }
        finally
        {
            release.Set();
        }
        Settle(viewer);
        Assert.Equal([1, 3], Selected(viewer));
    });

    [AvaloniaFact]
    public Task RemoveSelectionAndRestoreInOneUndo() => Headless.Run(async () =>
    {
        TextureViewerViewModel viewer = FiveTextures();
        viewer.SelectTexture(1);
        viewer.SelectTexture(3, KeyModifiers.Control);
        Assert.False(viewer.RenameSelected("same_name"));
        Assert.False(await viewer.ReplaceSelectedFromImage());
        Assert.False(viewer.CanEditSingle);
        Assert.True(viewer.RemoveSelected());
        Assert.Equal("Removed 2 textures", viewer.Status);
        Settle(viewer);
        Assert.Equal(["texture_0", "texture_2", "texture_4"], Names(viewer));
        Assert.Single(viewer.UndoStack);
        Assert.Equal("Remove textures", viewer.UndoLabel);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(Enumerable.Range(0, 5).Select(index => $"texture_{index}"), Names(viewer));
        Assert.Equal([1, 3], Selected(viewer));
        Assert.False(viewer.Modified);
    });

    [AvaloniaFact]
    public Task ExtractsOnlySelectedTextures() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogs { Folder = folder.Path };
        TextureViewerViewModel viewer = FiveTextures();
        viewer.Dialogs = dialogs;
        viewer.SelectTexture(1);
        viewer.SelectTexture(3, KeyModifiers.Control);
        await viewer.ExtractSelected();
        Assert.Equal(["texture_1.dds", "texture_3.dds"],
            Directory.GetFiles(folder.Path, "*.dds").Select(Path.GetFileName).Order());
        Assert.Equal("Extracted 2 textures", viewer.Status);
        Assert.Equal(["Extract selected textures"], dialogs.Titles);
        Assert.False(viewer.Modified);

        viewer.SelectTexture(0);
        dialogs.SaveFile = folder.File("single.png");
        await viewer.ExtractSelected();
        Assert.True(File.Exists(folder.File("single.dds")));
        Assert.Equal("Extracted texture_0", viewer.Status);
        Assert.Equal(30, Texture.FromSurface(DdsCodec.Read(folder.File("single.dds")), "x").Width);

        dialogs.Folder = folder.File("all");
        await viewer.ExtractAll();
        Assert.Equal("Extracted 5 textures", viewer.Status);
    });

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public Task RemoveDuplicatesKeepsFirstAndUndoRestoresOriginal(GameTarget target) => Headless.Run(() =>
    {
        TextureSet dictionary = Dictionary(target, "Paint", "detail", "paint", "Paint", "unique");
        TextureViewerViewModel viewer = Loaded(dictionary);
        viewer.SelectTexture(2);
        viewer.SelectTexture(4, KeyModifiers.Control);
        Assert.Equal(2, viewer.DuplicateTextureCount);
        Assert.True(viewer.CanRemoveDuplicates);
        Assert.True(viewer.RemoveDuplicatesByName());
        Assert.Equal("Removed 2 duplicate textures", viewer.Status);
        Settle(viewer);
        Assert.Equal(["Paint", "detail", "unique"], Names(viewer));
        Assert.Equal([dictionary.Textures[0], dictionary.Textures[1], dictionary.Textures[4]], viewer.Dictionary!.Textures);
        Assert.Equal([0, 2], viewer.SelectedRows);
        Assert.Equal("unique", viewer.SelectedName);
        Assert.Equal(0, viewer.DuplicateTextureCount);
        Assert.Single(viewer.UndoStack);
        Assert.True(viewer.Modified);
        Ytd saved = Ytd.Read(YtdSaving.Serialize(viewer.Dictionary));
        Assert.Equal(target, saved.Target);
        Assert.Equal(["Paint", "detail", "unique"], saved.Textures.Select(texture => texture.Name).Order(StringComparer.Ordinal));
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(dictionary.Textures, viewer.Dictionary!.Textures);
        Assert.Equal([2, 4], viewer.SelectedRows);
        Assert.Equal(2, viewer.DuplicateTextureCount);
        Assert.False(viewer.Modified);
        Assert.ThrowsAny<Exception>(() => YtdSaving.Serialize(viewer.Dictionary));
        Assert.True(viewer.RemoveDuplicatesByName());
    });

    [AvaloniaFact]
    public Task NoDuplicatesIsNoop() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Viewer();
        Assert.Equal(0, viewer.DuplicateTextureCount);
        Assert.False(viewer.RemoveDuplicatesByName());
        viewer.DictionaryLoaded(viewer.Generation, Dictionary("one", "two"));
        Settle(viewer);
        Assert.False(viewer.RemoveDuplicatesByName());
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
    });

    [AvaloniaFact]
    public Task DuplicateCleanupIgnoresSelectionButRespectsBusyState() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(Dictionary("same", "SAME", "other"));
        viewer.SelectTexture(0, KeyModifiers.Control);
        Assert.Equal(0, viewer.SelectedCount);
        viewer.SetExternalWriteBusy(true);
        Assert.False(viewer.CanRemoveDuplicates);
        Assert.False(viewer.RemoveDuplicatesByName());
        Assert.Equal(3, viewer.TextureCount);
        viewer.SetExternalWriteBusy(false);
        Assert.True(viewer.RemoveDuplicatesByName());
        Assert.Equal("Removed 1 duplicate texture", viewer.Status);
        Settle(viewer);
        Assert.Equal(2, viewer.TextureCount);
        Assert.Equal(0, viewer.SelectedCount);
        Assert.Equal(-1, viewer.SelectedIndex);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(0, viewer.SelectedCount);
    });

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public Task ResizeToPowerOfTwoRoundTripAndUndo(GameTarget target) => Headless.Run(() =>
    {
        Texture source = Bgra("paint", 30, 50, [0, 0, 255, 128]);
        TextureViewerViewModel viewer = Opened("paint.ytd", Bytes(new TextureSet([source], target)));
        Texture original = viewer.Dictionary!.Textures[0];
        Assert.True(viewer.SelectedNeedsPowerOfTwo);
        Assert.True(viewer.CanResizeToPowerOfTwo);
        Assert.Equal(new TextureSize(32, 64), viewer.PowerOfTwoSize("nearest"));
        Assert.Null(viewer.PowerOfTwoSize("sideways"));
        Assert.True(viewer.ResizeSelected(32, 64, "mitchell", 1, true));
        Settle(viewer);
        Assert.Equal((32, 64), (viewer.SelectedWidth, viewer.SelectedHeight));
        Assert.False(viewer.SelectedNeedsPowerOfTwo);
        Assert.Equal(7, viewer.MipCount);
        Assert.True(viewer.Modified);
        Assert.True(viewer.CanUndo);
        Texture saved = Ytd.Read(YtdSaving.Serialize(viewer.Dictionary!)).Textures[0];
        Assert.Equal((32, 64), (saved.Width, saved.Height));
        Assert.Equal((original.Format, original.Usage, original.Name), (saved.Format, saved.Usage, saved.Name));
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal((30, 50), (viewer.SelectedWidth, viewer.SelectedHeight));
        Assert.Equal(original.Data.ToArray(), viewer.Dictionary!.Textures[0].Data.ToArray());
        Assert.False(viewer.Modified);
    });

    [AvaloniaFact]
    public Task PowerOfTwoValidationRespectsTheEditionLimit() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Loaded(new TextureSet([Bgra("wide", 0x7FFF, 1, [0, 0, 0, 255])], GameTarget.Legacy));
        Assert.Equal(0x7FFF, viewer.MaximumDimension);
        Assert.False(viewer.ValidPowerOfTwoSelection("up"));
        Assert.True(viewer.ValidPowerOfTwoSelection("down"));
        Assert.False(viewer.ResizeSelectionToPowerOfTwo("up", "mitchell", 1, true));
        Assert.Equal("A rounded dimension exceeds the limit; choose Round down", viewer.Status);
        Assert.False(viewer.ResizeSelected(0x8000, 4, "mitchell", 1, true));
        Assert.Equal("Texture dimensions cannot exceed 32767", viewer.Status);
        Assert.False(viewer.ResizeSelected(0, 4, "mitchell", 1, true));
        Assert.Equal("Texture dimensions must be positive", viewer.Status);
        Assert.False(viewer.ChangeSelectedFormat("BC6H", 1, "mitchell", 1));
        Assert.Equal("Unsupported texture format: BC6H", viewer.Status);
        Assert.Equal(0xFFFF, Loaded(Dictionary(GameTarget.Enhanced, "a")).MaximumDimension);
    });

    [AvaloniaFact]
    public Task EstimatesAndFormatOptionsMatchThePythonSlots() => Headless.Run(() =>
    {
        TextureViewerViewModel viewer = Viewer();
        Assert.Equal(7, viewer.EstimatedMipCount(256, 512, 4));
        Assert.Equal("4 × 8", viewer.EstimatedSmallestMipDimensions(256, 512, 4));
        Assert.Equal("—", viewer.EstimatedSmallestMipDimensions(0, 512, 4));
        Assert.Equal(["DXT1 (BC1)", "DXT3 (BC2)", "DXT5 (BC3)", "ATI1 (BC4)", "ATI2 (BC5)", "BPTC RGBA (BC7)", "ARGB 32-bit (A8R8G8B8)"],
            viewer.FormatOptions.Select(option => option.Label));
        Assert.Equal(TextureFormatNames.Editable, viewer.FormatOptions.Select(option => option.Value));
        Assert.Equal("No texture dictionary loaded", viewer.Status);
    });

    [AvaloniaFact]
    public Task ReplaceFromImageKeepsSizeFormatAndMips() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var source = TextureProcessing.Encode(Solid(16, 8, 0, 0, 255, 255),
            new TextureEncodingOptions { Format = TextureFormat.BC1Unorm, MinimumMipDimension = 4 }, "paint");
        source.Usage = TextureUsage.Diffuse;
        var dialogs = new FakeDialogs();
        dialogs.OpenFiles.Add(ImageFile(folder.File("replacement.png"), 5, 3, alpha: 255));
        TextureViewerViewModel viewer = Loaded(new TextureSet([source, Red("other")], GameTarget.Legacy), dialogs: dialogs);
        Assert.True(await viewer.ReplaceSelectedFromImage());
        Settle(viewer);
        Texture result = viewer.Dictionary!.Textures[0];
        Assert.Equal(("paint", 16, 8, TextureFormat.BC1Unorm, source.MipCount, TextureUsage.Diffuse),
            (result.Name, result.Width, result.Height, result.Format, result.MipCount, result.Usage));
        byte[] pixel = TextureProcessing.Decode(result).Pixels[..4].ToArray();
        Assert.True(pixel[0] > 190 && pixel[1] < 100 && pixel[2] < 100 && pixel[3] == 255, string.Join(",", pixel));
        Assert.Equal("Replace texture", viewer.UndoLabel);
        Assert.Equal(["Replace texture from image"], dialogs.Titles);
    });

    internal static byte[] FirstPixel(Bitmap bitmap)
    {
        nint buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, 1, 1), buffer, 4, 4);
            var pixel = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(buffer, pixel, 0, 4);
            return pixel;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }
}
