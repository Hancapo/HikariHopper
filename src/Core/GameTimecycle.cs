using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Xml;
using CommonFury.Hashing;

namespace HikariHopper.Core;

/// <summary>
/// A timecycle box as one of GTA V's maps places it (a map's timeCycleModifiers): the modifier it applies, by its name's
/// hash, its corners in the world, its strength in percent, the range it fades out over around it, and the hours it's in
/// force (none where the start and end are the same).
/// </summary>
internal readonly record struct TimecycleBox(uint Name, Vector3 Min, Vector3 Max, float Percentage, float Range, int StartHour, int EndHour);

/// <summary>
/// One of GTA V's timecycle modifiers, as its file has it: the two values of each field it sets (what the field blends
/// to, and for some fields a multiplier or the top of a range: the game's variable table says which), its user flags,
/// and where it was read among all of them, which is the order the game applies them in.
/// </summary>
internal sealed record GameTimecycleModifier(int Order, uint Flags, Dictionary<string, (float A, float B)> Fields)
{
    /// <summary>The modifier applies only with the camera under water (the game's first user flag).</summary>
    public const uint UnderwaterOnly = 1;

    /// <summary>The modifier applies only inside an interior (its second).</summary>
    public const uint InteriorOnly = 2;

    /// <summary>The first value of a field it sets.</summary>
    public bool TryGetValue(string name, out float value)
    {
        bool found = Fields.TryGetValue(name, out var values);
        value = values.A;
        return found;
    }
}

/// <summary>
/// GTA V's timecycle, as the game has it in its archives: clear weather's (w_extrasunny.xml: each of its regions, GLOBAL
/// first, each field at the timecycle's thirteen keyframes) and the modifiers areas and interior rooms apply over it
/// (timecycle_mods_1.xml to _4), the update's copies over the base game's. Read once per game folder, in the
/// background, a second or so.
/// </summary>
internal sealed class GameTimecycle
{
    private static readonly ConcurrentDictionary<string, Lazy<GameTimecycle?>> Shared = new(StringComparer.OrdinalIgnoreCase);

    private GameTimecycle(IReadOnlyList<(string Name, Dictionary<string, float[]> Fields)> regions, Dictionary<uint, GameTimecycleModifier> modifiers) =>
        (Regions, Modifiers) = (regions, modifiers);

    /// <summary>Clear weather's GLOBAL region: its fields, each at the keyframes.</summary>
    public IReadOnlyDictionary<string, float[]> Weather => Regions[0].Fields;

    /// <summary>
    /// Clear weather's regions in the order its file has them, GLOBAL first: the game takes them by their place (its
    /// region boxes blend the second, URBAN, over the first).
    /// </summary>
    public IReadOnlyList<(string Name, Dictionary<string, float[]> Fields)> Regions { get; }

    /// <summary>The modifiers, by their name's hash.</summary>
    public IReadOnlyDictionary<uint, GameTimecycleModifier> Modifiers { get; }

    // Where the files are: in the update, then in the base game.
    private static readonly (string Archive, string Folder)[] Places = [("update/update.rpf", "common/data/timecycle/"), ("common.rpf", "data/timecycle/")];

    /// <summary>The timecycle of the GTA V game <paramref name="game"/>, shared; null when its files can't be read.</summary>
    public static GameTimecycle? ForGame(LoadedGame game) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Root)), _ => new Lazy<GameTimecycle?>(() => Read(game))).Value;

    /// <summary>A timecycle of this GLOBAL region (and these other regions) and these modifiers; for tests.</summary>
    internal static GameTimecycle From(Dictionary<string, float[]> weather, Dictionary<uint, GameTimecycleModifier> modifiers,
        params (string Name, Dictionary<string, float[]> Fields)[] regions) =>
        new([("GLOBAL", weather), .. regions], modifiers);

    /// <summary>A timecycle of a weather file's regions and these modifiers; for tests.</summary>
    internal static GameTimecycle From(string weather, Dictionary<uint, GameTimecycleModifier> modifiers) => new(ParseRegions(weather), modifiers);

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    private static GameTimecycle? Read(LoadedGame game)
    {
        try
        {
            string? weather = ReadText(game, "w_extrasunny.xml");
            if (weather is null)
                return null;
            var modifiers = new Dictionary<uint, GameTimecycleModifier>();
            int order = 0;
            for (int i = 1; i <= 4; i++)
                if (ReadText(game, $"timecycle_mods_{i}.xml") is { } text)
                    foreach (var (name, flags, fields) in ParseModifiers(text))
                        modifiers[new Hash32(name.ToLowerInvariant()).Value] = new GameTimecycleModifier(order++, flags, fields);
            return new GameTimecycle(ParseRegions(weather), modifiers);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or EndOfStreamException)
        {
            return null;
        }
    }

    // A timecycle file: the update's, else the base game's; null when neither has it.
    private static string? ReadText(LoadedGame game, string name) =>
        GameFiles.ReadText(game, Places.Select(place => (place.Archive, place.Folder + name)));

    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ','];

    /// <summary>A weather file's GLOBAL region: each field's values at the keyframes.</summary>
    internal static Dictionary<string, float[]> ParseWeather(string xml) => ParseRegions(xml)[0].Fields;

    /// <summary>
    /// A weather file's regions, in its order, the first being GLOBAL whatever it's called (as the game loads them, by
    /// their place): each field's values at the keyframes.
    /// </summary>
    internal static List<(string Name, Dictionary<string, float[]> Fields)> ParseRegions(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        var regions = new List<(string Name, Dictionary<string, float[]> Fields)>();
        foreach (XmlNode region in document.SelectNodes("//cycle/region")!)
        {
            var fields = new Dictionary<string, float[]>();
            foreach (XmlNode field in region.ChildNodes)
                if (field.NodeType == XmlNodeType.Element)
                    fields[field.Name] = [.. field.InnerText.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))];
            regions.Add((regions.Count == 0 ? "GLOBAL" : region.Attributes?["name"]?.Value ?? "", fields));
        }
        if (regions.Count == 0)
            throw new InvalidDataException("The weather has no regions.");
        return regions;
    }

    /// <summary>
    /// A modifiers file: each modifier's name, its user flags, and the two values of each field it sets (the second
    /// nought where the file has one only).
    /// </summary>
    internal static IEnumerable<(string Name, uint Flags, Dictionary<string, (float A, float B)> Fields)> ParseModifiers(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        foreach (XmlNode modifier in document.SelectNodes("//modifier[@name]")!)
        {
            var fields = new Dictionary<string, (float, float)>();
            foreach (XmlNode field in modifier.ChildNodes)
                if (field.NodeType == XmlNodeType.Element
                    && field.InnerText.Split(Separators, StringSplitOptions.RemoveEmptyEntries) is [var first, .. var rest]
                    && float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                    fields[field.Name] = (a, rest is [var second, ..] && float.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out float b) ? b : 0);
            uint flags = uint.TryParse(modifier.Attributes?["userFlags"]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint read) ? read : 0;
            yield return (modifier.Attributes!["name"]!.Value, flags, fields);
        }
    }
}
