using System.Globalization;
using System.Numerics;
using System.Xml;

namespace HikariHopper.Core;

/// <summary>
/// One layer of a GTA V cloud hat, as clouds.xml has it: its model, what it costs to show, how
/// fast it turns with the hat, how much it follows the camera up, and the heights it fades in above and out above.
/// </summary>
internal sealed record CloudHatLayerDefinition(string Filename, float CostFactor, float RotationScale, float CamPositionScalerAdjust,
    float HeightTrigger, float HeightFadeRange, float HeightTrigger2, float HeightFadeRange2);

/// <summary>
/// A GTA V cloud hat, as clouds.xml has it: where it is from the camera, its turn and scale,
/// its layers, its transition's alpha range and mid point, and its animation (the hat's turn a second, then the
/// three texture layers' blend weights, scroll a second, modes and visibility).
/// </summary>
internal sealed record CloudHatDefinition(string Name, Vector3 Position, Vector3 Rotation, Vector3 Scale, CloudHatLayerDefinition[] Layers,
    float TransitionAlphaRange, float TransitionMidPoint, bool Enabled, Vector3 AngularVelocity, Vector3 AnimBlendWeights, Vector2[] UVVelocity,
    byte[] AnimMode, bool[] ShowLayer, bool EnableAnimations);

/// <summary>
/// One keyframed setting of a weather's clouds (a particle keyframe of four values, as the game keeps it), with its times as the game
/// syncs them to the cloud keyframe times on load.
/// </summary>
internal sealed class CloudKeyframe
{
    private readonly (float Time, Vector4 Value)[] entries;

    internal CloudKeyframe((float Time, Vector4 Value)[] entries) => this.entries = entries;

    internal IReadOnlyList<(float Time, Vector4 Value)> Entries => entries;

    /// <summary>
    /// The value at <paramref name="time"/>: linear between the entries either side; past the
    /// last, the last; before the second, along the first two.
    /// </summary>
    internal Vector4 Query(float time)
    {
        if (entries.Length == 0)
            return Vector4.Zero;
        for (int i = 1; i < entries.Length; i++)
            if (time < entries[i].Time)
            {
                var (t0, v0) = entries[i - 1];
                var (t1, v1) = entries[i];
                float span = t1 - t0;
                return Vector4.Lerp(v0, v1, span > 0 ? (time - t0) / span : 0);
            }
        return entries[^1].Value;
    }

    /// <summary>
    /// The keyframe as the game keeps it after loading: one entry (time nought, all
    /// ones) where it has none or only a zero at nought, then sampled at each of <paramref name="times"/>, and the
    /// value at nought again at 24 so midnight doesn't jump.
    /// </summary>
    internal CloudKeyframe Synced(IReadOnlyList<float> times)
    {
        CloudKeyframe source = this;
        if (entries.Length == 0 || (entries.Length == 1 && entries[0].Time == 0 && entries[0].Value == Vector4.Zero))
            source = new CloudKeyframe([.. entries, (0, Vector4.One)]);
        var synced = new List<(float, Vector4)>(times.Count + 1);
        foreach (float time in times)
            synced.Add((time, source.Query(time)));
        var made = new CloudKeyframe([.. synced]);
        return new CloudKeyframe([.. synced, (24, made.Query(0))]);
    }
}

/// <summary>
/// A weather's clouds, as cloudkeyframes.xml has them: which hats it may show and how likely each
/// is (a bit and a weight per hat, then one for no hat), and its keyframed colours and
/// lighting.
/// </summary>
internal sealed record CloudSettings(bool[] Bits, int[] Probability, CloudKeyframe CloudColor, CloudKeyframe LightColor, CloudKeyframe AmbientColor,
    CloudKeyframe SkyColor, CloudKeyframe BounceColor, CloudKeyframe EastColor, CloudKeyframe WestColor, CloudKeyframe ScaleFillColors,
    CloudKeyframe DensityShiftScaleScatteringConstScale, CloudKeyframe PiercingLightPowerStrengthNormalStrengthThickness,
    CloudKeyframe ScaleDiffuseFillAmbientWrapAmount);

/// <summary>
/// GTA V's cloud hats and the clouds of its clear weather, as the game reads them from its archives:
/// <c>commoncrc:/data/clouds.xml</c> (the CloudHatManager: every hat, its layers and its animation),
/// <c>common:/data/cloudkeyframes.xml</c> (the CloudSettingsMap: each weather's clouds, keyframed over the day) and
/// the level's weather.xml (which settings EXTRASUNNY's clouds take, and its wind), the update's copies over the base
/// game's; and the archetype files the level keeps loaded (gta5.meta's PERMANENT_ITYP_FILEs), where the game finds
/// the hats' layers' archetypes by name.
/// </summary>
internal sealed record GameClouds(CloudHatDefinition[] Hats, Vector3 CamPositionScaler, float AltitudeScrollScaler, float[] KeyframeTimes,
    CloudSettings? Settings, float WindMin, float WindMax)
{
    /// <summary>The names of the level's permanent archetype files, as gta5.meta lists them (no folder, no extension).</summary>
    internal string[] ArchetypeFiles { get; init; } = [];

    /// <summary>The weather the viewer shows.</summary>
    internal const string Weather = "EXTRASUNNY";

    private static readonly (string Archive, string Path)[] HatPlaces = [("update/update.rpf", "common/data/clouds.xml"), ("common.rpf", "data/clouds.xml")];
    private static readonly (string Archive, string Path)[] KeyframePlaces =
        [("update/update.rpf", "common/data/cloudkeyframes.xml"), ("common.rpf", "data/cloudkeyframes.xml")];
    private static readonly (string Archive, string Path)[] WeatherPlaces =
        [("update/update.rpf", "common/data/levels/gta5/weather.xml"), ("common.rpf", "data/levels/gta5/weather.xml")];
    private static readonly (string Archive, string Path)[] LevelPlaces =
        [("update/update.rpf", "common/data/levels/gta5/gta5.meta"), ("common.rpf", "data/levels/gta5/gta5.meta")];

    /// <summary>The clouds of the GTA V game <paramref name="game"/>; null when its files can't be read.</summary>
    public static GameClouds? Read(LoadedGame game)
    {
        try
        {
            if (GameFiles.ReadText(game, HatPlaces) is not { } hats || GameFiles.ReadText(game, KeyframePlaces) is not { } keyframes)
                return null;
            return Parse(hats, keyframes, GameFiles.ReadText(game, WeatherPlaces)) with
            {
                ArchetypeFiles = GameFiles.ReadText(game, LevelPlaces) is { } level ? PermanentArchetypes(level) : [],
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or EndOfStreamException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// The clouds from the files' text: clouds.xml's hats, cloudkeyframes.xml's settings for the weather's clouds and
    /// weather.xml's weather (null: the weather's settings and wind aren't known, and no hat is chosen).
    /// </summary>
    internal static GameClouds Parse(string hats, string keyframes, string? weather)
    {
        var manager = new XmlDocument();
        manager.LoadXml(hats);
        XmlElement root = manager.DocumentElement ?? throw new InvalidDataException("clouds.xml is empty.");
        CloudHatDefinition[] list = [.. Items(root["mCloudHatFrags"]).Select(Hat)];
        // mCamPositionScaler isn't initialised by the parser: the manager's own one, all ones, where the file hasn't it.
        Vector3 scaler = Vector(root["mCamPositionScaler"], Vector3.One);
        float scroll = Value(root["mAltitudeScrollScaler"], 0);

        var map = new XmlDocument();
        map.LoadXml(keyframes);
        XmlElement mapRoot = map.DocumentElement ?? throw new InvalidDataException("cloudkeyframes.xml is empty.");
        float[] times = Floats(mapRoot["KeyframeTimes"]);

        string? settingsName = null;
        float windMin = 0, windMax = 0;
        if (weather is not null)
        {
            var weathers = new XmlDocument();
            weathers.LoadXml(weather);
            foreach (XmlElement type in weathers.GetElementsByTagName("Item").OfType<XmlElement>())
                if (string.Equals(type["Name"]?.InnerText.Trim(), Weather, StringComparison.OrdinalIgnoreCase))
                {
                    settingsName = type["CloudSettingsName"]?.InnerText.Trim();
                    windMin = Value(type["WindMin"], 0);
                    windMax = Value(type["WindMax"], 0);
                    break;
                }
        }
        CloudSettings? settings = null;
        if (settingsName is not null)
            foreach (XmlElement item in Items(mapRoot["SettingsMap"]))
                if (string.Equals(item["Name"]?.InnerText.Trim(), settingsName, StringComparison.OrdinalIgnoreCase) && item["Settings"] is { } found)
                {
                    settings = ReadSettings(found, times, list.Length);
                    break;
                }
        return new GameClouds(list, scaler, scroll, times, settings, windMin, windMax);
    }

    /// <summary>A level meta's permanent archetype files (its PERMANENT_ITYP_FILE entries), by name, in its order.</summary>
    internal static string[] PermanentArchetypes(string meta)
    {
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            // The meta's includes are xi: elements its root doesn't declare a namespace for.
            document.LoadXml(meta.Replace("xi:include", "include", StringComparison.Ordinal));
        }
        catch (XmlException)
        {
            return [];
        }
        var names = new List<string>();
        foreach (XmlElement item in document.GetElementsByTagName("Item").OfType<XmlElement>())
            if (string.Equals(item["fileType"]?.InnerText.Trim(), "PERMANENT_ITYP_FILE", StringComparison.OrdinalIgnoreCase)
                && item["filename"]?.InnerText.Trim() is { Length: > 0 } file)
                names.Add(Path.GetFileNameWithoutExtension(file.Split('/', '\\')[^1]));
        return [.. names];
    }

    private static CloudHatDefinition Hat(XmlElement item)
    {
        CloudHatLayerDefinition[] layers = [.. Items(item["mLayers"]).Select(layer => new CloudHatLayerDefinition(
            layer["mFilename"]?.InnerText.Trim() ?? "",
            Value(layer["mCostFactor"], 1),
            Value(layer["mRotationScale"], 1),
            Value(layer["mCamPositionScalerAdjust"], 1),
            Value(layer["mHeightTigger"], 0),
            Value(layer["mHeightFadeRange"], 0),
            Value(layer["mHeightTrigger2"], 0),
            Value(layer["mHeightFadeRange2"], 0)))];
        float[] velocity = Floats(item["mUVVelocity"]);
        var uv = new Vector2[3];
        for (int i = 0; i < 3 && i * 2 + 1 < velocity.Length; i++)
            uv[i] = new Vector2(velocity[i * 2], velocity[i * 2 + 1]);
        // As the container's constructor leaves them where the file hasn't them: combine, and only the first shown.
        byte[] modes = [.. Floats(item["mAnimMode"]).Select(value => (byte)value).Concat(Enumerable.Repeat((byte)0, 3)).Take(3)];
        bool[] show = [true, false, false];
        int shown = 0;
        foreach (XmlElement flag in Items(item["mShowLayer"]))
            if (shown < 3)
                show[shown++] = Bool(flag, false);
        return new CloudHatDefinition(item["mName"]?.InnerText.Trim() ?? "",
            Vector(item["mPosition"], Vector3.Zero), Vector(item["mRotation"], Vector3.Zero), Vector(item["mScale"], Vector3.One), layers,
            Value(item["mTransitionAlphaRange"], 0), Value(item["mTransitionMidPoint"], 0), Bool(item["mEnabled"], true),
            Vector(item["mAngularVelocity"], Vector3.Zero), Vector(item["mAnimBlendWeights"], Vector3.One), uv, modes, show,
            Bool(item["mEnableAnimations"], false));
    }

    private static CloudSettings ReadSettings(XmlElement settings, float[] times, int hats)
    {
        XmlElement? list = settings["CloudList"];
        int[] probability = [.. Floats(list?["mProbability"]).Select(value => (int)value)];
        var bits = new List<bool>();
        if (list?["mBits"] is { } words)
        {
            int count = int.TryParse(words.GetAttribute("bits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            string[] parts = words.InnerText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < count; i++)
            {
                int word = i / 32;
                uint value = word < parts.Length ? ParseWord(parts[word]) : 0;
                bits.Add((value >> (i % 32) & 1) != 0);
            }
        }
        var (fixedBits, fixedProbability) = Fixup([.. bits], probability, hats);
        CloudKeyframe Key(string name) => Keyframe(settings[name]).Synced(times);
        return new CloudSettings(fixedBits, fixedProbability, Key("CloudColor"), Key("CloudLightColor"), Key("CloudAmbientColor"), Key("CloudSkyColor"),
            Key("CloudBounceColor"), Key("CloudEastColor"), Key("CloudWestColor"), Key("CloudScaleFillColors"),
            Key("CloudDensityShift_Scale_ScatteringConst_Scale"), Key("CloudPiercingLightPower_Strength_NormalStrength_Thickness"),
            Key("CloudScaleDiffuseFillAmbient_WrapAmount"));
    }

    /// <summary>
    /// A weather's hat list made to fit the hats there are: a bit and a weight for
    /// each hat, then one for no hat, which keeps the last of the file's; new weights are one.
    /// </summary>
    internal static (bool[] Bits, int[] Probability) Fixup(bool[] bits, int[] probability, int hats)
    {
        int count = hats + 1;
        bool[] fixedBits = bits;
        if (bits.Length != count)
        {
            bool none = bits.Length > 1 && bits[^1];
            fixedBits = new bool[count];
            for (int i = 0; i < Math.Min(bits.Length - 1, hats); i++)
                fixedBits[i] = bits[i];
            fixedBits[hats] = none;
        }
        int[] fixedProbability = probability;
        if (probability.Length != count)
        {
            // The file's last weight was no hat's: it moves to the new end, and its place goes back to one.
            bool moved = probability.Length > 1;
            int none = moved ? probability[^1] : 1;
            fixedProbability = new int[count];
            for (int i = 0; i < count; i++)
                fixedProbability[i] = i < probability.Length && !(moved && i == probability.Length - 1) ? probability[i] : 1;
            fixedProbability[hats] = none;
        }
        return (fixedBits, fixedProbability);
    }

    private static CloudKeyframe Keyframe(XmlElement? keyframe)
    {
        XmlElement? data = keyframe?["keyData"];
        float[] values = Floats(data?["keyEntryData"]);
        int count = data?["numKeyEntries"] is { } n ? (int)Value(n, 0) : values.Length / 5;
        var entries = new List<(float, Vector4)>();
        for (int i = 0; i < count && i * 5 + 4 < values.Length; i++)
            entries.Add((values[i * 5], new Vector4(values[i * 5 + 1], values[i * 5 + 2], values[i * 5 + 3], values[i * 5 + 4])));
        return new CloudKeyframe([.. entries]);
    }

    private static uint ParseWord(string word) => word.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(word[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : uint.Parse(word, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static IEnumerable<XmlElement> Items(XmlElement? parent) =>
        parent?.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "Item") ?? [];

    private static float Value(XmlElement? element, float fallback) =>
        element is not null && float.TryParse(element.GetAttribute("value"), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    private static bool Bool(XmlElement? element, bool fallback) =>
        element is not null && bool.TryParse(element.GetAttribute("value"), out bool value) ? value : fallback;

    private static Vector3 Vector(XmlElement? element, Vector3 fallback)
    {
        if (element is null)
            return fallback;
        float Axis(string name, float otherwise) =>
            float.TryParse(element.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : otherwise;
        return new Vector3(Axis("x", fallback.X), Axis("y", fallback.Y), Axis("z", fallback.Z));
    }

    private static float[] Floats(XmlElement? element) => element is null ? []
        : [.. element.InnerText.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))];
}
