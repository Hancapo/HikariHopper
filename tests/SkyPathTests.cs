using System.Numerics;
using System.Text;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>The sun's and moon's paths, as the game's <c>time.xml</c> and its sky update set them.</summary>
public sealed class SkyPathTests
{
    private const float Degrees = MathF.PI / 180;

    private static void AssertDirection(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void The_game_sweeps_the_day_and_the_night_each_through_half_a_turn()
    {
        Assert.Equal(0, DayCycle.SunAngle(0), 5);
        Assert.Equal(MathF.PI / 4, DayCycle.SunAngle(3), 5);
        Assert.Equal(MathF.PI / 2, DayCycle.SunAngle(6), 5);
        // Noon of the game's day is 13:00, halfway from 06:00 to 20:00.
        Assert.Equal(MathF.PI, DayCycle.SunAngle(13), 5);
        Assert.Equal(3 * MathF.PI / 2, DayCycle.SunAngle(20), 5);
        Assert.Equal(7 * MathF.PI / 4, DayCycle.SunAngle(22), 5);
        Assert.Equal(DayCycle.SunAngle(1), DayCycle.SunAngle(25), 5);
        Assert.Equal(DayCycle.SunAngle(23), DayCycle.SunAngle(-1), 5);
    }

    [Fact]
    public void The_sun_rises_due_east_at_six_peaks_south_at_one_and_sets_due_west_at_eight()
    {
        AssertDirection(Vector3.UnitX, DayCycle.SunDirection(6));
        AssertDirection(-Vector3.UnitX, DayCycle.SunDirection(20));
        // At 13:00 it's as high as the roll of 122° leaves it, 58° up, to the south.
        Vector3 noon = DayCycle.SunDirection(13);
        AssertDirection(new Vector3(0, MathF.Cos(122 * Degrees), MathF.Sin(122 * Degrees)), noon);
        Assert.Equal(58, MathF.Asin(noon.Z) / Degrees, 2);
        Assert.True(noon.Y < 0, "the sun is to the south");
        // The viewer's afternoon: lower and further west than at noon.
        Vector3 afternoon = DayCycle.SunDirection(DayCycle.DefaultHours);
        Assert.Equal(53.2f, MathF.Asin(afternoon.Z) / Degrees, 1);
        Assert.Equal(213, (MathF.Atan2(afternoon.X, afternoon.Y) / Degrees + 360) % 360, 0);
    }

    [Fact]
    public void A_yaw_turns_the_suns_path_about_the_vertical()
    {
        var turned = SkyPath.Game with { SunYaw = 90 * Degrees };
        // Rising due east, turned a quarter anticlockwise: due north.
        AssertDirection(Vector3.UnitY, DayCycle.SunDirection(6, turned));
        Assert.Equal(DayCycle.SunDirection(13).Z, DayCycle.SunDirection(13, turned).Z, 5);
    }

    [Fact]
    public void The_sun_is_the_light_from_a_quarter_to_five_to_a_quarter_to_ten_and_the_moon_otherwise()
    {
        Assert.False(DayCycle.Evaluate(4.7f).Sun);
        Assert.True(DayCycle.Evaluate(4.8f).Sun);
        Assert.True(DayCycle.Evaluate(21.7f).Sun);
        Assert.False(DayCycle.Evaluate(21.8f).Sun);
        Assert.Equal(DayCycle.SunDirection(12), DayCycle.Evaluate(12).Direction);
        Assert.Equal(DayCycle.MoonDirection(2), DayCycle.Evaluate(2).Direction);
        Assert.Throws<ArgumentOutOfRangeException>(() => DayCycle.Evaluate(float.NaN));
    }

    [Fact]
    public void The_moon_is_full_opposite_the_sun_and_lifted_by_its_wobble()
    {
        // The rolls cancel, so its circle is the horizon; on the viewer's day it's half a turn from the sun, and its
        // wobble's offset lifts it 0.375 off the circle.
        foreach (float hours in new[] { 0f, 2, 23 })
        {
            float angle = DayCycle.SunAngle(hours) - MathF.PI;
            AssertDirection(Vector3.Normalize(new Vector3(MathF.Sin(angle), -MathF.Cos(angle), .375f)), DayCycle.MoonDirection(hours));
        }
        Assert.True(DayCycle.Evaluate(1).Direction.Z > 0, "the moon is up at night");
    }

    [Fact]
    public void A_time_xml_gives_the_paths_in_degrees_and_the_game_fills_what_it_leaves_out()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <time_data version="1.000000" mode="dynamic">
              <suninfo sun_roll="100" sun_yaw="15.5"/>
              <mooninfo moon_roll="-90" moon_wobble_freq="3"/>
              <sample name="00:00" hour="0" duration="4" uw_tc_mod="underwater"/>
            </time_data>
            """;
        SkyPath sky = SkyPath.Read(Encoding.UTF8.GetBytes(xml))!;
        Assert.Equal(100 * Degrees, sky.SunRoll, 5);
        Assert.Equal(15.5f * Degrees, sky.SunYaw, 5);
        Assert.Equal(-90 * Degrees, sky.MoonRoll, 5);
        Assert.Equal(3, sky.MoonWobbleFrequency);
        Assert.Equal(SkyPath.Game.MoonWobbleAmplitude, sky.MoonWobbleAmplitude);
        Assert.Equal(SkyPath.Game.MoonWobbleOffset, sky.MoonWobbleOffset);

        // No suninfo, or a value that isn't a number: the game's own.
        Assert.Equal(SkyPath.Game, SkyPath.Read("<time_data><suninfo sun_roll=\"lots\"/></time_data>"u8.ToArray()));
        Assert.Null(SkyPath.Read("not xml"u8.ToArray()));
        Assert.Null(SkyPath.Read("<!DOCTYPE x [<!ENTITY a \"b\">]><time_data/>"u8.ToArray()));
    }

}

/// <summary>The sky read from the game's own files, for the models opened from it.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class GameSkyTests : ExplorerTestBase
{
    private const string TimeXml = """<time_data mode="dynamic"><suninfo sun_roll="100" sun_yaw="0.0"/></time_data>""";

    [AvaloniaFact]
    public async Task A_model_opened_from_a_game_moves_the_sun_along_the_games_time_xml()
    {
        MakeGame();
        SaveArchive(Path.Combine(Work, "common.rpf"), ("data/levels/gta5/time.xml", TimeXml));
        File.WriteAllBytes(Path.Combine(Work, "prop_bench.ydr"), [1, 2, 3]);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        ModelFile? opened = null;
        bridge.ModelFileOpened += (_, file) => opened = file;
        bridge.ActivateEntry(Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == "prop_bench.ydr"));

        Assert.NotNull(opened?.GameSky);
        SkyPath sky = await opened.GameSky;
        Assert.Equal(100 * MathF.PI / 180, sky.SunRoll, 5);
        Assert.Equal(SkyPath.Game.MoonRoll, sky.MoonRoll);
    }

    [AvaloniaFact]
    public async Task The_games_update_takes_the_place_of_common_rpf_and_a_game_without_one_has_the_games_own_paths()
    {
        MakeGame();
        SaveArchive(Path.Combine(Work, "common.rpf"), ("data/levels/gta5/time.xml", TimeXml));
        SaveArchive(Path.Combine(Work, "update", "update.rpf"),
            ("common/data/levels/gta5/time.xml", """<time_data><suninfo sun_roll="110"/></time_data>"""));
        SkyPath updated = await ExplorerViewModel.SkyOf(FakeLoadedGame(Work));
        Assert.Equal(110 * MathF.PI / 180, updated.SunRoll, 5);

        string bare = MakeGame("bare");
        Assert.Equal(SkyPath.Game, await ExplorerViewModel.SkyOf(FakeLoadedGame(bare)));
    }

    [AvaloniaFact]
    public void The_viewer_puts_the_games_sky_on_its_viewport_once_read()
    {
        var turned = SkyPath.Game with { SunRoll = 100 * MathF.PI / 180 };
        var viewer = new Views.ModelViewer(new ModelFile("box.ybn", [1, 2, 3]) { GameSky = Task.FromResult(turned) });
        Assert.True(viewer.SkyRead.Wait(TimeSpan.FromSeconds(5)) || PumpUntil(() => viewer.SkyRead.IsCompleted));
        Assert.Equal(turned, viewer.Viewport.Sky);
        // Outside a game, the game's own paths.
        Assert.Equal(SkyPath.Game, new Views.ModelViewer(new ModelFile("box.ybn", [1, 2, 3])).Viewport.Sky);
    }

    private static bool PumpUntil(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < 5000)
            TestWait.Pump(10);
        return done();
    }
}
