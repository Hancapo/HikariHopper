using System.Buffers.Binary;
using FiveFury;
using FiveFury.Cryptography;
using FiveFury.Resources;
using FiveFury.Rpf;
using FiveFury.Textures;
using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Preservation checks on original Legacy assets from <c>x64c.rpf</c> (<see cref="TestGames"/>), read into
/// memory; writes go only to memory or temporary copies.</summary>
public sealed class YdrTextureTests
{
    private const ulong SystemBase = 0x50000000;
    private const string Props = "levels/gta5/props/";
    private const string BreadRack = Props + "commercial/v_fastfood.rpf/prop_bread_rack_01.ydr";
    private static readonly Lazy<(string Root, GameCrypto Crypto)> Game = new(() =>
    {
        string root = TestGames.Legacy();
        return (root, RpfProvider.LoadGame(root).Crypto);
    });
    private static readonly Dictionary<string, byte[]> Assets = [];

    private static byte[] Asset(string path)
    {
        TestGames.Legacy();
        lock (Assets)
        {
            if (Assets.TryGetValue(path, out byte[]? cached))
                return cached;
            ArchiveNode node = ArchiveNode.OpenRoot(Path.Combine(Game.Value.Root, "x64c.rpf"), Game.Value.Crypto);
            try
            {
                var (_, entry) = node.FindEntry(path) ?? throw new FileNotFoundException(path);
                return Assets[path] = ((RpfResourceFileEntry)entry).ReadStandalone();
            }
            finally
            {
                node.Close();
            }
        }
    }

    private static YdrTextureDocument Document(byte[] raw) => YdrTextureDocument.Read(raw);

    private static (ResourceHeader Header, byte[] System, byte[] Graphics) Sections(byte[] data)
    {
        ResourceFile resource = ResourceFile.Read(data);
        return (resource.Header, resource.SystemData.ToArray(), resource.GraphicsData.ToArray());
    }

    private static byte[] Rebuild(ResourceHeader header, byte[] system, byte[] graphics) =>
        ResourceFile.Create(header.Version, system, graphics, header.SystemFlags, header.GraphicsFlags).Write();

    private static int Offset(byte[] system, int field) => (int)(BinaryPrimitives.ReadUInt64LittleEndian(system.AsSpan(field)) - SystemBase);

    /// <summary>System offset of the shader group's texture dictionary pointer.</summary>
    private static int DictionaryField(byte[] system) => Offset(system, 0x10) + 8;

    /// <summary>Descriptor offsets by name in the embedded texture dictionary.</summary>
    private static Dictionary<string, int> Descriptors(byte[] system)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        int dictionary = Offset(system, DictionaryField(system));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(system.AsSpan(dictionary + 0x38));
        int array = Offset(system, dictionary + 0x30);
        for (int index = 0; index < count; index++)
        {
            int descriptor = Offset(system, array + 8 * index);
            int name = Offset(system, descriptor + 0x28);
            result[System.Text.Encoding.UTF8.GetString(system, name, Array.IndexOf(system, (byte)0, name) - name)] = descriptor;
        }
        return result;
    }

    /// <summary>Everything but the textures reads back the same: geometry, bounds, LODs, skeleton and materials.</summary>
    private static void AssertPreserved(byte[] original, byte[] saved)
    {
        var before = FiveFury.Ydr.Ydr.Read(original);
        var after = FiveFury.Ydr.Ydr.Read(saved);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Target, after.Target);
        Assert.Equal(before.Drawable.Box, after.Drawable.Box);
        Assert.Equal(before.Drawable.Sphere, after.Drawable.Sphere);
        Assert.Equal(before.Drawable.Skeleton?.Bones.Count, after.Drawable.Skeleton?.Bones.Count);
        foreach (CommonFury.Assets.DrawableLod lod in Enum.GetValues<CommonFury.Assets.DrawableLod>())
            Assert.Equal(before.Drawable.Lods[lod].Distance, after.Drawable.Lods[lod].Distance);
        Assert.Equal(before.Drawable.Materials.Select(material => material.ShaderHash), after.Drawable.Materials.Select(material => material.ShaderHash));
        var geometries = before.Drawable.Geometries.Zip(after.Drawable.Geometries).ToList();
        Assert.Equal(before.Drawable.Geometries.Count(), geometries.Count);
        Assert.Equal(before.Drawable.Geometries.Count(), after.Drawable.Geometries.Count());
        foreach ((var left, var right) in geometries)
        {
            Assert.Equal(left.Box, right.Box);
            Assert.Equal(left.MaterialIndex, right.MaterialIndex);
            Assert.True(left.Vertices.Data.SequenceEqual(right.Vertices.Data), "Vertex data changed");
            Assert.True(left.Indices.Indices.SequenceEqual(right.Indices.Indices), "Index data changed");
        }
    }

    private static int TotalSize(byte[] data) => (int)ResourceFile.Read(data).Header.TotalSize;

    /// <summary>Graphics address of each embedded texture's payload, by name.</summary>
    private static Dictionary<string, ulong> PayloadAddresses(byte[] data)
    {
        var (_, system, _) = Sections(data);
        return Descriptors(system).ToDictionary(pair => pair.Key, pair => BinaryPrimitives.ReadUInt64LittleEndian(system.AsSpan(pair.Value + 0x70)));
    }

    /// <summary>Material texture names read independently by FiveFury.NET's drawable reader.</summary>
    private static List<string?> MaterialNames(byte[] data) =>
        [.. FiveFury.Ydr.Ydr.Read(data).Drawable.Materials.SelectMany(material => material.EnumerateTextureReferences())
            .Select(reference => reference.Name)];

    private static TextureSet Legacy(IEnumerable<Texture> textures) => new(textures, GameTarget.Legacy);

    private static readonly Dictionary<string, string> NoRenames = [];

    [AvaloniaFact]
    public void NoopKeepsOriginalBytes()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Assert.Equal(raw, document.Serialize(document.Dictionary, NoRenames));
    }

    [AvaloniaFact]
    public void ReplacementPreservesEntireNontextureResource()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        List<Texture> textures = [.. document.Dictionary.Textures];
        Texture old = textures[0];
        textures[0] = TextureOperations.Resize(old, old.Width * 2, old.Height * 2, ImageFilter.Mitchell, 4, true);
        byte[] result = document.Serialize(Legacy(textures), NoRenames);
        AssertPreserved(raw, result);
        Texture reopened = Document(result).Dictionary.Textures.Single(texture => texture.Name == old.Name);
        Assert.Equal(textures[0].Data.ToArray(), reopened.Data.ToArray());
        Assert.Equal(old.Width * 2, reopened.Width);
        Assert.Equal(result, document.Serialize(Legacy(textures), NoRenames));
    }

    [AvaloniaFact]
    public void RenameKeepsPayloadsInPlaceWithoutGrowing()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Texture old = document.Dictionary.Textures[0];
        byte[] result = document.Serialize(
            Legacy(document.Dictionary.Textures.Select(texture => ReferenceEquals(texture, old) ? TextureOperations.WithName(texture, "hikari_renamed") : texture)),
            new Dictionary<string, string> { [old.Name!] = "hikari_renamed" });
        AssertPreserved(raw, result);
        // Untouched pixel data stays where it was, and the resource does not grow.
        Dictionary<string, ulong> before = PayloadAddresses(raw), after = PayloadAddresses(result);
        Assert.Equal(before[old.Name!], after["hikari_renamed"]);
        foreach (Texture texture in document.Dictionary.Textures.Skip(1))
            Assert.Equal(before[texture.Name!], after[texture.Name!]);
        Assert.True(TotalSize(result) <= TotalSize(raw), $"Grew from {TotalSize(raw)} to {TotalSize(result)} bytes");
    }

    [AvaloniaFact]
    public void RepeatedEditSessionsDoNotGrowTheResource()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument first = Document(raw);
        List<Texture> textures = [.. first.Dictionary.Textures];
        textures[0] = TextureOperations.Resize(textures[0], textures[0].Width * 2, textures[0].Height * 2, ImageFilter.Mitchell, 4, true);
        byte[] current = first.Serialize(Legacy(textures), NoRenames);
        int settled = TotalSize(current);
        // Each round reopens the saved file, as closing and reopening the editor does, and edits it again,
        // alternating a pixel edit with a rename: the two edits that used to stack copies.
        for (int round = 0; round < 6; round++)
        {
            YdrTextureDocument document = Document(current);
            List<Texture> edited = [.. document.Dictionary.Textures];
            int index = round % edited.Count;
            Texture target = edited[index];
            string name = target.Name!;
            if (round % 2 == 0)
                edited[index] = Repainted(target);
            else
                edited[index] = TextureOperations.WithName(target, name = name.EndsWith("_x") ? name[..^2] : name + "_x");
            byte[] next = document.Serialize(Legacy(edited), name == target.Name ? NoRenames : new Dictionary<string, string> { [target.Name!] = name });
            AssertPreserved(current, next);
            Assert.True(TotalSize(next) <= settled, $"Round {round}: grew from {settled} to {TotalSize(next)} bytes");
            current = next;
        }
    }

    [AvaloniaFact]
    public void ReplacingATextureReusesItsOldSpace()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        List<Texture> textures = [.. document.Dictionary.Textures];
        Texture old = textures[0];
        // Same size, different pixels: the new payload fits where the old one was.
        textures[0] = Repainted(old);
        Assert.Equal(old.Data.Length, textures[0].Data.Length);
        Assert.False(old.Data.SequenceEqual(textures[0].Data));
        byte[] result = document.Serialize(Legacy(textures), NoRenames);
        AssertPreserved(raw, result);
        Assert.Equal(TotalSize(raw), TotalSize(result));
        Assert.Equal(textures[0].Data.ToArray(), Document(result).Dictionary.Textures.Single(texture => texture.Name == old.Name).Data.ToArray());
    }

    /// <summary>The same texture with different pixels: exactly the same size, format and mip chain.</summary>
    private static Texture Repainted(Texture texture)
    {
        byte[] data = texture.Data.ToArray();
        for (int index = 0; index < data.Length; index += 64)
            data[index] ^= 0x5A;
        return new Texture(texture.Name, texture.Width, texture.Height, texture.Format, texture.MipCount, data, texture.Shape)
        {
            Usage = texture.Usage,
            UsageFlags = texture.UsageFlags,
            ReferenceCount = texture.ReferenceCount,
        };
    }

    [AvaloniaFact]
    public void RenameUpdatesEmbeddedMaterialBindings()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Texture old = document.Dictionary.Textures[0];
        TextureSet textures = Legacy(document.Dictionary.Textures.Select(texture =>
            ReferenceEquals(texture, old) ? TextureOperations.WithName(texture, "hikari_renamed") : texture));
        byte[] result = document.Serialize(textures, new Dictionary<string, string> { [old.Name!] = "hikari_renamed" });
        AssertPreserved(raw, result);
        List<string?> before = MaterialNames(raw);
        Assert.Contains(old.Name, before);
        Assert.Equal(before.Select(name => name == old.Name ? "hikari_renamed" : name), MaterialNames(result));
    }

    [AvaloniaFact]
    public void RemoveAllKeepsMaterialNamesAndModel()
    {
        byte[] raw = Asset(BreadRack);
        byte[] result = Document(raw).Serialize(Legacy([]), NoRenames);
        AssertPreserved(raw, result);
        Assert.Empty(Document(result).Dictionary.Textures);
        Assert.Equal(MaterialNames(raw), MaterialNames(result));
    }

    [AvaloniaFact]
    public void CaseOnlyRenameRemainsBoundOnNextSave()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Texture old = document.Dictionary.Textures[0];
        string upper = old.Name!.ToUpperInvariant();
        Assert.NotEqual(old.Name, upper);
        byte[] result = document.Serialize(Legacy([TextureOperations.WithName(old, upper), .. document.Dictionary.Textures.Skip(1)]),
            new Dictionary<string, string> { [old.Name] = upper });
        byte[] final = Document(result).Serialize(Legacy([]), NoRenames);
        Assert.Equal(MaterialNames(result), MaterialNames(final));
    }

    [AvaloniaFact]
    public void RejectsZeroCountDictionaryWithNonzeroTextureCount()
    {
        var (header, system, graphics) = Sections(Asset(BreadRack));
        int dictionary = Offset(system, DictionaryField(system));
        BinaryPrimitives.WriteUInt16LittleEndian(system.AsSpan(dictionary + 0x28), 0);
        // An inconsistent dictionary is refused rather than opened as something else.
        Assert.ThrowsAny<Exception>(() => Document(Rebuild(header, system, graphics)));
    }

    [AvaloniaFact]
    public void DictionaryWithoutTexturesCanBeEdited()
    {
        YdrTextureDocument document = Document(Asset(BreadRack));
        YdrTextureDocument empty = Document(document.Serialize(Legacy([]), NoRenames));
        byte[] result = empty.Serialize(Legacy([document.Dictionary.Textures[0]]), NoRenames);
        Assert.Single(Document(result).Dictionary.Textures);
    }

    [AvaloniaFact]
    public Task AYdrWithNoEmbeddedTexturesIsNotOpened() => Headless.Run(() =>
    {
        byte[] withTextures = Asset(BreadRack);
        byte[] without = Document(withTextures).Serialize(Legacy([]), NoRenames);
        TextureViewerViewModel viewer = Viewer();
        int opened = 0;
        var declined = new List<string>();
        viewer.OpenRequested += (_, _) => opened++;
        viewer.OpenDeclined += (_, message) => declined.Add(message);

        // Nothing to edit: the editor is never asked to open, and says why.
        viewer.OpenDictionary("prop_bare.ydr", "", without);
        Settle(viewer);
        Assert.Equal(0, opened);
        Assert.Equal(["prop_bare.ydr has no embedded textures"], declined);
        Assert.False(viewer.HasDocument);

        // One with textures opens as before.
        viewer.OpenDictionary("prop_bread_rack_01.ydr", "", withTextures);
        Settle(viewer);
        Assert.Equal(1, opened);
        Assert.True(viewer.HasDocument);
        Assert.Single(declined);
    });

    [AvaloniaFact]
    public void ZeroCountEmbeddedDictionaryIsNotRejected()
    {
        var (header, system, graphics) = Sections(Asset(BreadRack));
        int dictionary = Offset(system, DictionaryField(system));
        system.AsSpan(dictionary + 0x20, 0x20).Clear();
        Assert.Empty(Document(Rebuild(header, system, graphics)).Dictionary.Textures);
    }

    [AvaloniaFact]
    public void RejectsWrongEditionAndInvalidDictionary()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        var edition = Assert.Throws<ArgumentException>(() =>
            document.Serialize(new TextureSet(document.Dictionary.Textures, GameTarget.Enhanced), NoRenames));
        Assert.Contains("edition", edition.Message);
        byte[] broken = raw.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(4), 159);
        Assert.ThrowsAny<Exception>(() => Document(broken));
    }

    [AvaloniaFact]
    public void AddedTexturePreservesExistingMaterials()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Texture added = TextureOperations.WithName(document.Dictionary.Textures[0], "hikari_added");
        byte[] result = document.Serialize(Legacy([.. document.Dictionary.Textures, added]), NoRenames);
        AssertPreserved(raw, result);
        Assert.Equal(document.Dictionary.Textures.Length + 1, Document(result).Dictionary.Textures.Length);
    }

    [AvaloniaFact]
    public void MalformedDictionaryIsNotOpenedAsEmpty()
    {
        var (header, system, graphics) = Sections(Asset(BreadRack));
        BinaryPrimitives.WriteUInt64LittleEndian(system.AsSpan(DictionaryField(system)), SystemBase + (ulong)system.Length + 16);
        Assert.ThrowsAny<Exception>(() => Document(Rebuild(header, system, graphics)));
    }

    [AvaloniaFact]
    public void TruncatedTexturePayloadIsRejectedOnOpen()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        var (header, system, graphics) = Sections(raw);
        Texture texture = document.Dictionary.Textures[0];
        int descriptor = Descriptors(system)[texture.Name!];
        BinaryPrimitives.WriteUInt64LittleEndian(system.AsSpan(descriptor + 0x70), 0x60000000UL + (ulong)(graphics.Length - texture.Data.Length + 8));
        Assert.ThrowsAny<Exception>(() => Document(Rebuild(header, system, graphics)));
    }

    [AvaloniaFact]
    public void AmbiguousDuplicateOriginsCannotBeRewritten()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        var (header, system, graphics) = Sections(raw);
        int dictionary = Offset(system, DictionaryField(system));
        int array = Offset(system, dictionary + 0x30);
        int first = Offset(system, array), second = Offset(system, array + 8);
        system.AsSpan(second + 0x28, 8).CopyTo(system.AsSpan(first + 0x28));
        // It opens; what can't be done is to write it back, as which texture an edit is for is ambiguous.
        YdrTextureDocument duplicate = Document(Rebuild(header, system, graphics));
        var error = Assert.ThrowsAny<InvalidDataException>(() => duplicate.Serialize(Legacy([duplicate.Dictionary.Textures[0]]), NoRenames));
        Assert.Contains("ambiguous", error.Message);
    }

    [AvaloniaFact]
    public void RenameRetainsOpaqueActiveDescriptorFields()
    {
        byte[] raw = Asset(BreadRack);
        YdrTextureDocument document = Document(raw);
        Texture old = document.Dictionary.Textures[0];
        byte[] saved = document.Serialize(Legacy([TextureOperations.WithName(old, "new_name"), .. document.Dictionary.Textures.Skip(1)]),
            new Dictionary<string, string> { [old.Name!] = "new_name" });
        Dictionary<string, byte[]> before = DescriptorBytes(raw), after = DescriptorBytes(saved);
        foreach ((string name, byte[] descriptor) in before)
        {
            byte[] active = after[name == old.Name ? "new_name" : name];
            byte[] expected = descriptor.ToArray();
            foreach (int field in new[] { 0x28, 0x70 })
                active.AsSpan(field, 8).CopyTo(expected.AsSpan(field));
            Assert.Equal(expected, active);
        }
    }

    private static Dictionary<string, byte[]> DescriptorBytes(byte[] raw)
    {
        var (_, system, _) = Sections(raw);
        return Descriptors(system).ToDictionary(pair => pair.Key, pair => system.AsSpan(pair.Value, 0x90).ToArray());
    }

    [AvaloniaFact]
    public void ExternalReferenceDoesNotBecomeEmbeddedOnRename()
    {
        YdrTextureDocument document = Document(Asset(BreadRack));
        YdrTextureDocument empty = Document(document.Serialize(Legacy([]), NoRenames));
        YdrTextureDocument readded = Document(empty.Serialize(document.Dictionary, NoRenames));
        Texture old = readded.Dictionary.Textures[0];
        byte[] saved = readded.Serialize(Legacy([TextureOperations.WithName(old, "new_name"), .. readded.Dictionary.Textures.Skip(1)]),
            new Dictionary<string, string> { [old.Name!] = "new_name" });
        Assert.Equal(MaterialNames(readded.Original), MaterialNames(saved));
    }

    [AvaloniaTheory]
    [InlineData("commercial/v_fastfood.rpf/prop_bread_rack_02.ydr")]
    [InlineData("commercial/v_fastfood.rpf/prop_chip_fryer.ydr")]
    [InlineData("commercial/v_office.rpf/prop_cabinet_02b.ydr")]
    [InlineData("commercial/v_office.rpf/prop_coathook_01.ydr")]
    [InlineData("commercial/v_office.rpf/prop_copier_01.ydr")]
    [InlineData("lev_des/p_v_lev_des_skin.rpf/cropduster1_skin.ydr")]
    [InlineData("lev_des/p_v_lev_des_skin.rpf/p_abat_roller_1.ydr")]
    [InlineData("lev_des/p_v_lev_des_skin.rpf/p_amb_drain_water_double.ydr")]
    public void RealCorpusPreservationAfterResize(string relative)
    {
        byte[] original = Asset(Props + relative);
        YdrTextureDocument document = Document(original);
        Texture old = document.Dictionary.Textures[0];
        Texture resized = TextureOperations.Resize(old, Math.Max(4, old.Width / 2), Math.Max(4, old.Height / 2), ImageFilter.Mitchell, 4, true);
        TextureSet textures = Legacy([resized, .. document.Dictionary.Textures.Skip(1)]);
        byte[] result = document.Serialize(textures, NoRenames);
        AssertPreserved(original, result);
        Assert.Equal(textures.Textures.ToDictionary(texture => texture.Name!, texture => Convert.ToHexString(texture.Data)),
            Document(result).Dictionary.Textures.ToDictionary(texture => texture.Name!, texture => Convert.ToHexString(texture.Data)));
    }

    [AvaloniaFact]
    public void SaveInsideRealNestedRpfCopy()
    {
        byte[] raw = Asset(BreadRack);
        using var folder = new TempFolder();
        string copied = folder.File("v_fastfood.rpf");
        File.WriteAllBytes(copied, ReadBinary(Props + "commercial/v_fastfood.rpf"));
        YdrTextureDocument document = Document(raw);
        Texture old = document.Dictionary.Textures[0];
        byte[] peer;
        ArchiveNode node = ArchiveNode.OpenRoot(copied, Game.Value.Crypto);
        peer = ((RpfResourceFileEntry)node.FindEntry("prop_bread_rack_02.ydr")!.Value.Entry).ReadStandalone();
        var target = new ArchiveEntryTarget(copied, "prop_bread_rack_01.ydr", Game.Value.Crypto, node);
        target.Save(document.Serialize(Legacy([TextureOperations.WithName(old, "archive_rename"), .. document.Dictionary.Textures.Skip(1)]),
            new Dictionary<string, string> { [old.Name!] = "archive_rename" }));
        ArchiveNode saved = ArchiveNode.OpenRoot(copied, Game.Value.Crypto);
        try
        {
            Assert.Equal(peer, ((RpfResourceFileEntry)saved.FindEntry("prop_bread_rack_02.ydr")!.Value.Entry).ReadStandalone());
            AssertPreserved(raw, ((RpfResourceFileEntry)saved.FindEntry("prop_bread_rack_01.ydr")!.Value.Entry).ReadStandalone());
        }
        finally
        {
            saved.Close();
        }
    }

    private static byte[] ReadBinary(string path)
    {
        Asset(BreadRack);
        ArchiveNode node = ArchiveNode.OpenRoot(Path.Combine(Game.Value.Root, "x64c.rpf"), Game.Value.Crypto);
        try
        { return ((RpfBinaryFileEntry)node.FindEntry(path)!.Value.Entry).ReadStandalone(); }
        finally
        { node.Close(); }
    }

    private static TextureViewerViewModel OpenedDrawable(TempFolder folder, out string destination)
    {
        byte[] raw = Asset(BreadRack);
        destination = folder.File("model.ydr");
        File.WriteAllBytes(destination, raw);
        TextureViewerViewModel viewer = Viewer();
        viewer.OpenDictionary("model.ydr", destination, raw);
        Settle(viewer);
        Assert.True(viewer.IsDrawable);
        Assert.Equal(2, viewer.TextureCount);
        return viewer;
    }

    [AvaloniaFact]
    public Task ViewerYdrRenameUndoAndSave() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = OpenedDrawable(folder, out string destination);
        byte[] raw = Asset(BreadRack);
        string original = viewer.TexturesModel[0].Name;
        Assert.True(viewer.RenameSelected("renamed_twice"));
        Assert.True(viewer.RenameSelected("last_name"));
        Assert.True(viewer.Undo());
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        byte[] result = File.ReadAllBytes(destination);
        AssertPreserved(raw, result);
        Assert.Contains("renamed_twice", MaterialNames(result));
        Assert.True(viewer.Undo());
        Assert.Equal(original, viewer.TexturesModel[0].Name);
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.Equal(raw, File.ReadAllBytes(destination));
    });

    [AvaloniaFact]
    public Task ViewerYdrSaveAsRetainsContainer() => Headless.Run(async () =>
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogs { SaveFile = folder.File("output") };
        TextureViewerViewModel viewer = Viewer(dialogs: dialogs);
        viewer.OpenDictionary("source.ydr", "source.ydr", Asset(BreadRack));
        Settle(viewer);
        Assert.Equal(2, viewer.TextureCount);
        Assert.True(viewer.RenameSelected("save_as"));
        Assert.True(await viewer.SaveYtdAs());
        Settle(viewer);
        Assert.Equal(["Save drawable"], dialogs.Titles);
        Assert.True(File.Exists(folder.File("output.ydr")));
        Assert.False(File.Exists(folder.File("output.ytd")));
        AssertPreserved(Asset(BreadRack), File.ReadAllBytes(folder.File("output.ydr")));
    });

    [AvaloniaFact]
    public Task InvalidRenamePreservesDestinationAndModifiedState() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = OpenedDrawable(folder, out string destination);
        // A NUL cannot be represented in resource C strings: the name is refused, nothing changes and nothing is saved.
        Assert.False(viewer.RenameSelected("broken\0name"));
        Assert.False(viewer.Modified);
        Assert.Equal(Asset(BreadRack), File.ReadAllBytes(destination));
    });

    [AvaloniaFact]
    public Task DeletedNameReuseDoesNotRebindRemovedTexture() => Headless.Run(() =>
    {
        using var folder = new TempFolder();
        TextureViewerViewModel viewer = OpenedDrawable(folder, out string destination);
        string deleted = viewer.TexturesModel[0].Name, remaining = viewer.TexturesModel[1].Name;
        Assert.True(viewer.RemoveSelected());
        Assert.True(viewer.RenameSelected(deleted));
        Assert.True(viewer.RenameSelected("last_name"));
        Assert.True(viewer.SaveYtd());
        Settle(viewer);
        Assert.Equal(MaterialNames(Asset(BreadRack)).Select(name => name == remaining ? "last_name" : name),
            MaterialNames(File.ReadAllBytes(destination)));
    });

    [AvaloniaFact]
    public Task SingleRemoveKeepsCompactUndoHistory() => Headless.Run(() =>
    {
        TextureSet dictionary = Document(Asset(BreadRack)).Dictionary;
        TextureViewerViewModel viewer = Loaded(dictionary);
        viewer.UndoDataLimit = dictionary.Textures.Sum(texture => (long)texture.Data.Length);
        Assert.True(viewer.RemoveSelected());
        Assert.True(viewer.RenameSelected("renamed"));
        Assert.True(viewer.Undo());
        Assert.True(viewer.Undo(), "Removal retained unchanged texture data and exhausted the undo budget");
        Assert.Equal(dictionary.Textures, viewer.Dictionary!.Textures);
    });

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task RemoveDeletesTheSelectionAndUndoRestoresIt(bool allSelected, bool duplicateNeighbor) => Headless.Run(() =>
    {
        TextureSet dictionary = Document(Asset(BreadRack)).Dictionary;
        if (duplicateNeighbor)
            dictionary = Legacy([.. dictionary.Textures, dictionary.Textures[1]]);
        TextureViewerViewModel viewer = Loaded(dictionary);
        if (allSelected)
            viewer.SelectAllTextures();
        int expected = viewer.TextureCount - viewer.SelectedCount;
        Assert.True(viewer.CanRemove);
        Assert.True(viewer.RemoveSelected());
        Assert.Equal(expected, viewer.TextureCount);
        Assert.True(viewer.Modified);
        Assert.True(viewer.Undo());
        Assert.Equal(dictionary.Textures, viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
    });
}
