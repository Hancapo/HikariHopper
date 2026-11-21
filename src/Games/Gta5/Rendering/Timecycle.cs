using System.Numerics;
using System.Text.Json;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's light at one time of day, from its timecycle: the directional light (the sun, or the moon at night), the
/// natural ambient as a base and a down light from the sky, the directional ambient, the artificial ambient outside interiors (the
/// same hemisphere; nought where the timecycle in use doesn't have it), the ambient occlusion multipliers, the
/// peds' rim light, the exposure range and the bloom: the exposed luminance its bright pass is centred on, the width it fades in over, and
/// its intensity. Colours are already multiplied by their intensities.
/// </summary>
internal readonly record struct TimecycleLight(
    Vector3 Directional,
    Vector3 NaturalDown,
    Vector3 NaturalUp,
    float DownWrap,
    Vector3 DirectionalAmbient,
    float Bounce,
    float OcclusionVehicle,
    float OcclusionProp,
    float OcclusionPed,
    float PedRim,
    float Exposure,
    float ExposureMin,
    float ExposureMax,
    float BloomThreshold,
    float BloomWidth,
    float BloomIntensity,
    Vector3 ArtificialDown = default,
    Vector3 ArtificialUp = default,
    float WaterFogLight = 0,
    float DirectionalIntensity = 0);

/// <summary>
/// GTA V's timecycle for clear weather (EXTRASUNNY, its global region, the same in both editions), interpolated
/// between its keyframes, with the exposure and filmic tone map the game's post-process builds from it
/// (postfx: PS_CalculateExposure and PS_Composite) and from visualsettings.dat. Read from the game once one is open
/// (<see cref="Use"/>, <see cref="UseVisuals"/>); until then, the timecycle from a copy of its values kept with the
/// app (Gta5/timecycle.json) and visualsettings.dat's settings as the game's code defaults them.
/// </summary>
internal static class Timecycle
{
    private static readonly float[] Hours;
    private static readonly Dictionary<string, float[]> Kept;
    private static volatile Dictionary<string, float[]> Values;
    private static volatile GtaToneMapVisuals toneMap = GtaToneMapVisuals.GameDefaults;
    // The game's weather's second region (URBAN), each field at the keyframes; none before a game is open.
    private static volatile Dictionary<string, float[]>? Region;


    /// <summary>
    /// The gamma PS_Composite encodes with: it raises the tone-mapped colour to its Gamma, one over this, itself, and
    /// writes it to a plain 8-bit target. The game sets it from the Brightness slider, 1.4 + 1.6 × slider / 30, which is
    /// 2.2 at the slider's middle; one of its paths uses 2.2 whatever the slider says.
    /// </summary>
    internal const float DisplayGamma = 2.2f;

    /// <summary>A tone-mapped colour channel as the game writes it, 0 to 1.</summary>
    internal static float Display(float mapped) => MathF.Pow(Math.Clamp(mapped, 0, 1), 1 / DisplayGamma);

    /// <summary>
    /// The albedo of the ground the viewer puts below the horizon, where the game has its world: a mid-grey map,
    /// squared as GTA's lighting reads it.
    /// </summary>
    internal const float ReferenceAlbedo = .25f;

    static Timecycle()
    {
        using JsonDocument document = JsonDocument.Parse(ViewportRenderer.Read("Gta5/timecycle.json"));
        JsonElement root = document.RootElement;
        Hours = [.. root.GetProperty("hours").EnumerateArray().Select(value => value.GetSingle())];
        Kept = root.GetProperty("values").EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Values = Kept;
    }

    /// <summary>
    /// Takes the game's clear weather from now on, each field it has at the keyframes over the copy kept with the app,
    /// and its second region (URBAN) to blend over it by place; null goes back to the copy. The keyframes are kept as
    /// the game keeps them, at half precision. Views pick it up the next time they set their hour.
    /// </summary>
    internal static void Use(Core.GameTimecycle? game)
    {
        var values = new Dictionary<string, float[]>(Kept);
        foreach (var (name, keyframes) in game?.Weather ?? new Dictionary<string, float[]>())
            if (keyframes.Length == Hours.Length)
                values[name] = Half(keyframes);
        Dictionary<string, float[]>? region = null;
        // The game blends two regions at most.
        if (game is { Regions.Count: > 1 })
        {
            region = [];
            foreach (var (name, keyframes) in game.Regions[1].Fields)
                if (keyframes.Length == Hours.Length)
                    region[name] = Half(keyframes);
        }
        (Values, Region) = (values, region);
    }

    /// <summary>
    /// Takes the exposure curve and filmic tone map from the game's visualsettings.dat from now on, each setting it
    /// lacks as the game's code defaults it; null goes back to those defaults. Views pick it up the next time they set
    /// their hour.
    /// </summary>
    internal static void UseVisuals(Core.GameVisualSettings? game) => toneMap = GtaToneMapVisuals.From(game);

    /// <summary>The exposure curve and filmic tone map in use, the game's visualsettings.dat's or its code's defaults.</summary>
    internal static GtaToneMapVisuals ToneMap => toneMap;

    // A keyframe's values as the game stores them: half-precision floats.
    private static float[] Half(float[] keyframes) => [.. keyframes.Select(value => (float)(Half)value)];

    /// <summary>Whether the timecycle in use has this field, the game's or the kept copy's.</summary>
    internal static bool Has(string name) => Values.ContainsKey(name);

    /// <summary>
    /// The timecycle at <paramref name="hours"/> after midnight, each field interpolated between the keyframes either
    /// side, as the game blends them; any finite value wraps to the day.
    /// </summary>
    internal static TimecycleSample Sample(float hours)
    {
        if (!float.IsFinite(hours))
            throw new ArgumentOutOfRangeException(nameof(hours));
        hours = WrapHours(hours);
        // The keyframe at or before this hour, and how far it is towards the next; the last one runs to midnight.
        int index = Hours.Length - 1;
        while (index > 0 && Hours[index] > hours)
            index--;
        int next = (index + 1) % Hours.Length;
        float end = next == 0 ? 24 : Hours[next];
        return new(index, next, (hours - Hours[index]) / (end - Hours[index]));
    }

    /// <summary>A finite time wrapped to the day, 0 to 24.</summary>
    internal static float WrapHours(float hours)
    {
        hours %= 24;
        return hours < 0 ? hours + 24 : hours;
    }

    /// <summary>One field of the timecycle between two keyframes.</summary>
    internal static float Field(string name, int index, int next, float t) => Field(name, index, next, t, 0);

    /// <summary>One field of the timecycle between two keyframes, or <paramref name="fallback"/> where it hasn't the field.</summary>
    internal static float Field(string name, int index, int next, float t, float fallback) =>
        Values.TryGetValue(name, out float[]? keyframes) ? float.Lerp(keyframes[index], keyframes[next], t) : fallback;

    /// <summary>
    /// The timecycle at <paramref name="hours"/> after midnight at <paramref name="place"/> in the game's world, worked
    /// out into <paramref name="frame"/> as the game does: every field between
    /// its keyframes, GLOBAL's then the second region's blended over it by the region's strength (the second alone at
    /// full strength), then each box modifier applied over that in turn, at its strength, as its field blends. A field
    /// the second region lacks keeps GLOBAL's (the game's own regions have the same fields).
    /// </summary>
    internal static TimecycleSample Resolve(float hours, TimecyclePlace? place, TimecycleFrame frame)
    {
        TimecycleSample sample = Sample(hours);
        var (index, next, t) = sample;
        Dictionary<string, float[]> values = Values;
        Dictionary<string, float[]>? region = Region;
        float strength = place?.Region ?? 0;
        Dictionary<string, float> resolved = frame.Values;
        if (!ReferenceEquals(frame.Source, values))
        {
            resolved.Clear();
            frame.Source = values;
        }
        foreach (var (name, keyframes) in values)
        {
            float value = Between(keyframes);
            if (strength > 0 && region is not null && region.TryGetValue(name, out float[]? local))
                value = strength >= 1 ? Between(local) : value + (Between(local) - value) * strength;
            resolved[name] = value;
        }
        if (place is not null)
            foreach (var (modifier, at) in place.Modifiers)
                foreach (var (name, (a, b)) in modifier.Fields)
                    if (resolved.TryGetValue(name, out float value))
                        resolved[name] = TimecycleModifierBlend.Apply(TimecycleModifierBlend.Of(name), value, a, b, Math.Clamp(at, 0, 1));
        return sample with { Frame = frame };

        float Between(float[] keyframes) => keyframes[index] + (keyframes[next] - keyframes[index]) * t;
    }

    /// <summary>The light at <paramref name="hours"/> after midnight; any finite value wraps to the day.</summary>
    internal static TimecycleLight Evaluate(float hours) => Evaluate(Sample(hours));

    /// <summary>The light of a sample of the timecycle.</summary>
    internal static TimecycleLight Evaluate(in TimecycleSample sample)
    {
        TimecycleSample tc = sample;
        float Get(string name) => tc.Get(name);
        Vector3 Colour(string name) => tc.Colour(name);

        // As the game sets the frame's: the natural ambient multiplier isn't the frame's (it scales what's
        // inside a room or a box, entity by entity), so a modifier box at the camera that sets it changes nothing here.
        float occlusion = Get("light_amb_occ_mult");
        return new(
            Colour("light_dir_col") * Get("light_dir_mult"),
            Colour("light_natural_amb_down_col") * Get("light_natural_amb_down_intensity"),
            Colour("light_natural_amb_up_col") * Get("light_natural_amb_up_intensity") * Get("light_natural_amb_up_intensity_mult"),
            Get("light_amb_down_wrap"),
            Colour("light_directional_amb_col") * Get("light_directional_amb_intensity") * Get("light_directional_amb_intensity_mult"),
            Get("light_directional_amb_bounce_enabled"),
            occlusion * Get("light_amb_occ_mult_veh"),
            occlusion * Get("light_amb_occ_mult_prop"),
            occlusion * Get("light_amb_occ_mult_ped"),
            Get("light_ped_rim_mult"),
            Get("postfx_exposure"),
            Get("postfx_exposure_min"),
            Get("postfx_exposure_max"),
            Get("postfx_bright_pass_thresh"),
            Get("postfx_bright_pass_thresh_width"),
            Get("postfx_intensity_bloom"),
            Colour("light_artificial_ext_down_col") * Get("light_artificial_ext_down_intensity"),
            Colour("light_artificial_ext_up_col") * Get("light_artificial_ext_up_intensity"),
            Get("water_foglight"),
            Get("light_dir_mult"));
    }

    /// <summary>
    /// BloomParams, as the game sets it for the bright pass and the composite: the exposed luminance the bright pass
    /// starts at (the threshold less half the width), the bloom's intensity, and one over the width it fades in over;
    /// with no width, it starts at the threshold, at once.
    /// </summary>
    internal static Vector4 BloomParameters(in TimecycleLight light)
    {
        if (light.BloomWidth <= 0)
            return new(light.BloomThreshold, light.BloomIntensity, 1, 0);
        float low = MathF.Max(0, light.BloomThreshold - light.BloomWidth / 2);
        float high = MathF.Max(0, light.BloomThreshold + light.BloomWidth / 2);
        return new(low, light.BloomIntensity, 1 / (high - low), 0);
    }

    /// <summary>
    /// The natural ambient reaching a surface with this normal, as the game's ambient pass has it: the up light, its
    /// base, whichever way the surface faces, plus the down light (from the sky), all of it on what faces up, wrapping
    /// past the horizon by the down wrap, none on what faces further down.
    /// </summary>
    internal static Vector3 NaturalAmbient(in TimecycleLight light, Vector3 normal)
    {
        float h = MathF.Max(0, (normal.Z + light.DownWrap) / (1 + light.DownWrap));
        return light.NaturalUp + light.NaturalDown * h;
    }

    /// <summary>
    /// The way the directional ambient comes from: the directional light, turned below the horizon (bounced off the
    /// ground) as far as the timecycle's bounce says.
    /// </summary>
    internal static Vector3 DirectionalAmbientDirection(in TimecycleLight light, Vector3 towardsLight)
    {
        Vector3 bounced = new(towardsLight.X, towardsLight.Y, -towardsLight.Z);
        Vector3 direction = Vector3.Lerp(towardsLight, bounced, Math.Clamp(light.Bounce, 0, 1));
        return direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
    }

    /// <summary>
    /// The stand-in ground's radiance: open ground of the reference albedo under this light, lit as GTA lights it
    /// (the directional light, the natural ambient from above and the directional ambient).
    /// </summary>
    internal static Vector3 GroundRadiance(in TimecycleLight light, Vector3 towardsLight)
    {
        Vector3 up = Vector3.UnitZ;
        Vector3 irradiance = light.Directional * MathF.Max(towardsLight.Z, 0) + NaturalAmbient(light, up)
            + light.DirectionalAmbient * MathF.Max(Vector3.Dot(DirectionalAmbientDirection(light, towardsLight), up), 0);
        return ReferenceAlbedo * irradiance;
    }

    /// <summary>The exposure GTA settles on for a frame of nothing but that ground, with this exposure curve.</summary>
    internal static float Exposure(GtaToneMapVisuals curve, in TimecycleLight light, Vector3 towardsLight) =>
        curve.ExposureFor(Luminance(GroundRadiance(light, towardsLight)), light);

    internal static float Luminance(Vector3 colour) => Vector3.Dot(colour, new(.2125f, .7154f, .0721f));
}

/// <summary>
/// The timecycle between two keyframes, a fraction of the way from one to the next: GLOBAL's, or with a frame, the
/// frame's, worked out at a place in the world.
/// </summary>
internal readonly record struct TimecycleSample(int Index, int Next, float T)
{
    /// <summary>The fields worked out at a place (<see cref="Timecycle.Resolve"/>); none for GLOBAL's alone.</summary>
    internal TimecycleFrame? Frame { get; init; }

    internal float Get(string name) => Frame is { } frame ? frame.Get(name) : Timecycle.Field(name, Index, Next, T);

    /// <summary>
    /// A field (the place's blend, with a frame), or the default the game's code gives it (its variable table)
    /// where the timecycle hasn't it.
    /// </summary>
    internal float Get(string name, float fallback) => Timecycle.Has(name) ? Get(name) : fallback;

    /// <summary>The same as <see cref="Get(string, float)"/>.</summary>
    internal float GetOr(string name, float fallback) => Get(name, fallback);

    /// <summary>A colour field's red, green and blue (<c>name_r</c>, <c>_g</c>, <c>_b</c>).</summary>
    internal Vector3 Colour(string name) => new(Get(name + "_r"), Get(name + "_g"), Get(name + "_b"));
}

/// <summary>GTA's filmic tone curve (Hable's), as PS_Composite applies it: A to F shape it, W is the white point.</summary>
internal readonly record struct Filmic(float A, float B, float C, float D, float E, float F, float W)
{
    internal float Curve(float x) => (x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F) - E / F;

    /// <summary>A linear colour channel after exposure, mapped to the display's range with W at one.</summary>
    internal float Map(float x) => Math.Clamp(Curve(MathF.Max(x, 0)) / Curve(W), 0, 1);

    internal static Filmic Lerp(Filmic a, Filmic b, float t) => new(
        float.Lerp(a.A, b.A, t), float.Lerp(a.B, b.B, t), float.Lerp(a.C, b.C, t), float.Lerp(a.D, b.D, t),
        float.Lerp(a.E, b.E, t), float.Lerp(a.F, b.F, t), float.Lerp(a.W, b.W, t));
}
