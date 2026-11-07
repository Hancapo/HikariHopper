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
        Assert.Equal(new Vector3(Atmosphere.SunIlluminance), key.Illuminance);
    }

    [Fact]
    public void At_night_the_moon_lights_drawables_far_more_dimly()
    {
        KeyLight night = DayCycle.Evaluate(1);
        Assert.False(night.Sun);
        Assert.True(night.Direction.Z > 0, "the moon is up when the sun is down");
        Assert.True(night.Illuminance.Length() < new Vector3(Atmosphere.SunIlluminance).Length() / 100);
        // The hour wraps.
        Assert.Equal(DayCycle.SunDirection(14), DayCycle.SunDirection(38));
    }

    [Fact]
    public void Sunlight_reaching_the_ground_is_dimmer_and_redder_near_the_horizon()
    {
        Vector3 overhead = Atmosphere.SunTransmittance(2, Vector3.UnitZ);
        Vector3 low = Atmosphere.SunTransmittance(2, Vector3.Normalize(new Vector3(1, 0, .05f)));
        Assert.All(new[] { overhead.X, overhead.Y, overhead.Z }, value => Assert.InRange(value, .5f, 1));
        Assert.True(low.Length() < overhead.Length());
        // Blue scatters out first.
        Assert.True(low.X / low.Z > overhead.X / overhead.Z);
        // Below the horizon the planet blocks it.
        Assert.Equal(Vector3.Zero, Atmosphere.SunTransmittance(2, Vector3.Normalize(new Vector3(1, 0, -.2f))));
    }
}
