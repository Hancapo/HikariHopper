using FiveFury.Textures;
using FiveFury.Ytd;

namespace HikariHopper.Rendering;

/// <summary>A texture dictionary near a model, read only if a texture is still missing after the ones named for it.</summary>
internal sealed record NearbyDictionary(string Name, long Size, Func<byte[]> Read);

/// <summary>
/// Where a model's textures come from when it doesn't embed them (after RedFury's ArchiveTextureLookup): the
/// dictionaries named for it first, in order, and only when a texture is still missing, the other dictionaries beside
/// it, read one at a time until it turns up, up to <see cref="SearchLimit"/>. A vehicle's interior, say, is in its
/// parent dictionary, which sits beside it under a name of its own.
/// </summary>
internal sealed class TextureLookup
{
    /// <summary>How much of the dictionaries beside a model is read looking for its missing textures.</summary>
    public const long SearchLimit = 512L * 1024 * 1024;

    private readonly Dictionary<string, Texture> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture> overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<NearbyDictionary> nearby;
    private readonly List<string> diagnostics = [];
    private int next;
    private long searched;

    /// <summary>No dictionaries: a model's own textures only.</summary>
    public static TextureLookup None { get; } = new([], []);

    /// <param name="named">The dictionaries named for the model, read already, in the order they're looked in.</param>
    /// <param name="nearby">The other dictionaries beside it, searched in this order when a texture is still missing.</param>
    /// <param name="overrides">
    /// Textures that take the place of those of their names, even the model's own: a fragment's embedded textures as
    /// edited and saved in the texture editor, which can't write them back into it.
    /// </param>
    public TextureLookup(IReadOnlyList<TextureDictionary> named, IReadOnlyList<NearbyDictionary> nearby, IReadOnlyList<Texture>? overrides = null)
    {
        foreach (TextureDictionary dictionary in named)
            Add(dictionary);
        this.nearby = nearby;
        foreach (Texture texture in overrides ?? [])
            if (texture.Name is { } name)
                this.overrides.TryAdd(name, texture);
    }

    /// <summary>The texture taking the place of this name's, even a model's own, if there is one.</summary>
    public Texture? Override(string name) => overrides.GetValueOrDefault(name);

    /// <summary>What the search ran into: dictionaries that couldn't be read, or the limit.</summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>The texture of this name, searching the dictionaries beside the model for it if it has to; null if none has it.</summary>
    public Texture? Find(string name)
    {
        if (textures.TryGetValue(name, out Texture? texture))
            return texture;
        while (next < nearby.Count)
        {
            NearbyDictionary dictionary = nearby[next++];
            if (searched + dictionary.Size > SearchLimit)
            {
                diagnostics.Add($"Stopped looking for missing textures after {SearchLimit / (1024 * 1024)} MB of the dictionaries beside it.");
                next = nearby.Count;
                break;
            }
            searched += dictionary.Size;
            try
            {
                Add(Ytd.Read(dictionary.Read()).Dictionary);
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException
                or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"{dictionary.Name} couldn't be read: {error.Message}");
                continue;
            }
            if (textures.TryGetValue(name, out texture))
                return texture;
        }
        return null;
    }

    private void Add(TextureDictionary dictionary)
    {
        foreach (Texture texture in dictionary.Textures)
            if (texture.Name is { } name)
                textures.TryAdd(name, texture);
    }
}
