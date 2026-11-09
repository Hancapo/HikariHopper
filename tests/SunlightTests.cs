using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class SunlightTests
{
    [Fact]
    public void The_viewers_afternoon_sun_is_up_and_past_noon()
    {
        KeyLight key = DayCycle.Evaluate(DayCycle.DefaultHours);
        Assert.True(key.Sun);
        Assert.Equal(1, key.Direction.Length(), 3);
        // High in the sky, and after the solar noon (13:00) it has moved west: +X is east.
        Assert.InRange(key.Direction.Z, .5f, .95f);
        Assert.True(key.Direction.X < 0, "west of the noon meridian");
    }

    [Fact]
    public void At_night_the_moon_is_the_light_and_the_timecycle_dims_it()
    {
        KeyLight night = DayCycle.Evaluate(1);
        Assert.False(night.Sun);
        Assert.True(night.Direction.Z > 0, "the moon is up when the sun is down");
        Assert.True(Timecycle.Evaluate(1).Directional.Length() < Timecycle.Evaluate(DayCycle.DefaultHours).Directional.Length() / 50);
        // The hour wraps.
        Assert.Equal(DayCycle.SunDirection(14), DayCycle.SunDirection(38));
    }

    [Fact]
    public void The_timecycle_is_its_keyframes_and_runs_between_them()
    {
        // EXTRASUNNY's 13:00 keyframe: a warm sun at 64, the sky bluish at 5 and the ground warm at 4.5.
        TimecycleLight one = Timecycle.Evaluate(13);
        AssertClose(new Vector3(1.002f, .8721f, .6309f) * 64, one.Directional);
        AssertClose(new Vector3(.5977f, .7725f, .9395f) * 5, one.NaturalDown);
        AssertClose(new Vector3(.9771f, .8394f, .5811f) * 4.5f, one.NaturalUp);
        Assert.Equal(.7979f, one.OcclusionVehicle, 4);
        Assert.Equal(1, one.OcclusionProp, 4);

        // Half past two is halfway to the 16:00 keyframe, in each field: the colour, and the multiplier at 58.
        Vector3 colour = Vector3.Lerp(new(1.002f, .8721f, .6309f), new(1, .7852f, .3914f), .5f);
        AssertClose(colour * 58, Timecycle.Evaluate(14.5f).Directional);
        // The last keyframe (22:00) runs to midnight's, and the day wraps.
        AssertClose(Vector3.Lerp(Timecycle.Evaluate(22).NaturalDown, Timecycle.Evaluate(0).NaturalDown, .5f), Timecycle.Evaluate(23).NaturalDown);
        AssertClose(Timecycle.Evaluate(1).NaturalUp, Timecycle.Evaluate(-23).NaturalUp);
    }

    [Fact]
    public void The_natural_ambient_is_the_sky_above_and_the_ground_below()
    {
        TimecycleLight light = Timecycle.Evaluate(13);
        AssertClose(light.NaturalDown, Timecycle.NaturalAmbient(light, Vector3.UnitZ));
        AssertClose(light.NaturalUp, Timecycle.NaturalAmbient(light, -Vector3.UnitZ));
        // The sky's light wraps past the horizon, by the down wrap: a wall still gets some, and so does what faces a little
        // downwards, until the wrap.
        Vector3 Facing(float z) => Timecycle.NaturalAmbient(light, Vector3.Normalize(new Vector3(MathF.Sqrt(1 - z * z), 0, z)));
        Assert.InRange(light.DownWrap, .2f, .4f);
        Assert.True(Vector3.Distance(Facing(0), light.NaturalUp) > .5f);
        Assert.True(Vector3.Distance(Facing(-light.DownWrap / 2), light.NaturalUp) > .1f);
        AssertClose(light.NaturalUp, Facing(-light.DownWrap));
    }

    [Fact]
    public void The_exposure_follows_the_games_curve_darker_by_day_brighter_by_night()
    {
        TimecycleLight day = Timecycle.Evaluate(DayCycle.DefaultHours);
        // visualsettings.dat's curve: a luminance of one is a stop and a half down.
        Assert.Equal(-1.5f, Timecycle.ExposureFor(1, day), 3);
        float afternoon = Timecycle.Exposure(day, DayCycle.Evaluate(DayCycle.DefaultHours).Direction);
        Assert.InRange(afternoon, -3.5f, -2);
        float night = Timecycle.Exposure(Timecycle.Evaluate(1), DayCycle.Evaluate(1).Direction);
        Assert.True(night > afternoon + 2, $"night {night}, afternoon {afternoon}");
        Assert.InRange(night, -3.5f, 5);
        // Daylight uses the bright curve.
        Assert.Equal(Timecycle.Bright, Timecycle.FilmicFor(afternoon));
    }

    [Fact]
    public void The_filmic_curve_maps_black_to_black_and_the_white_point_to_one()
    {
        Filmic curve = Timecycle.Bright;
        Assert.Equal(0, curve.Map(0), 4);
        Assert.Equal(1, curve.Map(curve.W), 4);
        Assert.Equal(1, curve.Map(curve.W * 10));
        // A shoulder: steeper in the mid-tones than near white.
        Assert.True(curve.Map(.5f) - curve.Map(.25f) > curve.Map(3.75f) - curve.Map(3.5f));
        // Dark scenes use a curve without the bright one's toe offset.
        Assert.Equal(Timecycle.Dark, Timecycle.FilmicFor(Timecycle.DarkExposure + 1));
    }

    [Fact]
    public void The_game_writes_the_tone_mapped_colour_raised_to_one_over_2_2()
    {
        // The Brightness slider's middle: 1.4 + 1.6 × 15 / 30.
        Assert.Equal(1.4f + 1.6f * 15 / 30, Timecycle.DisplayGamma, 5);
        Assert.Equal(0, Timecycle.Display(0));
        Assert.Equal(1, Timecycle.Display(1), 5);
        Assert.Equal(MathF.Pow(.18f, 1 / 2.2f), Timecycle.Display(.18f), 5);
        // Brighter than the sRGB curve near black: a hundredth of white is 31 of 255, not sRGB's 25 ...
        Assert.Equal(31.4f, Timecycle.Display(.01f) * 255, 1);
        Assert.True(Timecycle.Display(.01f) > Srgb(.01f) + .02f);
        // ... and within a step of it from a tenth of white up.
        foreach (float mapped in new[] { .1f, .2f, .5f, .8f })
            Assert.True(MathF.Abs(Timecycle.Display(mapped) - Srgb(mapped)) * 255 < 1.5f, $"{mapped}");

        static float Srgb(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - .055f;
    }

    [Fact]
    public void The_bloom_starts_lower_and_is_stronger_at_night_as_the_timecycle_says()
    {
        // 13:00: a threshold of 1.7979 and a width of 1.5, so the bright pass fades in from 1.0479 over 1.5.
        Vector4 day = Timecycle.BloomParameters(Timecycle.Evaluate(13));
        AssertClose(new Vector3(1.0479f, .1f, 1 / 1.5f), new Vector3(day.X, day.Y, day.Z));
        // 22:00: 0.05 and 0.25, whose lower end would be below zero, so it starts at zero; full intensity.
        Vector4 night = Timecycle.BloomParameters(Timecycle.Evaluate(22));
        AssertClose(new Vector3(0, 1, 1 / .175f), new Vector3(night.X, night.Y, night.Z));
        // No width: it starts at the threshold, at once.
        Vector4 sharp = Timecycle.BloomParameters(Timecycle.Evaluate(13) with { BloomWidth = 0 });
        AssertClose(new Vector3(1.7979f, .1f, 1), new Vector3(sharp.X, sharp.Y, sharp.Z));
    }

    [Fact]
    public void The_bloom_chain_has_the_games_eight_levels_fewer_in_a_small_window()
    {
        Assert.Equal(8, ViewportRenderer.BloomLevelsFor(1920, 1080));
        // Half of 64 is 32: 32, 16, 8, 4, 2 and 1.
        Assert.Equal(6, ViewportRenderer.BloomLevelsFor(64, 200));
        Assert.Equal(1, ViewportRenderer.BloomLevelsFor(1, 1));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f * MathF.Max(1, expected.Length()), $"expected {expected}, got {actual}");
    }
}
