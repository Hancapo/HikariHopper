using CommonFury.Textures;
using System.Buffers.Binary;
using FiveFury;
using FiveFury.Resources;
using FiveFury.Rpf;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Ports of test_empty_ytd.py and test_ytd_page_saving.py on FiveFury.NET's native writer.</summary>
public sealed class YtdSavingTests
{
    private const ulong SystemBase = 0x50000000;

    private static ulong U64(ReadOnlySpan<byte> data, ulong address) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data[(int)(address - SystemBase)..]);

    /// <summary>Every allocation (pointer array, descriptors, names, payloads, pages info) lies within one page.</summary>
    private static Ytd AssertPageSafe(byte[] data)
    {
        ResourceFile resource = ResourceFile.Read(data);
        ResourceHeader header = resource.Header;
        ReadOnlySpan<byte> system = resource.SystemData;
        Ytd parsed = Ytd.Read(data);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(system[0x38..]);
        ulong array = U64(system, SystemBase + 0x30);
        Assert.True(header.TryResolveChunk(array, 8 * count, out _));
        Dictionary<string, Texture> byName = parsed.Textures.ToDictionary(texture => texture.Name!);
        for (int index = 0; index < count; index++)
        {
            ulong descriptor = U64(system, array + 8 * (ulong)index);
            Assert.True(header.TryResolveChunk(descriptor, header.Version == 13 ? 0x90 : 0x80, out _));
            ulong namePointer = U64(system, descriptor + 0x28);
            int nameOffset = (int)(namePointer - SystemBase);
            int nameEnd = system[nameOffset..].IndexOf((byte)0);
            string name = System.Text.Encoding.UTF8.GetString(system.Slice(nameOffset, nameEnd));
            Assert.True(header.TryResolveChunk(namePointer, nameEnd + 1, out _), name);
            ulong dataPointer = U64(system, descriptor + (header.Version == 13 ? 0x70UL : 0x38UL));
            Assert.True(header.TryResolveChunk(dataPointer, byName[name].Data.Length, out _), name);
        }
        ulong info = U64(system, SystemBase + 8);
        int systemPages = system[(int)(info - SystemBase) + 8], graphicsPages = system[(int)(info - SystemBase) + 9];
        Assert.Equal(header.SystemFlags.TotalPageCount, systemPages);
        Assert.Equal(header.GraphicsFlags.TotalPageCount, graphicsPages);
        Assert.True(header.TryResolveChunk(info, 16 + 8 * (systemPages + graphicsPages), out _));
        return parsed;
    }

    public static TheoryData<GameTarget> Targets => [GameTarget.Legacy, GameTarget.Enhanced];

    // Three independent 1 MiB allocations expose page crossings in a flat writer.
    private static TextureSet LargeDictionary(GameTarget target) =>
        new(Enumerable.Range(0, 3).Select(index => Bgra($"texture_{index}", 512, 512, [(byte)index, 0, 0, 255])), target);

    [AvaloniaTheory]
    [MemberData(nameof(Targets))]
    public void LooseSaveKeepsEveryAllocationInsideAPage(GameTarget target)
    {
        TextureSet dictionary = LargeDictionary(target);
        Ytd result = AssertPageSafe(YtdSaving.Serialize(dictionary));
        Assert.Equal(target, result.Target);
        Assert.Equal(dictionary.Textures.ToDictionary(texture => texture.Name!, texture => Convert.ToHexString(texture.Data)),
            result.Textures.ToDictionary(texture => texture.Name!, texture => Convert.ToHexString(texture.Data)));
    }

    [AvaloniaFact]
    public void ManyNamesStayWithinPages()
    {
        var dictionary = new TextureSet(Enumerable.Range(0, 300).Select(index => Red($"{index:000}_" + new string('x', 97))), GameTarget.Legacy);
        Assert.Equal(300, AssertPageSafe(YtdSaving.Serialize(dictionary)).Textures.Count);
    }

    [AvaloniaFact]
    public void PayloadBytesThatLookLikePointersAreNotRelocated()
    {
        byte[] pattern = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(pattern, 0x50000040);
        BinaryPrimitives.WriteUInt64LittleEndian(pattern.AsSpan(8), 0x60000010);
        var texture = new Texture("pointer_pixels", 4, 4, TextureFormat.B8G8R8A8Unorm, 1, Repeat(pattern, 4));
        Assert.Equal(Repeat(pattern, 4), AssertPageSafe(YtdSaving.Serialize(new TextureSet([texture], GameTarget.Legacy))).Textures[0].Data.ToArray());
    }

    [AvaloniaFact]
    public void NonemptyYtdStillUsesNormalValidation() =>
        Assert.ThrowsAny<Exception>(() => YtdSaving.Serialize(Dictionary("duplicate", "duplicate")));

    [AvaloniaTheory]
    [MemberData(nameof(Targets))]
    public void EmptyYtdHasZeroArraysAndNoGraphics(GameTarget target)
    {
        byte[] data = YtdSaving.Serialize(new TextureSet([], target));
        ResourceFile resource = ResourceFile.Read(data);
        Assert.Equal(target == GameTarget.Legacy ? 13u : 5u, resource.Header.Version);
        Assert.True(resource.GraphicsData.IsEmpty || !resource.GraphicsData.ContainsAnyExcept((byte)0));
        ReadOnlySpan<byte> system = resource.SystemData;
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(system[0x20..]));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(system[0x28..]));
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(system[0x30..]));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(system[0x38..]));
        TextureSet parsed = YtdSaving.Read(data);
        Assert.Equal(target, parsed.Target);
        Assert.Empty(parsed.Textures);
        // RPF storage can recompress an equivalent resource.
        byte[] recompressed = resource.Write(System.IO.Compression.CompressionLevel.Fastest);
        Assert.Empty(YtdSaving.Read(recompressed).Textures);
    }

    [AvaloniaFact]
    public void CorruptEmptyYtdIsRejected()
    {
        ResourceFile resource = ResourceFile.Read(EmptyYtd(GameTarget.Legacy));
        byte[] system = resource.SystemData.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(system.AsSpan(0x28), 1);
        byte[] corrupted = ResourceFile.Create(resource.Header.Version, system, resource.GraphicsData,
            resource.Header.SystemFlags, resource.Header.GraphicsFlags).Write();
        Assert.ThrowsAny<Exception>(() => YtdSaving.Read(corrupted));
        Assert.ThrowsAny<Exception>(() => YtdSaving.Read(EmptyYtd(GameTarget.Legacy).AsSpan(0, 24)));
    }

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy, 1, false)]
    [InlineData(GameTarget.Legacy, 3, false)]
    [InlineData(GameTarget.Enhanced, 1, false)]
    [InlineData(GameTarget.Enhanced, 3, true)]
    [InlineData(GameTarget.Legacy, 3, true)]
    public Task RemoveEveryTextureSaveReopenAndUndo(GameTarget target, int count, bool inArchive) => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureSet original = Dictionary(target, [.. Enumerable.Range(0, count).Select(index => $"tex_{index}")]);
        byte[] bytes = YtdSaving.Serialize(original);
        string path = folder.File("empty.ytd"), archivePath = folder.File("test.rpf");
        Action<byte[]>? saver = null;
        Action? prepare = null;
        string source = path, root = "";
        if (inArchive)
        {
            var archive = new RpfArchive("test.rpf");
            ArchiveWriter.AddStandalone(archive, "empty.ytd", bytes);
            archive.Save(archivePath);
            var entry = new ArchiveEntryTarget(archivePath, "empty.ytd", null, ArchiveNode.OpenRoot(archivePath, null));
            (saver, prepare, source, root) = (entry.Save, entry.Prepare, archivePath + "::empty.ytd", archivePath);
        }
        else
            File.WriteAllBytes(path, bytes);
        TextureViewerViewModel viewer = Viewer();
        viewer.OpenDictionary("empty.ytd", source, bytes, saver, prepare, root);
        Settle(viewer);
        Texture[] loaded = [.. viewer.Dictionary!.Textures];
        viewer.SelectAllTextures();
        Assert.True(viewer.CanRemoveSelection);
        Assert.True(viewer.RemoveSelected());
        Settle(viewer);
        Assert.Equal((0, 0, -1), (viewer.TextureCount, viewer.SelectedCount, viewer.SelectedIndex));
        Assert.Equal("", viewer.PreviewUrl);
        Assert.False(viewer.PreviewLoading);
        Assert.Equal("Texture dictionary is empty", viewer.Status);
        Assert.True(viewer.Modified);
        Assert.True(viewer.HasDocument);
        Assert.False(viewer.CanRemoveSelection);
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.False(viewer.Modified, viewer.Status);
        byte[] data = inArchive ? ReadArchived(archivePath, "empty.ytd") : File.ReadAllBytes(path);
        TextureSet saved = YtdSaving.Read(data);
        Assert.Empty(saved.Textures);
        Assert.Equal(target, saved.Target);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(loaded, viewer.Dictionary!.Textures);
        Assert.True(viewer.Modified);
        Assert.Equal(count, viewer.SelectedCount);
        Assert.Equal(target, viewer.Dictionary.Target);

        // Reopen the actual saved empty file.
        if (inArchive)
        {
            var entry = new ArchiveEntryTarget(archivePath, "empty.ytd", null, ArchiveNode.OpenRoot(archivePath, null));
            (saver, prepare) = (entry.Save, entry.Prepare);
        }
        int prompts = 0;
        viewer.UnsavedChangesRequested += (_, _) => prompts++;
        viewer.OpenDictionary("empty.ytd", source, data, saver, prepare, root);
        Assert.Equal(1, prompts);
        _ = viewer.ResolvePendingChanges("discard");
        Settle(viewer);
        Assert.Equal("", viewer.Error);
        Assert.True(viewer.HasDocument);
        Assert.Equal(0, viewer.TextureCount);
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.False(viewer.Saving);
        Assert.False(viewer.OperationBusy);
    });

    private static byte[] ReadArchived(string archivePath, string name)
    {
        ArchiveNode node = ArchiveNode.OpenRoot(archivePath, null);
        try
        { return ((RpfResourceFileEntry)node.Archive.Find(name)!).ReadStandalone(); }
        finally
        { node.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public Task SaveEmptyYtdAsPreservesTarget(GameTarget target) => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogs { SaveFile = folder.File("exported") };
        TextureViewerViewModel viewer = Opened("empty.ytd", EmptyYtd(target), dialogs: dialogs);
        Assert.True(viewer.HasDocument);
        Assert.False(viewer.CanSaveSource);
        Assert.True(viewer.CanSaveAs);
        Assert.True(await viewer.SaveYtdAs());
        Settle(viewer);
        string destination = folder.File("exported.ytd");
        Assert.True(viewer.CanSaveSource);
        Assert.Equal(destination, viewer.SourcePath);
        Assert.Equal("exported.ytd", viewer.SourceName);
        Assert.Equal(["Save texture dictionary"], dialogs.Titles);
        TextureSet saved = YtdSaving.Read(File.ReadAllBytes(destination));
        Assert.Equal(target, saved.Target);
        Assert.Empty(saved.Textures);
    });

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy)]
    [InlineData(GameTarget.Enhanced)]
    public Task UndoLastRemovalPreservesAnImportedUnnamedTexture(GameTarget target) => Headless.Run(() =>
    {
        ResourceFile resource = ResourceFile.Read(YtdSaving.Serialize(Dictionary(target, "named")));
        byte[] system = resource.SystemData.ToArray();
        ulong array = U64(system, SystemBase + 0x30);
        ulong descriptor = U64(system, array);
        ulong name = U64(system, descriptor + 0x28);
        system[(int)(name - SystemBase)] = 0;
        byte[] unnamed = ResourceFile.Create(resource.Header.Version, system, resource.GraphicsData,
            resource.Header.SystemFlags, resource.Header.GraphicsFlags).Write();
        TextureViewerViewModel viewer = Opened("unnamed.ytd", unnamed);
        Assert.Equal("", viewer.Error);
        Assert.Equal(1, viewer.TextureCount);
        Texture original = viewer.Dictionary!.Textures[0];
        Assert.Equal("", original.Name);
        Assert.True(viewer.RemoveSelected());
        Assert.Equal(0, viewer.TextureCount);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal([original], viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
        Assert.ThrowsAny<Exception>(() => YtdSaving.Serialize(viewer.Dictionary)); // Undo is not an export-validation bypass.
    });

    [AvaloniaFact]
    public Task InvalidSaveNeverTouchesTheDestinationOrTheSaver() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        string path = folder.File("existing.ytd");
        byte[] original = YtdSaving.Serialize(Dictionary("keep"));
        File.WriteAllBytes(path, original);
        var calls = new List<byte[]>();
        TextureViewerViewModel loose = Viewer();
        loose.OpenDictionary("existing.ytd", path, original);
        Settle(loose);
        loose.DictionaryLoaded(loose.Generation, Dictionary("duplicate", "DUPLICATE"));
        Assert.True(loose.SaveYtd());
        Settle(loose);
        Assert.StartsWith("Could not save existing.ytd: ", loose.Status);
        Assert.Equal(original, File.ReadAllBytes(path));

        var saveResults = new List<bool>();
        var writes = new List<SourceWriteResult>();
        TextureViewerViewModel archived = Viewer();
        archived.SaveFinished += (_, success) => saveResults.Add(success);
        archived.SourceWriteFinished += (_, result) => writes.Add(result);
        archived.OpenDictionary("existing.ytd", "root.rpf::existing.ytd", original, calls.Add, null, "root.rpf");
        Settle(archived);
        archived.DictionaryLoaded(archived.Generation, Dictionary("duplicate", "DUPLICATE"));
        Assert.True(archived.SaveYtd());
        Settle(archived);
        Assert.Empty(calls);
        Assert.Equal([false], saveResults);
        Assert.Equal([new SourceWriteResult("root.rpf", false)], writes);
    });
}
