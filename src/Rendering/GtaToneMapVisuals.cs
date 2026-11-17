namespace HikariHopper.Rendering;

/// <summary>
/// The exposure curve and filmic tone map in visualsettings.dat, as the game reads them:
/// the curve from the scene's luminance to an exposure in stops (its scale,
/// power and offset), and the bright and dark filmic curves with the exposures they belong to. The timecycle doesn't
/// change them: each frame the game puts these back over its own tone map fields, which only modifiers can override.
/// </summary>
internal sealed record GtaToneMapVisuals(float ExposureScale, float ExposurePower, float ExposureOffset, Filmic Bright, float BrightExposure,
    Filmic Dark, float DarkExposure)
{
    /// <summary>The settings with no file: each the default the game's code reads it with.</summary>
    public static readonly GtaToneMapVisuals GameDefaults = From(null);

    /// <summary>The game's file's settings, each with the default the game's code reads it with where the file hasn't it.</summary>
    public static GtaToneMapVisuals From(Core.GameVisualSettings? file)
    {
        float Get(string name, float fallback) => file?.Get(name, fallback) ?? fallback;
        Filmic Curve(string name) => new(
            Get($"Tonemapping.{name}.filmic.A", .22f),
            Get($"Tonemapping.{name}.filmic.B", .3f),
            Get($"Tonemapping.{name}.filmic.C", .1f),
            Get($"Tonemapping.{name}.filmic.D", .2f),
            Get($"Tonemapping.{name}.filmic.E", .01f),
            Get($"Tonemapping.{name}.filmic.F", .3f),
            Get($"Tonemapping.{name}.filmic.W", 4));
        return new(
            Get("Exposure.curve.scale", -1.0668907449987120E+02f),
            Get("Exposure.curve.power", 4.1897015173052825E-03f),
            Get("Exposure.curve.offset", 1.0460364172443734E+02f),
            Curve("bright"),
            Get("Tonemapping.bright.filmic.exposure", -2),
            Curve("dark"),
            Get("Tonemapping.dark.filmic.exposure", 1));
    }

    /// <summary>
    /// PS_CalculateExposure: the exposure in stops for a scene of this luminance, from the curve plus the timecycle's
    /// exposure, kept in the timecycle's range.
    /// </summary>
    public float ExposureFor(float luminance, in TimecycleLight light) =>
        Math.Clamp(ExposureScale * MathF.Pow(MathF.Max(luminance, 1e-6f), ExposurePower) + ExposureOffset + light.Exposure, light.ExposureMin, light.ExposureMax);

    /// <summary>
    /// TonemapParams, as the game sends it to PS_Composite: the filmic curve is the dark one's share of the way from the
    /// bright one, saturate(exposure × x + y); the exposures one apart when they are the same.
    /// </summary>
    public (float Scale, float Offset) Blend
    {
        get
        {
            float range = DarkExposure == BrightExposure ? 1 : DarkExposure - BrightExposure;
            return (1 / range, -BrightExposure / range);
        }
    }

    /// <summary>The filmic curve for this exposure, from the bright one towards the dark one as the exposure goes up.</summary>
    public Filmic FilmicFor(float stops)
    {
        var (scale, offset) = Blend;
        return Filmic.Lerp(Bright, Dark, Math.Clamp(stops * scale + offset, 0, 1));
    }
}
