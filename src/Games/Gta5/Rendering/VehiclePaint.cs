using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HikariHopper.Rendering;

/// <summary>
/// One of the game's car colours (carcols.ymt): its number, which carvariations and the game's scripts name it by, its
/// name as the viewer shows it, its colour, the finish it's sold as, and the specular carcols gives it (its metallicID's
/// setting), none for a colour that keeps its material's.
/// </summary>
public sealed record CarColour(int Index, string Name, uint Argb, string Finish, CarMetallic? Metallic = null)
{
    /// <summary>Its colour in sRGB, 0 to 1, as a paint slot takes it.</summary>
    public Vector3 Srgb => new(((Argb >> 16) & 0xFF) / 255f, ((Argb >> 8) & 0xFF) / 255f, (Argb & 0xFF) / 255f);
}

/// <summary>
/// One of carcols' metallic settings: the specular a paint slot is drawn with in place of its material's, the game's
/// specInt for SpecularColor (the intensity), specFalloff for Specular (the power) and specFresnel for Fresnel.
/// </summary>
public sealed record CarMetallic(float Intensity, float Falloff, float Fresnel);

/// <summary>
/// What a car can be painted with: the game's colours, and the model's combinations of them (carvariations.ymt), each
/// the six slots' colour numbers as <see cref="VehiclePaint.Slots"/> lists them, none where the game leaves one as it is.
/// </summary>
public sealed record CarPaints(IReadOnlyList<CarColour> Colours, IReadOnlyList<int?[]> Combinations)
{
    private readonly Dictionary<int, CarColour> byIndex = Colours.ToDictionary(colour => colour.Index);

    /// <summary>The colour numbered <paramref name="index"/>; none for a number the game has no colour for.</summary>
    public CarColour? Colour(int? index) => index is { } at && byIndex.TryGetValue(at, out CarColour? colour) ? colour : null;

    /// <summary>The finishes the colours come in, in the order the picker shows them.</summary>
    public IReadOnlyList<string> Finishes => [.. CarColourNames.Finishes.Where(finish => Colours.Any(colour => colour.Finish == finish))];
}

/// <summary>
/// The colours a vehicle's paint slots are drawn in: primary, secondary, pearlescent, wheels, interior and dashboard,
/// each the game's grey where nothing sets it. A vehicle shader's matDiffuseColor (2, n, n) names slot n: 1 to 4 these
/// first four, 5 one the game leaves white, 6 and 7 the last two. Each slot's colour may carry a metallic setting, which
/// the game draws that slot's paint materials with.
/// </summary>
public sealed record VehiclePaint(IReadOnlyList<Vector3?> Colours, IReadOnlyList<CarMetallic?>? Metallics = null)
{
    /// <summary>The slots, in the order carvariations lists their colours.</summary>
    public static readonly string[] Slots = ["Primary", "Secondary", "Pearlescent", "Wheels", "Interior", "Dashboard"];

    /// <summary>Every slot as the game draws it with no vehicle's colours: grey 0x96.</summary>
    public static VehiclePaint Unset { get; } = new([null, null, null, null, null, null]);

    private static readonly Vector3 Grey = new(150 / 255f);

    /// <summary>The colours of the six slots, numbered as <paramref name="paints"/> lists them.</summary>
    public static VehiclePaint Of(CarPaints paints, IReadOnlyList<int?> indices)
    {
        CarColour?[] colours = [.. Enumerable.Range(0, Slots.Length).Select(slot => slot < indices.Count ? paints.Colour(indices[slot]) : null)];
        return new([.. colours.Select(colour => colour?.Srgb)], [.. colours.Select(colour => colour?.Metallic)]);
    }

    /// <summary>What matDiffuseColor (2, <paramref name="layer"/>, n) draws: the slot's colour, white for the fifth.</summary>
    public Vector3 Layer(int layer) => layer switch
    {
        >= 1 and <= 4 => Colours[layer - 1] ?? Grey,
        5 => Vector3.One,
        6 or 7 => Colours[layer - 2] ?? Grey,
        _ => Grey,
    };

    /// <summary>
    /// The metallic setting matDiffuseColor (2, <paramref name="layer"/>, n) is drawn with: its slot's colour's, none
    /// for the fifth or a colour without one, which keeps the material's own specular.
    /// </summary>
    public CarMetallic? Metallic(int layer) => Metallics is null ? null : layer switch
    {
        >= 1 and <= 4 when layer - 1 < Metallics.Count => Metallics[layer - 1],
        6 or 7 when layer - 2 < Metallics.Count => Metallics[layer - 2],
        _ => null,
    };
}

/// <summary>The game's colour names as the viewer shows them, and the finish each is sold as.</summary>
public static partial class CarColourNames
{
    /// <summary>The finishes, as the picker orders them.</summary>
    public static readonly string[] Finishes = ["Metallic", "Matte", "Util", "Worn", "Metals", "Other"];

    // The game's own spellings, put right.
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mettalic"] = "Metallic",
        ["metaillic"] = "Metallic",
        ["uil"] = "Util",
        ["foilage"] = "Foliage",
        ["steal"] = "Steel",
        ["alluminium"] = "Aluminium",
    };

    // Short words kept as the game writes them.
    private static readonly HashSet<string> Upper = new(StringComparer.OrdinalIgnoreCase) { "MP100", "V", "LS", "LSPD" };

    /// <summary>
    /// A name as carcols has it ("12 Matte Black", "156 DEFAULT ALLOY COLOR", "127police car blue") put as the viewer
    /// shows it: without its number, each word capitalised, the game's misspellings put right, and "Color" dropped from
    /// the end ("Default Alloy").
    /// </summary>
    public static string Tidy(string name)
    {
        string text = LeadingNumber().Replace(name.Trim(), "");
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tidy = new List<string>();
        foreach (string raw in words)
        {
            if (Words.TryGetValue(raw, out string? fixedWord))
                tidy.Add(fixedWord);
            else if (Upper.Contains(raw))
                tidy.Add(raw.ToUpperInvariant());
            else
                tidy.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.ToLowerInvariant()));
        }
        if (tidy.Count > 1 && tidy[^1] is "Color" or "Colour")
            tidy.RemoveAt(tidy.Count - 1);
        return tidy.Count > 0 ? string.Join(' ', tidy) : name.Trim();
    }

    /// <summary>The finish a colour is sold as, from its name: the game's own groups, misspellings included.</summary>
    public static string Finish(string name) => LeadingNumber().Replace(name.Trim(), "").Split(' ')[0].ToLowerInvariant() switch
    {
        "metallic" or "mettalic" or "metaillic" => "Metallic",
        "matte" => "Matte",
        "util" or "uil" => "Util",
        "worn" => "Worn",
        "brushed" or "chrome" => "Metals",
        _ => "Other",
    };

    [GeneratedRegex(@"^\d+\s*")]
    private static partial Regex LeadingNumber();
}
