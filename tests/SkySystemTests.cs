using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class SkySystemTests
{
    [Fact]
    public void The_sky_takes_the_timecycles_colours_squared_times_their_intensities()
    {
        // EXTRASUNNY at 13:00: the zenith at intensity 0.02, and the sky's HDR at 38.
        SkySystem sky = SkySystem.At(13);
        TimecycleSample tc = Timecycle.Sample(13);
        Vector3 zenith = tc.Colour("sky_zenith_col");
        AssertClose(zenith * zenith * tc.Get("sky_zenith_col_inten"), sky.Zenith);
        Assert.Equal(38, sky.HdrIntensity, 3);
        Vector3 sun = tc.Colour("sky_sun_col");
        AssertClose(sun * sun * 255, sky.SunColourHdr);
        Assert.Equal(new Vector4(tc.Get("sky_zenith_transition_position"), tc.Get("sky_zenith_transition_east_blend"),
            tc.Get("sky_zenith_transition_west_blend"), 1 - tc.Get("sky_zenith_blend_start")), sky.ZenithConstants);
    }

    [Fact]
    public void The_suns_glow_is_the_games_cornette_shanks_phase_function()
    {
        // g = -0.49 - 0.5 * miephase; (2g, 1 + g², 1.5 (1 - g²) / (2 + g²) * miescatter / 1000, intensity).
        Vector4 constants = SkySystem.SunConstantsFor(.9478f, 1, .2f);
        float g = -.49f - .5f * .9478f;
        Assert.Equal(2 * g, constants.X, 5);
        Assert.Equal(1 + g * g, constants.Y, 5);
        Assert.Equal(1.5f * (1 - g * g) / (2 + g * g) / 1000, constants.Z, 7);
        Assert.Equal(.2f, constants.W);

        // It peaks towards the sun and is next to nothing away from it.
        SkySystem sky = SkySystem.At(13);
        Vector3 towardsSun = DayCycle.SunDirection(13);
        Vector3 atSun = sky.Radiance(towardsSun, towardsSun);
        Vector3 away = sky.Radiance(-towardsSun with { Z = Math.Abs(towardsSun.Z) }, towardsSun);
        Assert.True(atSun.Length() > away.Length() * 2, $"at the sun {atSun}, away {away}");
    }

    [Fact]
    public void The_gradient_runs_from_the_horizon_to_the_zenith_and_east_to_west()
    {
        SkySystem sky = SkySystem.At(13);
        // Overhead is the zenith colour (the blend starts at the transition in clear weather); with the sun far off.
        Vector3 farSun = Vector3.Normalize(new Vector3(0, 1, -1));
        AssertClose(sky.Zenith * sky.HdrIntensity, sky.Radiance(Vector3.UnitZ, farSun), .05f);
        // On the horizon due east and due west, the azimuth colours.
        AssertClose(sky.AzimuthEast * sky.HdrIntensity, sky.Radiance(Vector3.UnitX, farSun), .05f);
        AssertClose(sky.AzimuthWest * sky.HdrIntensity, sky.Radiance(-Vector3.UnitX, farSun), .05f);
        // The sky is mirrored below the horizon, as the game's dome is.
        AssertClose(sky.Radiance(Vector3.Normalize(new Vector3(1, 0, .3f)), farSun), sky.Radiance(Vector3.Normalize(new Vector3(1, 0, -.3f)), farSun), .05f);
    }

    [Fact]
    public void The_reflection_map_is_brightened_by_the_reflection_hdr_multiplier_and_has_a_full_mip_chain()
    {
        TimecycleSample tc = Timecycle.Sample(13);
        Assert.Equal(SkySystem.At(13).HdrIntensity * tc.Get("reflection_hdr_mult"), SkySystem.At(13, reflection: true).HdrIntensity, 4);
        Assert.Equal(10u, ViewportRenderer.MipsFor(Core.GraphicsSettings.DefaultReflectionWidth));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-3f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance * MathF.Max(1, expected.Length()), $"expected {expected}, got {actual}");
    }
}
