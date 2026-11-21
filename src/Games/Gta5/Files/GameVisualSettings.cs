using System.Globalization;

namespace HikariHopper.Core;

/// <summary>
/// GTA V's visualsettings.dat, as the game has it in its archives (the update's copy over the base game's): each
/// setting's name and its values, one setting a line, <c>#</c> starting a comment. The game reads a setting with the
/// default its code gives, which counts only where the file hasn't it.
/// </summary>
internal sealed class GameVisualSettings
{
    private readonly Dictionary<string, float[]> settings;

    private GameVisualSettings(Dictionary<string, float[]> settings) => this.settings = settings;

    // Where the file is: in the update, then in the base game.
    private static readonly (string Archive, string Path)[] Places = [("update/update.rpf", "common/data/visualsettings.dat"), ("common.rpf", "data/visualsettings.dat")];

    /// <summary>The settings of the GTA V game <paramref name="game"/>; null when its file can't be read.</summary>
    public static GameVisualSettings? Read(LoadedGame game)
    {
        try
        {
            return GameFiles.ReadText(game, Places) is { } text ? Parse(text) : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>A visualsettings.dat's settings: each line's name and the numbers after it, up to a comment.</summary>
    internal static GameVisualSettings Parse(string text)
    {
        var settings = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw;
            int comment = line.IndexOf('#');
            if (comment >= 0)
                line = line[..comment];
            string[] parts = line.Split((char[])[' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            var values = new List<float>();
            foreach (string part in parts.Skip(1))
            {
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    break;
                values.Add(value);
            }
            if (values.Count > 0)
                settings[parts[0]] = [.. values];
        }
        return new GameVisualSettings(settings);
    }

    /// <summary>A setting's first value, or the game's default for it where the file hasn't it.</summary>
    public float Get(string name, float fallback) => settings.TryGetValue(name, out float[]? values) ? values[0] : fallback;
}
