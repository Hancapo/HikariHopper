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
/// <para>Everything outside the embedded texture dictionary keeps its original address. The old dictionary's
/// allocations (structure, arrays, descriptors, names, payloads) and the page table are reclaimed unless something else
/// points into them; the new dictionary, laid out by FiveFury's YTD writer, keeps unchanged payloads in place and fills
/// reclaimed space before the sections grow. Only the page-info, dictionary and embedded material texture pointers are
/// patched. One page per section makes every retained allocation, including opaque ones, page-contained.</para>
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
        var oldTextures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        foreach (Texture texture in Dictionary.Textures)
            oldTextures[texture.Name ?? ""] = texture;
        ulong oldPointer = U64(System, Group + 8);
        Dictionary<string, int> oldDescriptors = Descriptors(System, oldPointer);
        if (oldDescriptors.Count != Dictionary.Textures.Length)
            throw new InvalidDataException("YDR has ambiguous duplicate texture origins; rewriting is not supported");
        Dictionary<int, string> owned = OwnedBindings(oldDescriptors);

        // The old dictionary and page table are reclaimed: unchanged payloads stay in place and new allocations reuse the
        // freed space before the sections grow, so repeated edits do not accumulate orphaned copies.
        var (reclaimedSystem, reclaimedGraphics) = Reclaimable();
        var freeSystem = new FreeSpace(reclaimedSystem);
        var freeGraphics = new FreeSpace(reclaimedGraphics);
        byte[] system = System.ToArray(), graphics = Graphics.ToArray();
        freeSystem.Clear(system);
        freeGraphics.Clear(graphics);

        var pointers = new Dictionary<string, ulong>(StringComparer.Ordinal);
        ulong dictionaryPointer = 0;
        if (!dictionary.Textures.IsEmpty)
        {
            PreparedResourceSections prepared = dictionary.ToYtd().PrepareSections();
            Texture[] ordered = [.. dictionary.Textures.OrderBy(texture => Jenkins.Hash(texture.Name ?? ""))];
            int[] oldPayloads = [.. oldDescriptors.Values.Select(descriptor => (int)(U64(System, descriptor + 0x70) - GraphicsBase))];
            dictionaryPointer = Place(prepared, ref system, ref graphics, Graphics, freeSystem, freeGraphics, oldPayloads);
            int root = Offset(system, dictionaryPointer, 0x40);
            Dictionary<string, int> newDescriptors = Descriptors(system, dictionaryPointer);
            VerifyPayloads(system, graphics, newDescriptors, ordered);
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
                    system.AsSpan(destination + offset, size).CopyTo(preserved.AsSpan(offset));
                preserved.CopyTo(system, destination);
            }
            if (oldPointer != 0)
            {
                int oldDictionary = Offset(System, oldPointer, 0x40);
                foreach ((int start, int end) in new[] { (0, 8), (0x10, 0x20), (0x2C, 0x30), (0x3C, 0x40) })
                    System.AsSpan(oldDictionary + start, end - start).CopyTo(system.AsSpan(root + start));
            }
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
                // Removed textures become named external references, never dangling pointers into a reclaimed
                // embedded texture descriptor.
                if (!external.TryGetValue(name, out ulong reference))
                {
                    byte[] nameBytes = Utf8.GetBytes(name);
                    int size = 0x50 + nameBytes.Length + 1;
                    int descriptor = Allocate(freeSystem, ref system, size, 16);
                    system.AsSpan(descriptor, size).Clear();
                    nameBytes.CopyTo(system, descriptor + 0x50);
                    BinaryPrimitives.WriteUInt32LittleEndian(system.AsSpan(descriptor), TextureBaseVft);
                    BinaryPrimitives.WriteUInt32LittleEndian(system.AsSpan(descriptor + 4), 1);
                    BinaryPrimitives.WriteUInt64LittleEndian(system.AsSpan(descriptor + 0x28), SystemBase + (ulong)(descriptor + 0x50));
                    BinaryPrimitives.WriteUInt16LittleEndian(system.AsSpan(descriptor + 0x30), 1);
                    BinaryPrimitives.WriteUInt16LittleEndian(system.AsSpan(descriptor + 0x32), 2);
                    external[name] = reference = SystemBase + (ulong)descriptor;
                }
                pointer = reference;
            }
            patches[field] = pointer.Value;
        }
        int pages = Allocate(freeSystem, ref system, 32, 16); // Exactly two pages: system and graphics.
        system.AsSpan(pages, 32).Clear();
        system[pages + 8] = 1;
        Array.Resize(ref system, freeSystem.Trim(system.Length));
        Array.Resize(ref graphics, freeGraphics.Trim(graphics.Length));
        system[pages + 9] = (byte)(graphics.Length > 0 ? 1 : 0);
        patches[8] = SystemBase + (ulong)pages;
        foreach ((int field, ulong pointer) in patches)
            WriteU64(system, field, pointer);
        if (Math.Max(system.Length, graphics.Length) > MaxSection)
            throw new InvalidDataException("Edited YDR exceeds the preservation editor's 128 MiB section limit");
        var resourceHeader = new ResourceHeader(165, SinglePageFlags(system.Length, 10), SinglePageFlags(graphics.Length, 5));
        if (!resourceHeader.TryResolveChunk(SystemBase, system.Length, out _)
            || graphics.Length > 0 && !resourceHeader.TryResolveChunk(GraphicsBase, graphics.Length, out _))
            throw new InvalidDataException("YDR sections are not contained in individual resource pages");
        byte[] expected = System.ToArray();
        foreach ((int field, ulong pointer) in patches)
            WriteU64(expected, field, pointer);
        if (!SameOutside(expected, system, reclaimedSystem) || !SameOutside(Graphics, graphics, reclaimedGraphics))
            throw new InvalidDataException("YDR preservation verification failed");
        byte[] result = ResourceFile.Create(165, system, graphics, resourceHeader.SystemFlags, resourceHeader.GraphicsFlags).Write();

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

    /// <summary>System and graphics ranges a save may overwrite: the embedded dictionary (its structure, hash and pointer
    /// arrays, descriptors, names and pixel payloads), the page table and the page's trailing zero padding. A range stays
    /// out when anything else in the system section points into it, so only space nothing else refers to is reclaimed.</summary>
    internal (List<(int Start, int End)> System, List<(int Start, int End)> Graphics) Reclaimable()
    {
        var system = new List<(int Start, int End)>();
        var graphics = new List<(int Start, int End)>();
        ulong pageTable = U64(System, 8);
        if (pageTable != 0)
        {
            int table = Offset(System, pageTable, 16);
            int size = 16 + 8 * (System[table + 8] + System[table + 9]);
            Offset(System, pageTable, size);
            system.Add((table, table + size));
        }
        ulong dictionaryPointer = U64(System, Group + 8);
        Dictionary<string, int> descriptors = Descriptors(System, dictionaryPointer);
        if (dictionaryPointer != 0)
        {
            int dictionary = Offset(System, dictionaryPointer, 0x40);
            system.Add((dictionary, dictionary + 0x40));
            int count = descriptors.Count;
            if (count > 0)
            {
                int hashes = Offset(System, U64(System, dictionary + 0x20), count * 4);
                int array = Offset(System, U64(System, dictionary + 0x30), count * 8);
                system.Add((hashes, hashes + count * 4));
                system.Add((array, array + count * 8));
                var payloadSizes = Dictionary.Textures.ToDictionary(texture => texture.Name ?? "", texture => texture.Data.Length, StringComparer.Ordinal);
                foreach ((string name, int descriptor) in descriptors)
                {
                    system.Add((descriptor, descriptor + DescriptorSize));
                    int text = Offset(System, U64(System, descriptor + 0x28), 1);
                    system.Add((text, text + Utf8.GetByteCount(name) + 1));
                    int payload = (int)(U64(System, descriptor + 0x70) - GraphicsBase);
                    graphics.Add((payload, payload + payloadSizes[name]));
                }
            }
        }

        // Pointers between the candidate allocations themselves are fine; a pointer from anywhere else keeps its
        // target. Repeat until stable, since a kept allocation's own pointers then keep their targets too.
        var ignored = new HashSet<int>(OwnedBindings(descriptors).Keys) { 8, Group + 8 };
        for (bool changed = true; changed;)
        {
            changed = false;
            var covered = new FreeSpace(system);
            for (int offset = 0; offset + 8 <= System.Length; offset += 8)
            {
                if (ignored.Contains(offset) || covered.Contains(offset))
                    continue;
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(System.AsSpan(offset));
                if (value >= SystemBase && value < SystemBase + (ulong)System.Length)
                {
                    int target = (int)(value - SystemBase);
                    changed |= system.RemoveAll(range => range.Start <= target && target < range.End) > 0;
                }
                else if (value >= GraphicsBase && value < GraphicsBase + (ulong)Graphics.Length)
                {
                    int target = (int)(value - GraphicsBase);
                    graphics.RemoveAll(range => range.Start <= target && target < range.End);
                }
            }
        }
        // The zero padding after the last used byte fills out the source page; it is free too unless a pointer targets it.
        AddPadding(System, SystemBase, system, ignored);
        AddPadding(Graphics, GraphicsBase, graphics, ignored);
        return ([.. new FreeSpace(system).Ranges], [.. new FreeSpace(graphics).Ranges]);
    }

    private void AddPadding(byte[] section, ulong sectionBase, List<(int Start, int End)> ranges, HashSet<int> ignored)
    {
        int start = Align(Array.FindLastIndex(section, value => value != 0) + 1, 16);
        if (start >= section.Length)
            return;
        for (int offset = 0; offset + 8 <= System.Length; offset += 8)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(System.AsSpan(offset));
            if (!ignored.Contains(offset) && value >= sectionBase + (ulong)start && value < sectionBase + (ulong)section.Length)
                return;
        }
        ranges.Add((start, section.Length));
    }

    /// <summary>Material texture parameter fields that point at an embedded descriptor of this dictionary.</summary>
    private Dictionary<int, string> OwnedBindings(Dictionary<string, int> descriptors)
    {
        // Descriptor identity distinguishes actual embedded references from external references with the same name.
        var owned = new Dictionary<int, string>();
        foreach ((int field, string name) in Bindings)
            if (descriptors.TryGetValue(name, out int descriptor) && U64(System, field) == SystemBase + (ulong)descriptor)
                owned[field] = name;
        return owned;
    }

    /// <summary>Lays the prepared YTD's blocks into the sections: payloads identical to an old one keep its place, other
    /// blocks take reclaimed space first and are appended only when nothing fits. Returns the dictionary pointer.</summary>
    private static ulong Place(PreparedResourceSections prepared, ref byte[] system, ref byte[] graphics,
        byte[] originalGraphics, FreeSpace freeSystem, FreeSpace freeGraphics, int[] oldPayloads)
    {
        byte[] sourceSystem = prepared.SystemData.ToArray(), sourceGraphics = prepared.GraphicsData.ToArray();
        ulong pageTable = U64(sourceSystem, 8);
        // Embedded dictionaries do not own a separate resource page table.
        WriteU64(sourceSystem, 8, 0);
        ResourceBlock[] systemBlocks = [.. prepared.SystemBlocks
            .Where(block => pageTable == 0 || block.Offset != (int)(pageTable - SystemBase)).OrderBy(block => block.Offset)];
        ResourceBlock[] graphicsBlocks = [.. prepared.GraphicsBlocks.OrderBy(block => block.Offset)];
        int[] systemAt = new int[systemBlocks.Length], graphicsAt = new int[graphicsBlocks.Length];
        var pending = new List<int>();
        for (int index = 0; index < graphicsBlocks.Length; index++)
        {
            ResourceBlock block = graphicsBlocks[index];
            int kept = -1;
            foreach (int start in oldPayloads)
            {
                if (start % block.Alignment == 0 && start + block.Size <= originalGraphics.Length
                    && originalGraphics.AsSpan(start, block.Size).SequenceEqual(sourceGraphics.AsSpan(block.Offset, block.Size))
                    && freeGraphics.TryTake(start, block.Size))
                {
                    kept = start;
                    break;
                }
            }
            if (kept >= 0)
                graphicsAt[index] = kept;
            else
                pending.Add(index);
        }
        foreach (int index in pending)
            graphicsAt[index] = Allocate(freeGraphics, ref graphics, graphicsBlocks[index].Size, graphicsBlocks[index].Alignment);
        for (int index = 0; index < systemBlocks.Length; index++)
            systemAt[index] = Allocate(freeSystem, ref system, systemBlocks[index].Size, systemBlocks[index].Alignment);

        ulong Relocate(ulong pointer)
        {
            bool inGraphics = pointer >= GraphicsBase;
            ulong sectionBase = inGraphics ? GraphicsBase : SystemBase;
            long target = (long)(pointer - sectionBase);
            ResourceBlock[] blocks = inGraphics ? graphicsBlocks : systemBlocks;
            int[] at = inGraphics ? graphicsAt : systemAt;
            for (int index = 0; index < blocks.Length; index++)
                if (target >= blocks[index].Offset && target < blocks[index].Offset + blocks[index].Size)
                    return sectionBase + (ulong)(at[index] + target - blocks[index].Offset);
            throw new InvalidDataException("Prepared texture dictionary pointer does not target one of its blocks");
        }

        void Copy(ResourceBlock[] blocks, int[] at, byte[] source, byte[] destination)
        {
            for (int index = 0; index < blocks.Length; index++)
            {
                ResourceBlock block = blocks[index];
                source.AsSpan(block.Offset, block.Size).CopyTo(destination.AsSpan(at[index]));
                foreach (int pointerOffset in block.PointerOffsets)
                {
                    ulong pointer = U64(source, block.Offset + pointerOffset);
                    if (pointer != 0)
                        WriteU64(destination, at[index] + pointerOffset, Relocate(pointer));
                }
            }
        }

        Copy(graphicsBlocks, graphicsAt, sourceGraphics, graphics);
        Copy(systemBlocks, systemAt, sourceSystem, system);
        int root = Array.FindIndex(systemBlocks, block => block.Offset == 0);
        if (root < 0)
            throw new InvalidDataException("Prepared texture dictionary has no root block");
        return SystemBase + (ulong)systemAt[root];
    }

    /// <summary>Reclaimed space when a range fits, otherwise a new aligned allocation at the end of the section.</summary>
    private static int Allocate(FreeSpace free, ref byte[] data, int size, int alignment)
    {
        if (free.TryAllocate(size, alignment, out int offset))
            return offset;
        offset = Align(data.Length, alignment);
        Array.Resize(ref data, offset + size);
        return offset;
    }

    private static int Align(int value, int alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    /// <summary>Whether every byte of <paramref name="original"/> outside <paramref name="reclaimed"/> is unchanged in
    /// <paramref name="result"/>.</summary>
    private static bool SameOutside(byte[] original, byte[] result, IReadOnlyList<(int Start, int End)> reclaimed)
    {
        int position = 0;
        foreach ((int start, int end) in reclaimed.Append((original.Length, original.Length)))
        {
            int stop = Math.Min(start, original.Length);
            if (stop > position && (stop > result.Length
                || !original.AsSpan(position, stop - position).SequenceEqual(result.AsSpan(position, stop - position))))
                return false;
            position = Math.Max(position, end);
        }
        return true;
    }

    /// <summary>Sorted, merged free byte ranges of one section.</summary>
    private sealed class FreeSpace
    {
        private readonly List<(int Start, int End)> ranges = [];

        public FreeSpace(IEnumerable<(int Start, int End)> source)
        {
            foreach ((int start, int end) in source.Where(range => range.End > range.Start).OrderBy(range => range.Start))
            {
                if (ranges.Count > 0 && start <= ranges[^1].End)
                    ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
                else
                    ranges.Add((start, end));
            }
        }

        public IReadOnlyList<(int Start, int End)> Ranges => ranges;

        public bool Contains(int offset)
        {
            int low = 0, high = ranges.Count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (offset < ranges[middle].Start)
                    high = middle - 1;
                else if (offset >= ranges[middle].End)
                    low = middle + 1;
                else
                    return true;
            }
            return false;
        }

        public void Clear(byte[] data)
        {
            foreach ((int start, int end) in ranges)
                data.AsSpan(start, end - start).Clear();
        }

        public bool TryTake(int start, int size)
        {
            for (int index = 0; index < ranges.Count; index++)
            {
                if (start >= ranges[index].Start && start + size <= ranges[index].End)
                {
                    Split(index, start, start + size);
                    return true;
                }
            }
            return false;
        }

        public bool TryAllocate(int size, int alignment, out int offset)
        {
            for (int index = 0; index < ranges.Count; index++)
            {
                int start = Align(ranges[index].Start, alignment);
                if (start + size <= ranges[index].End)
                {
                    Split(index, start, start + size);
                    offset = start;
                    return true;
                }
            }
            offset = 0;
            return false;
        }

        /// <summary>The section length without the free space at its end.</summary>
        public int Trim(int length)
        {
            while (ranges.Count > 0 && ranges[^1].End >= length && ranges[^1].Start < length)
            {
                length = ranges[^1].Start;
                ranges.RemoveAt(ranges.Count - 1);
            }
            return length;
        }

        private void Split(int index, int start, int end)
        {
            var (rangeStart, rangeEnd) = ranges[index];
            ranges.RemoveAt(index);
            if (end < rangeEnd)
                ranges.Insert(index, (end, rangeEnd));
            if (rangeStart < start)
                ranges.Insert(index, (rangeStart, start));
        }
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
