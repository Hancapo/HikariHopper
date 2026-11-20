using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class Rdr2GradingTests
{
    private static Vector3 At(float[] table, int x, int y)
    {
        int at = (y * Rdr2Grading.Width + x) * 4;
        return new Vector3(table[at], table[at + 1], table[at + 2]);
    }

    [Fact]
    public void A_table_is_laid_out_as_the_games_red_across_each_slice_green_down_blue_by_slice()
    {
        Assert.Equal(Vector3.Zero, Rdr2Grading.IdentityAt(0, 0));
        Assert.Equal(new Vector3(1, 0, 0), Rdr2Grading.IdentityAt(15, 0));
        Assert.Equal(new Vector3(0, 1, 1), Rdr2Grading.IdentityAt(240, 15));
    }

    [Fact]
    public void Neutral_levels_leave_the_colour_as_it_is()
    {
        float[] table = Rdr2Grading.Levels(new Vector4(1, 1, 1, .0001f), new Vector4(1, 1, 1, .1277f), 1);
        for (int x = 0; x < Rdr2Grading.Width; x += 17)
            for (int y = 0; y < Rdr2Grading.Height; y += 5)
            {
                Vector3 expected = Rdr2Grading.IdentityAt(x, y), got = At(table, x, y);
                Assert.True(Vector3.Distance(expected, got) < 1e-5f, $"{x},{y}: {got} for {expected}");
            }
    }

    [Fact]
    public void The_levels_desaturate_and_tint_the_shadows_by_the_shift_and_the_rest_by_the_correction()
    {
        // Desaturated all the way: each colour its luminance.
        float[] grey = Rdr2Grading.Levels(new Vector4(1, 1, 1, .0001f), new Vector4(1, 1, 1, .1f), 0);
        Vector3 red = At(grey, 15, 0);
        Assert.Equal(.2125f, red.X, 5);
        Assert.Equal(red.X, red.Y, 5);
        // Shadows (luminance at nought) take the shift's colour; bright colours the correction's.
        float[] tinted = Rdr2Grading.Levels(new Vector4(1, .5f, 1, .0001f), new Vector4(.5f, 1, 1, .5f), 1);
        Vector3 dark = At(tinted, 1, 1), bright = At(tinted, 15 * 16 + 14, 14);
        Assert.True(dark.X < Rdr2Grading.IdentityAt(1, 1).X, $"{dark}");
        Assert.True(bright.Y < Rdr2Grading.IdentityAt(15 * 16 + 14, 14).Y, $"{bright}");
        // With no cut-off, white takes the correction too, all but a hundredth of the way.
        Assert.Equal(.505f, At(tinted, 255, 15).Y, 4);
        // Within the correction's cut-off below white, it's left alone.
        float[] cut = Rdr2Grading.Levels(new Vector4(1, .5f, 1, .5f), new Vector4(1, 1, 1, .1f), 1);
        Assert.True(Vector3.Distance(Vector3.One, At(cut, 255, 15)) < 1e-5f, $"{At(cut, 255, 15)}");
    }

    [Fact]
    public void The_weather_tables_are_weighed_by_the_climate_from_its_base_bands_out()
    {
        Vector4 humidity = new(0, .49f, .51f, 1), temperature = new(-10, 14, 16, 35);
        // In both base bands: the middle table alone, by the strength.
        float[] middle = Rdr2Grading.WeatherWeights(.5f, 15, humidity, temperature, .75f);
        Assert.Equal(.75f, middle[4], 6);
        Assert.Equal(.75f, middle.Sum(), 6);
        // At its wettest and hottest: the hot, humid corner, [high + 3 high].
        float[] corner = Rdr2Grading.WeatherWeights(1, 35, humidity, temperature, 1);
        Assert.Equal(1, corner[8], 6);
        // Halfway to the coldest: the middle and the cold, middling table, half each.
        float[] cold = Rdr2Grading.WeatherWeights(.5f, 2, humidity, temperature, 1);
        Assert.Equal(.5f, cold[4], 5);
        Assert.Equal(.5f, cold[3], 5);
    }

    [Fact]
    public void The_weather_table_blends_its_tables_the_rest_of_the_way_the_identity()
    {
        var white = new float[Rdr2Grading.Width * Rdr2Grading.Height * 4];
        Array.Fill(white, 1f);
        float[]?[] tables = new float[]?[9];
        tables[4] = white;
        Rdr2Grading grading = Rdr2Grading.Of(tables);
        float[] weights = new float[9];
        weights[4] = .25f;
        // A quarter of the way from black to white.
        Assert.Equal(new Vector3(.25f), At(grading.Weather(weights), 0, 0));
        // Weights over one are taken over their sum.
        weights[4] = 4;
        Assert.Equal(Vector3.One, At(grading.Weather(weights), 0, 0));
    }

    [Fact]
    public void The_games_nine_weather_tables_are_read_from_its_files()
    {
        var sources = RedFury.World.WorldGameSource.ForGame(TestGames.Rdr2(), HikariHopper.Core.Rdr2Keys.Bundled.Crypto);
        Rdr2Grading grading = Rdr2Grading.Read(sources, HikariHopper.Core.Rdr2Keys.Bundled.Crypto) ?? throw new InvalidOperationException("no tables");
        Assert.True(grading.HasWeather);
        // The middle table, all weights on it, is the game's grading: not the identity, in range.
        float[] weights = new float[9];
        weights[4] = 1;
        float[] table = grading.Weather(weights);
        Assert.NotEqual(Rdr2Grading.IdentityAt(100, 8), At(table, 100, 8));
        Assert.All(table, value => Assert.InRange(value, 0, 1));
    }
}
