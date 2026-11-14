using CommonFury.Textures;
using FiveFury.Textures;
using FiveFury.Ytd;

namespace HikariHopper.Rendering;

/// <summary>
/// A texture dictionary near a model, read only if a texture is still missing after the ones named for it. With a
/// <paramref name="Key"/> naming what it holds (see <see cref="KeyFor"/>), it is parsed once and kept
/// (<see cref="ParsedDictionaries"/>) for every model that reads it after.
/// </summary>
internal sealed record NearbyDictionary(string Name, long Size, Func<byte[]> Read, string? Key = null)
{
    /// <summary>
    /// What names a dictionary's contents: the file it lives in (a root RPF, or the dictionary itself when loose) with
    /// that file's size and write time, and its path inside it; none when the file isn't there.
    /// </summary>
    public static string? KeyFor(string file, string inside)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists ? $"{info.FullName}|{inside}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The key of a game's file by its path from the game folder, RPFs as folders (<see cref="KeyFor"/>).</summary>
    public static string? KeyForGameFile(string gameRoot, string path)
    {
        string[] parts = path.Split('/');
        int first = Array.FindIndex(parts, part => part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
        return first < 0 || first == parts.Length - 1
            ? KeyFor(Path.Combine([gameRoot, .. parts]), "")
            : KeyFor(Path.Combine([gameRoot, .. parts[..(first + 1)]]), string.Join('/', parts[(first + 1)..]));
    }
}

/// <summary>
/// Where a model's textures come from when it doesn't embed them (after RedFury's ArchiveTextureLookup): the
/// dictionaries named for it first, in order, and only when a texture is still missing, the game's dictionaries for it,
/// read one at a time until it turns up, then the dictionaries the game's texture directory says hold one of its name,
/// nearest first, and last, when there's no directory to ask, the other dictionaries beside it, up to
/// <see cref="SearchLimit"/> of each. A vehicle's interior, say, is in its parent dictionary, which sits beside it under
/// a name of its own, and a vehicle mod part's shared textures in the base game's.
/// </summary>
internal sealed class TextureLookup
{
    /// <summary>How much of the dictionaries of each kind is read looking for a model's missing textures.</summary>
    public const long SearchLimit = 512L * 1024 * 1024;

    private readonly Dictionary<string, Texture> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture> overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Search nearby, beside;
    private readonly Func<string, IReadOnlyList<NearbyDictionary>?>? elsewhere;
    private readonly HashSet<string> read = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> diagnostics = [];
    private long searchedElsewhere;

    /// <summary>No dictionaries: a model's own textures only.</summary>
    public static TextureLookup None { get; } = new([], []);

    /// <param name="named">The dictionaries named for the model, read already, in the order they're looked in.</param>
    /// <param name="nearby">The game's dictionaries for it, searched in this order when a texture is still missing.</param>
    /// <param name="overrides">
    /// Textures that take the place of those of their names, even the model's own: a fragment's embedded textures as
    /// edited and saved in the texture editor, which can't write them back into it.
    /// </param>
    /// <param name="elsewhere">
    /// The dictionaries that hold a texture of a name, nearest the model first, from the game's texture directory: none
    /// when the game has none, null when it can't say (it isn't ready, or the model isn't in the game's RPFs).
    /// </param>
    /// <param name="beside">The other dictionaries beside the model, searched in this order when the directory can't say.</param>
    public TextureLookup(IReadOnlyList<TextureDictionary> named, IReadOnlyList<NearbyDictionary> nearby, IReadOnlyList<Texture>? overrides = null,
        Func<string, IReadOnlyList<NearbyDictionary>?>? elsewhere = null, IReadOnlyList<NearbyDictionary>? beside = null)
    {
        foreach (TextureDictionary dictionary in named)
            Add(dictionary);
        this.nearby = new Search(nearby);
        this.beside = new Search(beside ?? []);
        this.elsewhere = elsewhere;
        foreach (Texture texture in overrides ?? [])
            if (texture.Name is { } name)
                this.overrides.TryAdd(name, texture);
    }

    /// <summary>The texture taking the place of this name's, even a model's own, if there is one.</summary>
    public Texture? Override(string name) => overrides.GetValueOrDefault(name);

    /// <summary>What the search ran into: dictionaries that couldn't be read, or the limit.</summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>The texture of this name, searching the game's dictionaries for it if it has to; null if none has it.</summary>
    public Texture? Find(string name)
    {
        if (textures.TryGetValue(name, out Texture? texture) || (texture = Scan(nearby, name, "the game's dictionaries for it")) is not null)
            return texture;
        if (elsewhere?.Invoke(name) is { } holding)
        {
            bool any = holding.Count == 0;
            foreach (NearbyDictionary dictionary in holding)
            {
                // One read already holds no texture of this name: its hash is another's.
                if (read.Contains(dictionary.Key ?? dictionary.Name))
                    continue;
                if (searchedElsewhere + dictionary.Size > SearchLimit)
                {
                    diagnostics.Add($"Stopped reading the dictionaries the game's texture directory names after {SearchLimit / (1024 * 1024)} MB.");
                    break;
                }
                searchedElsewhere += dictionary.Size;
                if (!Read(dictionary))
                    continue;
                any = true;
                if (textures.TryGetValue(name, out texture))
                    return texture;
            }
            // The directory knows every dictionary of the game: the ones beside it hold none either, unless none it
            // named could be read.
            if (any)
                return null;
        }
        return Scan(beside, name, "the dictionaries beside it");
    }

    // Reads the dictionaries of a search one at a time, from where it got to, until one holds the texture.
    private Texture? Scan(Search search, string name, string what)
    {
        while (search.Next < search.Dictionaries.Count)
        {
            NearbyDictionary dictionary = search.Dictionaries[search.Next++];
            if (search.Searched + dictionary.Size > SearchLimit)
            {
                diagnostics.Add($"Stopped looking for missing textures after {SearchLimit / (1024 * 1024)} MB of {what}.");
                search.Next = search.Dictionaries.Count;
                break;
            }
            search.Searched += dictionary.Size;
            if (Read(dictionary) && textures.TryGetValue(name, out Texture? texture))
                return texture;
        }
        return null;
    }

    private bool Read(NearbyDictionary dictionary)
    {
        read.Add(dictionary.Key ?? dictionary.Name);
        try
        {
            // Parsed once for every model that reads it, when it says what it holds.
            Add(dictionary.Key is { } key ? ParsedDictionaries.Get(key, () => Ytd.Read(dictionary.Read()).Dictionary)
                : Ytd.Read(dictionary.Read()).Dictionary);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException
            or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"{dictionary.Name} couldn't be read: {error.Message}");
            return false;
        }
    }

    private void Add(TextureDictionary dictionary)
    {
        foreach (Texture texture in dictionary.Textures)
            if (texture.Name is { } name)
                textures.TryAdd(name, texture);
    }

    private sealed class Search(IReadOnlyList<NearbyDictionary> dictionaries)
    {
        public IReadOnlyList<NearbyDictionary> Dictionaries { get; } = dictionaries;
        public int Next { get; set; }
        public long Searched { get; set; }
    }
}
