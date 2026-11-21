using FiveFury.Textures;

namespace HikariHopper.Rendering;

/// <summary>
/// The texture dictionaries the viewers have read, kept parsed in memory by what they hold (<see cref="NearbyDictionary.Key"/>:
/// where they are and the RPF's size and time), so the dictionaries many models share (mapdetail.ytd, vehshare.ytd,
/// the gtxd parents) are parsed once, not again for every model. Kept to <see cref="Budget"/>, the least recently used
/// let go of first.
/// </summary>
/// <remarks>
/// What is kept is shared by every model that reads it, so it is only ever read: the viewers draw from its textures,
/// and anything that edits textures reads its own copy of the dictionary.
/// </remarks>
internal static class ParsedDictionaries
{
    /// <summary>How many bytes of texture data are kept at most; the one dictionary last read is kept even if it is more.</summary>
    public static long Budget { get; set; } = 512L * 1024 * 1024;

    private sealed class Entry(string key, Lazy<TextureDictionary> parsed)
    {
        public string Key { get; } = key;
        public Lazy<TextureDictionary> Parsed { get; } = parsed;
        // Counted once parsed; nothing until then.
        public long Bytes { get; set; }
    }

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> ByKey = new(StringComparer.OrdinalIgnoreCase);
    // Most recently used first.
    private static readonly LinkedList<Entry> Order = new();
    private static long held;

    /// <summary>
    /// The dictionary kept under <paramref name="key"/>, or <paramref name="parse"/>'s, kept from now on; two asking at
    /// once for the same one parse it once. A parse that fails keeps nothing.
    /// </summary>
    public static TextureDictionary Get(string key, Func<TextureDictionary> parse)
    {
        LinkedListNode<Entry> node;
        lock (Gate)
        {
            if (ByKey.TryGetValue(key, out LinkedListNode<Entry>? known))
            {
                node = known;
                Order.Remove(node);
                Order.AddFirst(node);
            }
            else
            {
                node = Order.AddFirst(new Entry(key, new Lazy<TextureDictionary>(parse, LazyThreadSafetyMode.ExecutionAndPublication)));
                ByKey[key] = node;
            }
        }
        TextureDictionary dictionary;
        try
        {
            dictionary = node.Value.Parsed.Value;
        }
        catch
        {
            lock (Gate)
                Remove(node);
            throw;
        }
        lock (Gate)
        {
            if (node.Value.Bytes == 0 && node.List is not null)
            {
                node.Value.Bytes = Math.Max(1, dictionary.Textures.Sum(texture => (long)texture.Data.Length));
                held += node.Value.Bytes;
            }
            Trim();
        }
        return dictionary;
    }

    // Lets go of the least recently used until what is kept fits; one being parsed now is left to its reader.
    private static void Trim()
    {
        LinkedListNode<Entry>? last = Order.Last;
        while (held > Budget && last is not null && last != Order.First)
        {
            LinkedListNode<Entry>? before = last.Previous;
            if (last.Value.Bytes > 0)
                Remove(last);
            last = before;
        }
    }

    private static void Remove(LinkedListNode<Entry> node)
    {
        if (node.List is null)
            return;
        Order.Remove(node);
        if (ByKey.TryGetValue(node.Value.Key, out LinkedListNode<Entry>? kept) && kept == node)
            ByKey.Remove(node.Value.Key);
        held -= node.Value.Bytes;
    }

    /// <summary>How many dictionaries are kept, and how many bytes of texture data; for tests.</summary>
    internal static (int Count, long Bytes) Held
    {
        get
        {
            lock (Gate)
                return (Order.Count, held);
        }
    }

    /// <summary>Lets go of every dictionary kept; for tests.</summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            Order.Clear();
            ByKey.Clear();
            held = 0;
        }
    }
}
