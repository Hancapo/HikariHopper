using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using CommonFury.Hashing;

namespace HikariHopper.Core;

/// <summary>
/// GTA V's timecycle, as the game has it in its archives: clear weather's (w_extrasunny.xml, its GLOBAL region, each
/// field at the timecycle's thirteen keyframes) and the modifiers areas and interior rooms apply over it
/// (timecycle_mods_1.xml to _4), the update's copies over the base game's. Each modifier keeps the first value of each
/// field it sets. Read once per game folder, in the background, a second or so.
/// </summary>
internal sealed class GameTimecycle
{
    private static readonly ConcurrentDictionary<string, Lazy<GameTimecycle?>> Shared = new(StringComparer.OrdinalIgnoreCase);

    private GameTimecycle(Dictionary<string, float[]> weather, Dictionary<uint, Dictionary<string, float>> modifiers) =>
        (Weather, Modifiers) = (weather, modifiers);

    /// <summary>Clear weather's fields, each at the keyframes.</summary>
    public IReadOnlyDictionary<string, float[]> Weather { get; }

    /// <summary>The modifiers, by their name's hash: the fields each sets.</summary>
    public IReadOnlyDictionary<uint, Dictionary<string, float>> Modifiers { get; }

    // Where the files are: in the update, then in the base game.
    private static readonly (string Archive, string Folder)[] Places = [("update/update.rpf", "common/data/timecycle/"), ("common.rpf", "data/timecycle/")];

    /// <summary>The timecycle of the GTA V game <paramref name="game"/>, shared; null when its files can't be read.</summary>
    public static GameTimecycle? ForGame(LoadedGame game) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Root)), _ => new Lazy<GameTimecycle?>(() => Read(game))).Value;

    /// <summary>A timecycle of these fields and modifiers; for tests.</summary>
    internal static GameTimecycle From(Dictionary<string, float[]> weather, Dictionary<uint, Dictionary<string, float>> modifiers) => new(weather, modifiers);

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    private static GameTimecycle? Read(LoadedGame game)
    {
        try
        {
            string? weather = ReadText(game, "w_extrasunny.xml");
            if (weather is null)
                return null;
            var modifiers = new Dictionary<uint, Dictionary<string, float>>();
            for (int i = 1; i <= 4; i++)
                if (ReadText(game, $"timecycle_mods_{i}.xml") is { } text)
                    foreach (var (name, fields) in ParseModifiers(text))
                        modifiers[new Hash32(name.ToLowerInvariant()).Value] = fields;
            return new GameTimecycle(ParseWeather(weather), modifiers);
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

    /// <summary>A weather file's GLOBAL region: each field's values at the keyframes.</summary>
    internal static Dictionary<string, float[]> ParseWeather(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlNode region = document.SelectSingleNode("//region[@name='GLOBAL']") ?? throw new InvalidDataException("The weather has no GLOBAL region.");
        var fields = new Dictionary<string, float[]>();
        foreach (XmlNode field in region.ChildNodes)
            if (field.NodeType == XmlNodeType.Element)
                fields[field.Name] = [.. field.InnerText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))];
        return fields;
    }

    /// <summary>A modifiers file: each modifier's name and the first value of each field it sets.</summary>
    internal static IEnumerable<(string Name, Dictionary<string, float> Fields)> ParseModifiers(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        foreach (XmlNode modifier in document.SelectNodes("//modifier[@name]")!)
        {
            var fields = new Dictionary<string, float>();
            foreach (XmlNode field in modifier.ChildNodes)
                if (field.NodeType == XmlNodeType.Element
                    && field.InnerText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries) is [var first, ..]
                    && float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    fields[field.Name] = value;
            yield return (modifier.Attributes!["name"]!.Value, fields);
        }
    }
}
