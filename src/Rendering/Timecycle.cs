using System.Numerics;
using System.Text.Json;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's light at one time of day, from its timecycle: the directional light (the sun, or the moon at night), the
/// natural ambient as a sky-to-ground hemisphere, the directional ambient, the ambient occlusion multipliers, the
/// exposure range and the bloom: the exposed luminance its bright pass is centred on, the width it fades in over, and
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
    float Exposure,
    float ExposureMin,
    float ExposureMax,
    float BloomThreshold,
    float BloomWidth,
    float BloomIntensity);

/// <summary>
/// GTA V's timecycle for clear weather (EXTRASUNNY, its global region, the same in both editions), interpolated
/// between its keyframes, with the exposure and filmic tone map the game's post-process builds from it
/// (postfx: PS_CalculateExposure and PS_Composite) and from visualsettings.dat.
/// </summary>
internal static class Timecycle
{
    private static readonly float[] Hours;
    private static readonly Dictionary<string, float[]> Values;

    // visualsettings.dat: the curve from the scene's luminance to an exposure in stops, in both editions.
    internal const float ExposureScale = -106.2f, ExposurePower = .007f, ExposureOffset = 104.7f;

    /// <summary>
    /// visualsettings.dat's filmic curves, bright and dark, and the exposures they belong to: the timecycle does not
    /// override them in clear weather. The tone map moves from one to the other as the exposure goes up.
    /// </summary>
    internal static readonly Filmic Bright = new(.22f, .3f, .1f, .2f, .01f, .3f, 4), Dark = new(.22f, .3f, .1f, .2f, 0, .3f, 4);
    internal const float BrightExposure = -.5f, DarkExposure = 3;

    /// <summary>
    /// The albedo of the ground the viewer puts below the horizon, where the game has its world: a mid-grey map,
    /// squared as GTA's lighting reads it.
    /// </summary>
    internal const float ReferenceAlbedo = .25f;

    static Timecycle()
    {
        using JsonDocument document = JsonDocument.Parse(ViewportRenderer.Read("timecycle.json"));
        JsonElement root = document.RootElement;
        Hours = [.. root.GetProperty("hours").EnumerateArray().Select(value => value.GetSingle())];
        Values = root.GetProperty("values").EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.EnumerateArray().Select(value => value.GetSingle()).ToArray());
    }

    /// <summary>
    /// The timecycle at <paramref name="hours"/> after midnight, each field interpolated between the keyframes either
    /// side, as the game blends them; any finite value wraps to the day.
    /// </summary>
    internal static TimecycleSample Sample(float hours)
    {
        if (!float.IsFinite(hours))
            throw new ArgumentOutOfRangeException(nameof(hours));
        hours %= 24;
        if (hours < 0)
            hours += 24;
        // The keyframe at or before this hour, and how far it is towards the next; the last one runs to midnight.
        int index = Hours.Length - 1;
        while (index > 0 && Hours[index] > hours)
            index--;
        int next = (index + 1) % Hours.Length;
        float end = next == 0 ? 24 : Hours[next];
        return new(index, next, (hours - Hours[index]) / (end - Hours[index]));
    }

    /// <summary>One field of the timecycle between two keyframes.</summary>
    internal static float Field(string name, int index, int next, float t) => float.Lerp(Values[name][index], Values[name][next], t);

    /// <summary>The light at <paramref name="hours"/> after midnight; any finite value wraps to the day.</summary>
    internal static TimecycleLight Evaluate(float hours)
    {
        TimecycleSample sample = Sample(hours);
        float Get(string name) => sample.Get(name);
        Vector3 Colour(string name) => sample.Colour(name);

        float natural = Get("natural_ambient_multiplier");
        float occlusion = Get("light_amb_occ_mult");
        return new(
            Colour("light_dir_col") * Get("light_dir_mult"),
            Colour("light_natural_amb_down_col") * Get("light_natural_amb_down_intensity") * natural,
            Colour("light_natural_amb_up_col") * Get("light_natural_amb_up_intensity") * Get("light_natural_amb_up_intensity_mult") * natural,
            Get("light_amb_down_wrap"),
            Colour("light_directional_amb_col") * Get("light_directional_amb_intensity") * Get("light_directional_amb_intensity_mult"),
            Get("light_directional_amb_bounce_enabled"),
            occlusion * Get("light_amb_occ_mult_veh"),
            occlusion * Get("light_amb_occ_mult_prop"),
            Get("postfx_exposure"),
            Get("postfx_exposure_min"),
            Get("postfx_exposure_max"),
            Get("postfx_bright_pass_thresh"),
            Get("postfx_bright_pass_thresh_width"),
            Get("postfx_intensity_bloom"));
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
    /// The natural ambient reaching a surface with this normal: the down light (from the sky) on what faces up, the up
    /// light (from the ground) on what faces down, the down light wrapping past the horizon by the down wrap.
    /// </summary>
    internal static Vector3 NaturalAmbient(in TimecycleLight light, Vector3 normal)
    {
        float h = MathF.Max(0, (normal.Z + light.DownWrap) / (1 + light.DownWrap));
        return Vector3.Lerp(light.NaturalUp, light.NaturalDown, h);
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
    /// PS_CalculateExposure: the exposure in stops for a scene of this luminance, from visualsettings.dat's curve plus
    /// the timecycle's exposure, kept in the timecycle's range.
    /// </summary>
    internal static float ExposureFor(float luminance, in TimecycleLight light)
    {
        float stops = ExposureScale * MathF.Pow(MathF.Max(luminance, 1e-6f), ExposurePower) + ExposureOffset + light.Exposure;
        return Math.Clamp(stops, light.ExposureMin, light.ExposureMax);
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

    /// <summary>The exposure GTA settles on for a frame of nothing but that ground.</summary>
    internal static float Exposure(in TimecycleLight light, Vector3 towardsLight) => ExposureFor(Luminance(GroundRadiance(light, towardsLight)), light);

    /// <summary>The filmic curve for this exposure: bright at or below the bright exposure, dark at or above the dark.</summary>
    internal static Filmic FilmicFor(float stops) => Filmic.Lerp(Bright, Dark, Math.Clamp((stops - BrightExposure) / (DarkExposure - BrightExposure), 0, 1));

    internal static float Luminance(Vector3 colour) => Vector3.Dot(colour, new(.2125f, .7154f, .0721f));
}

/// <summary>The timecycle between two keyframes, a fraction of the way from one to the next.</summary>
internal readonly record struct TimecycleSample(int Index, int Next, float T)
{
    internal float Get(string name) => Timecycle.Field(name, Index, Next, T);

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
