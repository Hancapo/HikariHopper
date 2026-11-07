using System.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's sky (its <c>sky_system</c> effect) as the game sets it up from the timecycle: the values of
/// <c>sky_system_locals</c> it uploads for the sky's gradient and the sun's glow. Colours are the timecycle's
/// squared, times their intensities; HDR values are multiplied in.
/// </summary>
/// <param name="AzimuthEast">azimuthEastColor: the horizon's colour towards the east.</param>
/// <param name="AzimuthWest">azimuthWestColor: towards the west.</param>
/// <param name="AzimuthTransition">azimuthTransitionColor: between them.</param>
/// <param name="AzimuthTransitionPosition">azimuthTransitionPosition: where between east and west the transition is.</param>
/// <param name="Zenith">zenithColor: overhead.</param>
/// <param name="ZenithTransition">zenithTransitionColor: between the horizon and overhead.</param>
/// <param name="ZenithConstants">zenithConstants: the transition's height, its east and west blends, and one less the
/// zenith blend's start.</param>
/// <param name="HdrIntensity">hdrIntensity: the sky's brightness.</param>
/// <param name="SunColourHdr">sunColorHdr: the sun's glow.</param>
/// <param name="SunConstants">sunConstants: the glow's phase function (2g, 1 + g², its scale) and intensity.</param>
internal readonly record struct SkySystem(
    Vector3 AzimuthEast,
    Vector3 AzimuthWest,
    Vector3 AzimuthTransition,
    float AzimuthTransitionPosition,
    Vector3 Zenith,
    Vector3 ZenithTransition,
    Vector4 ZenithConstants,
    float HdrIntensity,
    Vector3 SunColourHdr,
    Vector4 SunConstants)
{
    /// <summary>
    /// The sky at this time of day. <paramref name="reflection"/> is the sky as the game draws it into its reflection
    /// map, brightened by the timecycle's reflection HDR multiplier.
    /// </summary>
    internal static SkySystem At(float hours, bool reflection = false)
    {
        TimecycleSample tc = Timecycle.Sample(hours);
        Vector3 Colour(string name) => Square(tc.Colour(name)) * tc.Get(name + "_inten");
        float k = reflection ? tc.Get("reflection_hdr_mult") : 1;
        return new(
            Colour("sky_azimuth_east_col"),
            Colour("sky_azimuth_west_col"),
            Colour("sky_azimuth_transition_col"),
            tc.Get("sky_azimuth_transition_position"),
            Colour("sky_zenith_col"),
            Colour("sky_zenith_transition_col"),
            new(tc.Get("sky_zenith_transition_position"), tc.Get("sky_zenith_transition_east_blend"),
                tc.Get("sky_zenith_transition_west_blend"), 1 - tc.Get("sky_zenith_blend_start")),
            tc.Get("sky_hdr") * k,
            Square(tc.Colour("sky_sun_col")) * tc.Get("sky_sun_hdr") * k,
            SunConstantsFor(tc.Get("sky_sun_miephase"), tc.Get("sky_sun_miescatter"), tc.Get("sky_sun_mie_intensity_mult")));
    }

    /// <summary>The game's sun glow constants: a Cornette-Shanks phase function with g from the timecycle's mie phase.</summary>
    internal static Vector4 SunConstantsFor(float miePhase, float mieScatter, float mieIntensity)
    {
        float g = -.49f - .5f * miePhase;
        float g2 = g * g;
        return new(2 * g, 1 + g2, 1.5f * (1 - g2) / (2 + g2) * (mieScatter / 1000), mieIntensity);
    }

    /// <summary>
    /// The sky's radiance in a direction, as vs/ps_sky_normal_sun compute it (sky_system.wgsl mirrors this): the
    /// gradient from east to west and from the horizon to the zenith, then the sun's glow over it.
    /// </summary>
    internal Vector3 Radiance(Vector3 direction, Vector3 towardsSun)
    {
        Vector3 d = Vector3.Normalize(direction);
        float a = MathF.Sqrt(MathF.Max(.5f - .5f * d.X, 0));
        float p = AzimuthTransitionPosition;
        Vector3 azimuth = a < p
            ? Vector3.Lerp(AzimuthEast, AzimuthTransition, a / p)
            : Vector3.Lerp(AzimuthTransition, AzimuthWest, (a - p) / (1 - p));
        float blend = float.Lerp(ZenithConstants.Y, ZenithConstants.Z, a);
        Vector3 toward = azimuth + blend * (ZenithTransition - azimuth);
        float h = MathF.Abs(d.Z);
        Vector3 gradient;
        if (h < ZenithConstants.X)
            gradient = azimuth + h / ZenithConstants.X * blend * (ZenithTransition - azimuth);
        else
        {
            float up = Math.Clamp((h - ZenithConstants.X) / (1 - ZenithConstants.X), 0, 1);
            gradient = Vector3.Lerp(toward, Zenith, Math.Clamp(up / ZenithConstants.W, 0, 1));
        }
        gradient *= HdrIntensity;

        // The shader takes c against -sunDirection, which points towards the sun: with g near -1 the glow peaks there.
        float c = -Vector3.Dot(d, towardsSun);
        float m = SunConstants.Z * (1 + c * c) / MathF.Pow(MathF.Abs(SunConstants.Y - SunConstants.X * c), 1.5f);
        return Math.Clamp(1 - m, 0, 1) * gradient + Math.Clamp(m, 0, 1) * SunColourHdr * SunConstants.W;
    }

    private static Vector3 Square(Vector3 colour) => colour * colour;
}
