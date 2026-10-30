using CommonFury;
using System.Buffers.Binary;
using System.Text;
using CommonFury.Hashing;
using FiveFury;
using FiveFury.Resources;
using FiveFury.Textures;
using FiveFury.Ytd;

namespace HikariHopper.Textures;

/// <summary>AD HOC Legacy YDR embedded-texture editing that preserves the drawable.</summary>
/// <remarks>
/// <para>FiveFury.NET's <see cref="FiveFury.Ydr.Ydr.Write"/> is a graph writer: it relocates every allocation and
/// rejects drawables with unmodeled data, and its <see cref="ResourceEditSession"/> only patches equal-size bytes in
/// place. Neither replaces an embedded texture dictionary while preserving the drawable, so this adapter remains. It
/// never falls back to the rebuilding writer.</para>
/// <para>Both original sections are retained at their original addresses; a new texture dictionary, laid out by
/// FiveFury's YTD writer, is appended and relocated, and only the page-info, dictionary and embedded material texture
/// pointers are patched. One page per section makes every retained allocation, including opaque ones, page-contained.
/// Old texture bytes remain orphaned, so reopening and saving again can grow the resource.</para>
/// </remarks>
public sealed class YdrTextureDocument
{
    private const ulong SystemBase = 0x50000000, GraphicsBase = 0x60000000;
    private const int MaxSection = 128 * 1024 * 1024;
    private const int DescriptorSize = 0x90;
    private const uint TextureBaseVft = 0x40617568;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private YdrTextureDocument(byte[] original, byte[] system, byte[] graphics, TextureSet dictionary, int group,
        IReadOnlyList<(int Field, string Name)> bindings)
    {
        Original = original;
        System = system;
        Graphics = graphics;
        Dictionary = dictionary;
        Group = group;
        Bindings = bindings;
    }

    /// <summary>The complete source resource, returned unchanged by a no-op save.</summary>
    public byte[] Original { get; }
    internal byte[] System { get; }
    internal byte[] Graphics { get; }
    /// <summary>The embedded textures as read.</summary>
    public TextureSet Dictionary { get; }
    /// <summary>System offset of the drawable's shader group.</summary>
    public int Group { get; }
    /// <summary>Material texture parameter pointer fields and the names they referenced.</summary>
    public IReadOnlyList<(int Field, string Name)> Bindings { get; }

    public static YdrTextureDocument Read(byte[] data)
    {
        if (data.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(data) != ResourceHeader.Magic)
            throw new InvalidDataException("Not a standalone YDR resource");
        ResourceHeader header = ResourceHeader.Read(data);
        if (header.Version != 165)
            throw new NotSupportedException("Embedded texture editing currently supports Legacy YDR only");
        if (Math.Max(header.SystemSize, header.GraphicsSize) > MaxSection)
            throw new InvalidDataException("YDR exceeds the preservation editor's 128 MiB section limit");
        ResourceFile resource = ResourceFile.Read(data);
        byte[] system = resource.SystemData.ToArray(), graphics = resource.GraphicsData.ToArray();
        int group = Offset(system, U64(system, 0x10), 0x20);
        ulong dictionaryPointer = U64(system, group + 8);
        Dictionary<string, int> descriptors = Descriptors(system, dictionaryPointer);
        // Never treat an unreadable dictionary as an empty one.
        IEnumerable<Texture> textures = descriptors.Count > 0
            ? TextureDictionary.ReadEmbedded(resource, dictionaryPointer, GameTarget.Legacy).Textures : [];
        var dictionary = new TextureSet(textures, GameTarget.Legacy);
        foreach (Texture texture in dictionary.Textures)
        {
            texture.Validate().ThrowIfInvalid();
            if (!descriptors.TryGetValue(texture.Name ?? "", out int descriptor))
                throw new InvalidDataException("YDR texture names disagree with their descriptors");
            if (BinaryPrimitives.ReadUInt16LittleEndian(system.AsSpan(descriptor + 0x54)) != 1)
                throw new NotSupportedException("Only 2D Legacy textures are supported by the YDR preservation editor");
            long start = (long)U64(system, descriptor + 0x70) - (long)GraphicsBase;
            if (start < 0 || start + texture.Data.Length > graphics.Length)
                throw new InvalidDataException("YDR texture payload exceeds its graphics section");
        }
        return new YdrTextureDocument(data.ToArray(), system, graphics, dictionary, group, ReadBindings(system, group));
    }

    /// <summary>Writes <paramref name="dictionary"/> into a copy of the drawable. <paramref name="renames"/> maps
    /// original embedded names to current names; <paramref name="deleted"/> lists removed original names.</summary>
    public byte[] Serialize(TextureSet dictionary, IReadOnlyDictionary<string, string> renames, IReadOnlySet<string>? deleted = null)
    {
        deleted ??= new HashSet<string>(StringComparer.Ordinal);
        if (dictionary.Target != GameTarget.Legacy)
            throw new ArgumentException("Legacy YDR textures cannot change edition");
        if (SameTextures(dictionary.Textures, Dictionary.Textures) && renames.Count == 0 && deleted.Count == 0)
            return Original;
        if (!dictionary.Textures.IsEmpty)
            dictionary.Validate().ThrowIfInvalid();
        if ((long)Graphics.Length + dictionary.Textures.Sum(texture => (long)texture.Data.Length) > MaxSection)
            throw new InvalidDataException("Edited YDR exceeds the preservation editor's 128 MiB section limit");
        var system = new List<byte>(System);
        var graphics = new List<byte>(Graphics);
        var oldTextures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        foreach (Texture texture in Dictionary.Textures)
            oldTextures[texture.Name ?? ""] = texture;
        ulong oldPointer = U64(System, Group + 8);
        Dictionary<string, int> oldDescriptors = Descriptors(System, oldPointer);
        if (oldDescriptors.Count != Dictionary.Textures.Length)
            throw new InvalidDataException("YDR has ambiguous duplicate texture origins; rewriting is not supported");
        // Descriptor identity distinguishes actual embedded references from external references with the same name.
        var owned = new Dictionary<int, string>();
        foreach ((int field, string name) in Bindings)
            if (oldDescriptors.TryGetValue(name, out int descriptor) && U64(System, field) == SystemBase + (ulong)descriptor)
                owned[field] = name;

        var pointers = new Dictionary<string, ulong>(StringComparer.Ordinal);
        ulong dictionaryPointer = 0;
        if (!dictionary.Textures.IsEmpty)
        {
            PreparedResourceSections prepared = dictionary.ToYtd().PrepareSections();
            Texture[] ordered = [.. dictionary.Textures.OrderBy(texture => Jenkins.Hash(texture.Name ?? ""))];
            int systemDelta = system.Count, graphicsDelta = graphics.Count;
            byte[] embedded = prepared.SystemData.ToArray();
            foreach (ResourceBlock block in prepared.SystemBlocks)
            {
                foreach (int pointerOffset in block.PointerOffsets)
                {
                    int field = block.Offset + pointerOffset;
                    ulong pointer = U64(embedded, field);
                    if (pointer != 0)
                        WriteU64(embedded, field, pointer + (ulong)(pointer >= GraphicsBase ? graphicsDelta : systemDelta));
                }
            }
            // Embedded dictionaries do not own a separate resource page table.
            WriteU64(embedded, 8, 0);
            system.AddRange(embedded);
            graphics.AddRange(prepared.GraphicsData.ToArray());
            dictionaryPointer = SystemBase + (ulong)systemDelta;
            byte[] current = [.. system];
            Dictionary<string, int> newDescriptors = Descriptors(current, dictionaryPointer);
            VerifyPayloads(current, [.. graphics], newDescriptors, ordered);
            var origins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in oldTextures.Keys.Where(name => !deleted.Contains(name)))
                origins[renames.GetValueOrDefault(name, name)] = name;
            foreach (Texture texture in ordered)
            {
                if (!origins.TryGetValue(texture.Name ?? "", out string? originalName))
                    continue;
                Texture source = oldTextures[originalName];
                int start = oldDescriptors[originalName], destination = newDescriptors[texture.Name!];
                byte[] preserved = System.AsSpan(start, DescriptorSize).ToArray();
                // Preserve opaque fields, VFT, refcount and authored stride. Only explicit edited metadata and
                // relocated pointers change.
                var fields = new List<(int Offset, int Size)> { (0x28, 8), (0x70, 8) };
                if (texture.Width != source.Width) fields.Add((0x50, 2));
                if (texture.Height != source.Height) fields.Add((0x52, 2));
                if (texture.Format != source.Format) fields.Add((0x58, 4));
                if (texture.MipCount != source.MipCount) fields.Add((0x5D, 1));
                if (texture.Width != source.Width || texture.Format != source.Format) fields.Add((0x56, 2));
                if (texture.Usage != source.Usage || texture.UsageFlags != source.UsageFlags) fields.Add((0x40, 4));
                foreach ((int offset, int size) in fields)
                    current.AsSpan(destination + offset, size).CopyTo(preserved.AsSpan(offset));
                preserved.CopyTo(current, destination);
            }
            if (oldPointer != 0)
            {
                int oldDictionary = Offset(System, oldPointer, 0x40);
                foreach ((int start, int end) in new[] { (0, 8), (0x10, 0x20), (0x2C, 0x30), (0x3C, 0x40) })
                    System.AsSpan(oldDictionary + start, end - start).CopyTo(current.AsSpan(systemDelta + start));
            }
            system = [.. current];
            foreach ((string name, int descriptor) in newDescriptors)
                pointers[name] = SystemBase + (ulong)descriptor;
        }

        var patches = new Dictionary<int, ulong> { [Group + 8] = dictionaryPointer };
        var external = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach ((int field, string oldName) in Bindings)
        {
            if (!owned.ContainsKey(field))
                continue;
            string name = renames.GetValueOrDefault(oldName, oldName);
            ulong? pointer = deleted.Contains(oldName) ? null : pointers.TryGetValue(name, out ulong found) ? found : null;
            if (pointer is null)
            {
                // Removed textures become named external references, never dangling pointers into an orphaned
                // embedded texture descriptor.
                if (!external.TryGetValue(name, out ulong reference))
                {
                    Pad(system, 16);
                    int descriptor = system.Count;
                    system.AddRange(new byte[0x50]);
                    ulong namePointer = SystemBase + (ulong)system.Count;
                    system.AddRange(Utf8.GetBytes(name));
                    system.Add(0);
                    byte[] header = new byte[0x50];
                    BinaryPrimitives.WriteUInt32LittleEndian(header, TextureBaseVft);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
                    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x28), namePointer);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x30), 1);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x32), 2);
                    for (int index = 0; index < header.Length; index++)
                        system[descriptor + index] = header[index];
                    external[name] = reference = SystemBase + (ulong)descriptor;
                }
                pointer = reference;
            }
            patches[field] = pointer.Value;
        }
        Pad(system, 16);
        int pages = system.Count;
        system.AddRange(new byte[32]); // Exactly two pages: system and graphics.
        system[pages + 8] = 1;
        system[pages + 9] = (byte)(graphics.Count > 0 ? 1 : 0);
        patches[8] = SystemBase + (ulong)pages;
        byte[] systemData = [.. system], graphicsData = [.. graphics];
        foreach ((int field, ulong pointer) in patches)
            WriteU64(systemData, field, pointer);
        if (Math.Max(systemData.Length, graphicsData.Length) > MaxSection)
            throw new InvalidDataException("Edited YDR exceeds the preservation editor's 128 MiB section limit");
        var resourceHeader = new ResourceHeader(165, SinglePageFlags(systemData.Length, 10), SinglePageFlags(graphicsData.Length, 5));
        if (!resourceHeader.TryResolveChunk(SystemBase, systemData.Length, out _)
            || graphicsData.Length > 0 && !resourceHeader.TryResolveChunk(GraphicsBase, graphicsData.Length, out _))
            throw new InvalidDataException("YDR sections are not contained in individual resource pages");
        byte[] expected = System.ToArray();
        foreach ((int field, ulong pointer) in patches)
            WriteU64(expected, field, pointer);
        if (!systemData.AsSpan(0, expected.Length).SequenceEqual(expected)
            || !graphicsData.AsSpan(0, Graphics.Length).SequenceEqual(Graphics))
            throw new InvalidDataException("YDR preservation verification failed");
        byte[] result = ResourceFile.Create(165, systemData, graphicsData, resourceHeader.SystemFlags, resourceHeader.GraphicsFlags).Write();

        YdrTextureDocument reread = Read(result);
        Texture[] before = [.. dictionary.Textures.OrderBy(texture => texture.Name, StringComparer.Ordinal)];
        Texture[] after = [.. reread.Dictionary.Textures.OrderBy(texture => texture.Name, StringComparer.Ordinal)];
        if (before.Length != after.Length || before.Zip(after).Any(pair => !TextureComparison.SameContent(pair.First, pair.Second)))
            throw new InvalidDataException("YDR round-trip changed texture data or metadata");
        var expectedNames = Bindings.Select(binding =>
            (binding.Field, owned.ContainsKey(binding.Field) ? renames.GetValueOrDefault(binding.Name, binding.Name) : binding.Name));
        if (!reread.Bindings.SequenceEqual(expectedNames))
            throw new InvalidDataException("YDR round-trip changed material texture bindings");
        return result;
    }

    /// <summary>Descriptor offsets by name for the dictionary at <paramref name="dictionary"/>; empty for a null pointer.</summary>
    internal static Dictionary<string, int> Descriptors(byte[] system, ulong dictionary)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (dictionary == 0)
            return result;
        int offset = Offset(system, dictionary, 0x40);
        int hashes = BinaryPrimitives.ReadUInt16LittleEndian(system.AsSpan(offset + 0x28));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(system.AsSpan(offset + 0x38));
        if (count != hashes)
            throw new InvalidDataException("YDR texture dictionary counts disagree");
        if (count == 0)
            return result;
        Offset(system, U64(system, offset + 0x20), count * 4);
        int array = Offset(system, U64(system, offset + 0x30), count * 8);
        for (int index = 0; index < count; index++)
        {
            int descriptor = Offset(system, U64(system, array + 8 * index), DescriptorSize);
            result[Name(system, descriptor)] = descriptor;
        }
        return result;
    }

    private static IReadOnlyList<(int Field, string Name)> ReadBindings(byte[] system, int group)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(system.AsSpan(group + 0x18));
        if (count == 0)
            return [];
        int array = Offset(system, U64(system, group + 0x10), count * 8);
        var result = new List<(int, string)>();
        for (int index = 0; index < count; index++)
        {
            int shader = Offset(system, U64(system, array + 8 * index), 0x20);
            int parameterCount = system[shader + 0x10];
            if (parameterCount == 0)
                continue;
            int parameters = Offset(system, U64(system, shader), parameterCount * 16);
            for (int parameter = 0; parameter < parameterCount; parameter++)
            {
                int field = parameters + parameter * 16 + 8;
                ulong pointer = U64(system, field);
                if (system[field - 8] == 0 && pointer != 0)
                    result.Add((field, Name(system, Offset(system, pointer, 0x50))));
            }
        }
        return result;
    }

    private static void VerifyPayloads(byte[] system, byte[] graphics, Dictionary<string, int> descriptors, Texture[] ordered)
    {
        if (descriptors.Count != ordered.Length)
            throw new InvalidDataException("Serialized YTD names do not match the model order");
        foreach (Texture texture in ordered)
        {
            if (!descriptors.TryGetValue(texture.Name ?? "", out int descriptor))
                throw new InvalidDataException("Serialized YTD names do not match the model order");
            long start = (long)U64(system, descriptor + 0x70) - (long)GraphicsBase;
            if (start < 0 || start + texture.Data.Length > graphics.Length
                || !graphics.AsSpan((int)start, texture.Data.Length).SequenceEqual(texture.Data))
                throw new InvalidDataException($"Serialized texture payload changed: {texture.Name}");
        }
    }

    /// <summary>Flags for exactly one page holding <paramref name="size"/> bytes: the smallest page that fits, encoded
    /// with the smallest base shift (FiveFury Python <c>get_resource_flags_from_size_with_page_count(size, version, 1)</c>).</summary>
    internal static ResourceFlags SinglePageFlags(int size, int versionNibble)
    {
        Span<int> counts = stackalloc int[9];
        if (size <= 0)
            return ResourceFlags.FromPageCounts(counts, versionNibble);
        int units = 0; // log2 of the page size in 0x200-byte leaves.
        while ((0x200L << units) < size)
            units++;
        int shift = Math.Max(0, units - 8);
        counts[8 - (units - shift)] = 1;
        return ResourceFlags.FromPageCounts(counts, versionNibble, shift);
    }

    private static bool SameTextures(IReadOnlyList<Texture> left, IReadOnlyList<Texture> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => TextureComparison.SameContent(pair.First, pair.Second));

    private static void Pad(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
            data.Add(0);
    }

    private static int Offset(byte[] data, ulong pointer, int size)
    {
        long offset = (long)pointer - (long)SystemBase;
        if (pointer < SystemBase || size < 0 || offset + size > data.Length)
            throw new InvalidDataException("YDR pointer exceeds its system section");
        return (int)offset;
    }

    private static ulong U64(byte[] data, int offset)
    {
        if (offset < 0 || offset + 8 > data.Length)
            throw new InvalidDataException("Truncated YDR pointer");
        return BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
    }

    private static void WriteU64(byte[] data, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);

    private static string Name(byte[] data, int descriptor)
    {
        int start = Offset(data, U64(data, descriptor + 0x28), 1);
        int end = Array.IndexOf(data, (byte)0, start, Math.Min(data.Length, start + 4096) - start);
        if (end < 0)
            throw new InvalidDataException("Unterminated YDR texture name");
        return Utf8.GetString(data, start, end - start);
    }
}
