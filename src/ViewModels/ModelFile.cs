namespace HikariHopper.ViewModels;

/// <summary>
/// A collision, navmesh, drawable or fragment file opened for the 3D viewer: its name and its bytes, and for a drawable
/// the texture dictionaries found beside it.
/// </summary>
public sealed record ModelFile(string Name, byte[] Data)
{
    /// <summary>The largest file the viewer reads.</summary>
    public const long MaxSize = 256L * 1024 * 1024;

    /// <summary>The .ytd files named for a drawable (<see cref="DictionaryNames"/>), read, in that order.</summary>
    public IReadOnlyList<byte[]> Dictionaries { get; init; } = [];

    /// <summary>
    /// The game's dictionaries for a drawable, as the game finds them: those its archetypes name and their parents
    /// (gtxd), searched after <see cref="Dictionaries"/> and before <see cref="Nearby"/>. Null outside a game; until the
    /// game's tables are ready, it waits for them.
    /// </summary>
    internal Task<IReadOnlyList<Rendering.NearbyDictionary>>? GameDictionaries { get; init; }

    /// <summary>
    /// Opens the file's embedded textures in the texture viewer, or none for a file that can't; the action given is
    /// told what was saved, once the editor closes after saving.
    /// </summary>
    internal Action<Action<SavedDocument>>? OpenTextures { get; init; }

    /// <summary>
    /// The other .ytd files beside a drawable, read only if a texture is still missing: a vehicle's parent dictionary
    /// (its interior's, say) is among them under a name of its own.
    /// </summary>
    internal IReadOnlyList<Rendering.NearbyDictionary> Nearby { get; init; } = [];

    /// <summary>Whether a file of this name is a drawable, a drawable dictionary or a fragment, which have textures.</summary>
    public static bool IsDrawableName(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".ydr" or ".ydd" or ".yft";

    /// <summary>
    /// The texture dictionaries that usually hold a drawable's textures when it doesn't embed them, in the order they're
    /// looked in: the one sharing its name; for a vehicle's high-detail model (adder_hi.yft), its own (adder+hi.ytd)
    /// and then the vehicle's; and for any fragment, the textures vehicles share (vehshare.ytd).
    /// </summary>
    public static IReadOnlyList<string> DictionaryNames(string name)
    {
        if (!IsDrawableName(name))
            return [];
        string stem = Path.GetFileNameWithoutExtension(name);
        var names = new List<string>();
        bool fragment = name.EndsWith(".yft", StringComparison.OrdinalIgnoreCase);
        if (fragment && stem.EndsWith("_hi", StringComparison.OrdinalIgnoreCase))
        {
            string vehicle = stem[..^3];
            names.Add(vehicle + "+hi.ytd");
            names.Add(vehicle + ".ytd");
        }
        else
            names.Add(stem + ".ytd");
        if (fragment)
            names.Add("vehshare.ytd");
        return names;
    }
}
