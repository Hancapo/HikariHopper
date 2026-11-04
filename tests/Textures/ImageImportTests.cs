using CommonFury.Textures;
using FiveFury;
using FiveFury.Rpf;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Port of test_texture_image_drop.py (view-model side).</summary>
public sealed class ImageImportTests
{
    [AvaloniaFact]
    public Task ImportImagesAsksForFilesAndImportsThemAsOneBatch() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogs();
        dialogs.OpenFiles.Add(ImageFile(folder.File("paint.png"), 7, 5));
        dialogs.OpenFiles.Add(ImageFile(folder.File("detail.bmp")));
        TextureViewerViewModel viewer = Loaded(Dictionary("existing"), dialogs: dialogs);
        Assert.True(await viewer.ImportImagesFromFiles());
        Settle(viewer);
        Assert.Equal(["detail", "existing", "paint"], Names(viewer).Order());
        Assert.Equal(["Import images"], dialogs.Titles);
        Assert.Equal("Import textures", viewer.UndoLabel);

        // Cancelling the picker imports nothing.
        dialogs.OpenFiles.Clear();
        Assert.False(await viewer.ImportImagesFromFiles());
        Assert.Equal(3, viewer.TextureCount);
    });

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public Task DropIntoEmptyYtdImportsSavesAndUndoesOneBatch(GameTarget target) => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string path = folder.File("test.ytd");
        File.WriteAllBytes(path, EmptyYtd(target));
        TextureViewerViewModel viewer = Viewer();
        viewer.OpenDictionary("test.ytd", path, File.ReadAllBytes(path));
        Settle(viewer);
        Assert.Equal("Texture dictionary is empty", viewer.Status);
        string[] paths = [ImageFile(folder.File("paint.png"), 7, 5), ImageFile(folder.File("detail.bmp"))];
        Assert.True(viewer.CanAcceptImageDrop(paths));
        Assert.True(viewer.ImportDroppedImages(paths));
        Assert.True(viewer.OperationBusy);
        Assert.Equal("Importing 2 images…", viewer.Status);
        Settle(viewer);
        Assert.Equal(2, viewer.TextureCount);
        Assert.Equal(2, viewer.SelectedCount);
        Assert.Equal(target, viewer.Dictionary!.Target);
        Assert.Equal(["paint", "detail"], Names(viewer));
        Assert.Equal((8, 4), (viewer.Dictionary.Textures[0].Width, viewer.Dictionary.Textures[0].Height));
        Assert.Single(viewer.UndoStack);
        Assert.Equal("Import textures", viewer.UndoLabel);
        Assert.Empty(Ytd.Read(File.ReadAllBytes(path)).Textures); // Not autosaved.
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.Equal(["detail", "paint"], Ytd.Read(File.ReadAllBytes(path)).Textures.Select(texture => texture.Name).Order());
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(0, viewer.TextureCount);
        Assert.Equal(0, viewer.SelectedCount);
        Assert.Equal(-1, viewer.SelectedIndex);
        Assert.False(viewer.PreviewLoading);
    });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NameCollisionsRequireOneGlobalConfirmation(bool accept) => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = Opened("test.ytd", Bytes(Dictionary("paint", "detail", "keep")));
        Texture[] before = [.. viewer.Dictionary!.Textures];
        int requests = 0;
        viewer.ImageImportConfirmationRequested += (_, _) => requests++;
        string[] paths = [ImageFile(folder.File("PAINT.png")), ImageFile(folder.File("detail.png")), ImageFile(folder.File("new.png"))];
        Assert.True(viewer.ImportDroppedImages(paths));
        Assert.Equal(1, requests);
        Assert.True(viewer.ImportConfirmationPending);
        Assert.Equal(2, viewer.ImportConflictCount);
        Assert.Equal(3, viewer.ImportFileCount);
        Assert.False(viewer.CanImportImages);
        Assert.Equal(before, viewer.Dictionary.Textures);
        Assert.False(viewer.Modified);
        viewer.ConfirmImageImport(accept);
        Settle(viewer);
        Assert.Equal(1, requests);
        Assert.False(viewer.ImportConfirmationPending);
        if (!accept)
        {
            Assert.Equal(before, viewer.Dictionary!.Textures);
            Assert.False(viewer.Modified);
            Assert.False(viewer.CanUndo);
            return;
        }
        Dictionary<string, Texture> result = viewer.Dictionary!.Textures.ToDictionary(texture => texture.Name!);
        Assert.Equal(["detail", "keep", "new", "paint"], result.Keys.Order());
        Assert.Same(before.Single(texture => texture.Name == "keep"), result["keep"]);
        Assert.Equal(8, result["paint"].Width);
        Assert.Equal(8, result["detail"].Width);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(before, viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
    });

    [AvaloniaFact]
    public Task BadImageRollsBackWholeImport() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = Opened("test.ytd", Bytes(Dictionary("keep")));
        Texture[] before = [.. viewer.Dictionary!.Textures];
        string bad = folder.File("bad.png");
        File.WriteAllBytes(bad, "not an image"u8.ToArray());
        Assert.True(viewer.ImportDroppedImages([ImageFile(folder.File("good.png")), bad]));
        Settle(viewer);
        Assert.Equal(before, viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
        Assert.False(viewer.OperationBusy);
        Assert.StartsWith("Import failed: bad.png: ", viewer.Status);
    });

    [AvaloniaFact]
    public Task IncomingDuplicatesUseOneConfirmationAndLastFile() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = Opened("test.ytd", EmptyYtd(GameTarget.Legacy));
        int requests = 0;
        viewer.ImageImportConfirmationRequested += (_, _) => requests++;
        Assert.True(viewer.ImportDroppedImages([ImageFile(folder.File("same.png"), 8, 8), ImageFile(folder.File("same.bmp"), 16, 4)]));
        Assert.Equal(1, requests);
        Assert.Equal(1, viewer.ImportConflictCount);
        Assert.True(viewer.ConfirmImageImport(true));
        Settle(viewer);
        Assert.Equal(["same"], Names(viewer));
        Assert.Equal(16, viewer.Dictionary!.Textures[0].Width);
    });

    [AvaloniaFact]
    public Task StaleConfirmationCannotModifyAnotherDocument() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = Opened("first.ytd", Bytes(Dictionary("paint")));
        Assert.True(viewer.ImportDroppedImages([ImageFile(folder.File("paint.png"))]));
        viewer.OpenDictionary("second.ytd", "", EmptyYtd(GameTarget.Enhanced));
        Settle(viewer);
        Assert.False(viewer.ConfirmImageImport(true));
        Assert.Equal(0, viewer.TextureCount);
        Assert.False(viewer.Modified);
    });

    [AvaloniaFact]
    public Task DropRejectsUnloadedBusyRemoteFolderAndUnsupported() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string image = ImageFile(folder.File("test.png"));
        TextureViewerViewModel viewer = Viewer();
        Assert.False(viewer.ImportDroppedImages([image]));
        viewer.OpenDictionary("test.ytd", "", EmptyYtd(GameTarget.Legacy));
        Settle(viewer);
        Directory.CreateDirectory(folder.File("folder.png"));
        foreach (string[] paths in new[]
        {
            Array.Empty<string>(), ["https://example.com/test.png"], [folder.Path], [folder.File("missing.png")],
            [folder.File("unknown.txt")], ["relative.png"], [folder.File("folder.png")],
        })
            Assert.False(viewer.ImportDroppedImages(paths));
        viewer.SetExternalWriteBusy(true);
        Assert.False(viewer.ImportDroppedImages([image]));
        Assert.False(viewer.CanAcceptImageDrop([image]));
        viewer.SetExternalWriteBusy(false);
        Assert.True(viewer.CanAcceptImageDrop([image]));
        Assert.False(viewer.Modified);
        Assert.Equal(0, viewer.TextureCount);
    });

    [AvaloniaTheory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    [InlineData("tga")]
    [InlineData("webp")]
    [InlineData("dds")]
    public Task SupportedFormatsImportRealEncodedFiles(string extension) => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string path = folder.File($"input.{extension}");
        if (extension == "dds")
            File.WriteAllBytes(path, DdsCodec.Write(Bgra("source", 4, 4, [30, 60, 90, 100]).ToSurface()));
        else if (extension == "tga")
            // Synthetic uncompressed 4x4 32-bit TGA, top-left origin, eight alpha bits.
            File.WriteAllBytes(path, [.. Convert.FromHexString("000002000000000000000000040004002028"), .. Repeat([30, 60, 90, 100], 16)]);
        else
            ImageFile(path, 4, 4);
        TextureViewerViewModel viewer = Opened("test.ytd", EmptyYtd(GameTarget.Legacy));
        Assert.True(viewer.ImportDroppedImages([path]));
        Settle(viewer);
        Assert.Equal(1, viewer.TextureCount);
        Texture result = viewer.Dictionary!.Textures[0];
        Assert.Equal("input", result.Name);
        Assert.Equal((4, 4), (result.Width, result.Height));
        if (extension == "dds")
        {
            Assert.Equal(TextureFormat.BC3Unorm, result.Format);
            Assert.Equal(1, result.MipCount);
            return;
        }
        int alpha = TextureOperations.DecodeImage(result).Pixels[3];
        if (extension is "jpg" or "jpeg") // JPEG drops alpha; the BMP fixture is 32-bit with alpha.
            Assert.Equal(255, alpha);
        else
            Assert.InRange(alpha, 80, 120);
    });

    [AvaloniaFact]
    public Task DroppedImagesSaveInsideRpf() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string archivePath = folder.File("textures.rpf");
        var archive = new RpfArchive("textures.rpf");
        ArchiveWriter.AddStandalone(archive, "test.ytd", EmptyYtd(GameTarget.Enhanced));
        ArchiveWriter.AddStandalone(archive, "keep.txt", "untouched"u8.ToArray());
        archive.Save(archivePath);
        ArchiveNode source = ArchiveNode.OpenRoot(archivePath, null);
        var target = new ArchiveEntryTarget(archivePath, "test.ytd", null, source);
        byte[] data = ((RpfResourceFileEntry)source.Archive.Find("test.ytd")!).ReadStandalone();
        var writes = new List<SourceWriteResult>();
        TextureViewerViewModel viewer = Viewer();
        viewer.SourceWriteFinished += (_, result) => writes.Add(result);
        viewer.OpenDictionary("test.ytd", archivePath + "::test.ytd", data, target.Save, target.Prepare, archivePath);
        Settle(viewer);
        Assert.True(viewer.CanSaveSource);
        Assert.True(viewer.ImportDroppedImages([ImageFile(folder.File("paint.png"))]));
        Settle(viewer);
        Assert.True(viewer.Modified);
        Assert.True(viewer.SaveYtd());
        Assert.True(viewer.Saving);
        Assert.Equal("Saving test.ytd…", viewer.Status);
        Settle(viewer);
        Assert.False(viewer.Modified, viewer.Status);
        Assert.Equal([new SourceWriteResult(archivePath, true)], writes);
        ArchiveNode saved = ArchiveNode.OpenRoot(archivePath, null);
        try
        {
            Assert.Equal("untouched"u8.ToArray(), ((RpfBinaryFileEntry)saved.Archive.Find("keep.txt")!).ReadStandalone());
            Ytd result = Ytd.Read(((RpfResourceFileEntry)saved.Archive.Find("test.ytd")!).ReadStandalone());
            Assert.Equal(["paint"], result.Textures.Select(texture => texture.Name));
            Assert.Equal(GameTarget.Enhanced, result.Target);
        }
        finally
        {
            saved.Close();
        }
    });
}
