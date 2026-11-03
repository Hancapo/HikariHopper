using CommonFury;
using System.Collections.Immutable;
using FiveFury;
using FiveFury.Textures;
using FiveFury.Ytd;

namespace HikariHopper.Textures;

/// <summary>An immutable snapshot of an edited dictionary: its textures, in document order, and its edition.</summary>
/// <remarks>The editor never mutates a <see cref="Texture"/> after placing it in a set, so snapshots can be shared by
/// the document, the undo history and background tasks. Constructing a set does not validate it.</remarks>
public sealed class TextureSet(IEnumerable<Texture> textures, GameTarget target)
{
    public ImmutableArray<Texture> Textures { get; } = [.. textures];
    public GameTarget Target { get; } = target;

    /// <summary>The names in document order (Python <c>Ytd.names()</c>).</summary>
    public IReadOnlyList<string> Names => [.. Textures.Select(texture => texture.Name ?? "")];

    /// <summary>A new FiveFury dictionary sharing these texture objects.</summary>
    public TextureDictionary ToDictionary()
    {
        var dictionary = new TextureDictionary();
        dictionary.Textures.AddRange(Textures);
        return dictionary;
    }

    public Ytd ToYtd() => new(ToDictionary(), Target);

    /// <summary>Export validation for this edition; an empty dictionary is valid.</summary>
    public ValidationReport Validate() => ToYtd().Validate();

    /// <summary>Textures whose lower-cased name repeats an earlier one.</summary>
    public int DuplicateCount => Textures.Length - Textures.Select(texture => Lower(texture.Name)).Distinct().Count();

    /// <summary>Keeps the first texture of each lower-cased name, retaining object identity and order.</summary>
    public TextureSet WithoutDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new(Textures.Where(texture => seen.Add(Lower(texture.Name))), Target);
    }

    internal static string Lower(string? name) => (name ?? "").ToLowerInvariant();
}
