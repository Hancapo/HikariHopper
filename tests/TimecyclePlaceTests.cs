using System.Numerics;
using CommonFury.Hashing;
using FiveFury.Cryptography;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's timecycle by place in its world: the URBAN region blended over GLOBAL by the code's region boxes, and the
// maps' modifier boxes applied over it, as the game works them out from the camera's position.
[Collection(ExplorerCollection.Name)]
public sealed class TimecyclePlaceTests
{
    private const string Weather = """
        <timecycle_keyframe_data version="1.000000">
          <cycle name="EXTRASUNNY" regions="2">
            <region name="GLOBAL">
              <light_dir_mult>1 2 3 4 5 6 7 8 9 10 11 12 13</light_dir_mult>
              <fog_start>10 10 10 10 10 10 10 10 10 10 10 10 10</fog_start>
              <sky_hdr>5 5 5 5 5 5 5 5 5 5 5 5 5</sky_hdr>
              <no_rain>0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5</no_rain>
              <postfx_exposure>0 0 0 0 0 0 0 0 0 0 0 0 0</postfx_exposure>
            </region>
            <region name="URBAN">
              <light_dir_mult>9 9 9 9 9 9 9 9 9 9 9 9 9</light_dir_mult>
              <fog_start>20 20 20 20 20 20 20 20 20 20 20 20 20</fog_start>
              <sky_hdr>7 7 7 7 7 7 7 7 7 7 7 7 7</sky_hdr>
              <no_rain>0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5</no_rain>
              <postfx_exposure>1 1 1 1 1 1 1 1 1 1 1 1 1</postfx_exposure>
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    private const string Modifiers = """
        <timecycle_modifier_data version="1.000000">
          <modifier name="tunnel" numMods="5" userFlags="0">
            <light_dir_mult>2.000 0.500</light_dir_mult>
            <fog_start>15.000 18.000</fog_start>
            <sky_hdr>100.000 0.000</sky_hdr>
            <no_rain>1.000 0.000</no_rain>
            <postfx_exposure>3.000 0.000</postfx_exposure>
          </modifier>
          <modifier name="zone" numMods="1" userFlags="0">
            <postfx_exposure>10.000 0.000</postfx_exposure>
          </modifier>
          <modifier name="StreetLighting" numMods="1" userFlags="0">
            <postfx_exposure>5.000 0.000</postfx_exposure>
          </modifier>
          <modifier name="underwater" numMods="1" userFlags="1">
            <postfx_exposure>5.000 0.000</postfx_exposure>
          </modifier>
          <modifier name="v_room" numMods="1" userFlags="2">
            <postfx_exposure>5.000 0.000</postfx_exposure>
          </modifier>
        </timecycle_modifier_data>
        """;

    private static readonly GameTimecycle Game = GameTimecycle.From(Weather, GameTimecycleTests.Read(Modifiers));

    private static GameTimecycleModifier Modifier(string name) => Game.Modifiers[new Hash32(name.ToLowerInvariant()).Value];

    private static TimecyclePlace Place(float region, params (string Name, float Strength)[] modifiers)
    {
        var place = new TimecyclePlace { Region = region };
        foreach (var (name, strength) in modifiers)
            place.Modifiers.Add((Modifier(name), strength));
        return place;
    }

    private static T WithGame<T>(Func<T> read)
    {
        try
        {
            Timecycle.Use(Game);
            return read();
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    [Fact]
    public void The_game_s_names_hash_to_the_codes_region_and_skipped_modifiers()
    {
        Assert.Equal(0x7d7b904fu, new Hash32("urban").Value);
        Assert.Equal(0xA502C509u, new Hash32("streetlighting").Value);
        Assert.Equal(0x77F1BF19u, new Hash32("streetlightingtraffic").Value);
        Assert.Equal(0x2D909E01u, new Hash32("streetlightingjunction").Value);
    }

    [Fact]
    public void The_urban_region_blends_over_global_by_its_strength()
    {
        var frame = new TimecycleFrame();
        // 13:00 is the sixth keyframe: GLOBAL's 6, URBAN's 9.
        Assert.Equal(6, WithGame(() => Timecycle.Resolve(13, Place(0), frame).Get("light_dir_mult")));
        Assert.Equal(7.5f, WithGame(() => Timecycle.Resolve(13, Place(.5f), frame).Get("light_dir_mult")));
        Assert.Equal(9, WithGame(() => Timecycle.Resolve(13, Place(1), frame).Get("light_dir_mult")));
        Assert.Equal(6, WithGame(() => Timecycle.Resolve(13, Place(.5f), frame).Get("sky_hdr")));
        // No place is GLOBAL's, as before.
        Assert.Equal(6, WithGame(() => Timecycle.Resolve(13, null, frame).Get("light_dir_mult")));
        Assert.Equal(6, WithGame(() => Timecycle.Sample(13).Get("light_dir_mult")));
    }

    [Fact]
    public void A_modifier_changes_each_field_as_the_games_variable_table_says()
    {
        var frame = new TimecycleFrame();
        TimecycleSample tc = WithGame(() => Timecycle.Resolve(13, Place(0, ("tunnel", .5f)), frame));
        // Interpolated by strength times the second value: .25 of the way from 6 to 2.
        Assert.Equal(5, tc.Get("light_dir_mult"), 4);
        // Clamped between 15 and 18, halfway there from 10.
        Assert.Equal(12.5f, tc.Get("fog_start"), 4);
        // The sky's fields aren't modified.
        Assert.Equal(5, tc.Get("sky_hdr"));
        // The larger of the weather's by what's left and the modifier's by its strength.
        Assert.Equal(.5f, tc.Get("no_rain"), 4);
        // Interpolated.
        Assert.Equal(1.5f, tc.Get("postfx_exposure"), 4);
        // Each in turn, over the region's blend.
        tc = WithGame(() => Timecycle.Resolve(13, Place(1, ("tunnel", .5f), ("zone", .5f)), frame));
        Assert.Equal((.5f * (.5f * 1 + .5f * 3) + .5f * 10), tc.Get("postfx_exposure"), 4);
        Assert.Equal(.75f * 9 + .25f * 2, tc.Get("light_dir_mult"), 4);
    }

    [Fact]
    public void A_light_and_sky_are_taken_from_the_sample_at_the_place()
    {
        var frame = new TimecycleFrame();
        TimecycleLight global = WithGame(() => Timecycle.Evaluate(13)), urban = WithGame(() => Timecycle.Evaluate(Timecycle.Resolve(13, Place(1), frame)));
        Assert.Equal(1.5f, urban.Directional.X / global.Directional.X, 3);
        Assert.Equal(1, urban.Exposure);
        SkySystem sky = WithGame(() => SkySystem.At(13, timecycle: Timecycle.Resolve(13, Place(1), frame)));
        Assert.Equal(7, sky.HdrIntensity);
    }

    [Fact]
    public void The_camera_s_place_takes_the_urban_boxes_and_the_maps_modifier_boxes_as_the_game_does()
    {
        uint tunnel = new Hash32("tunnel").Value, zone = new Hash32("zone").Value;
        var zones = new TimecycleZones(Game,
        [
            new TimecycleBox(tunnel, new(0, 0, 0), new(10, 10, 10), 50, 20, 0, 0),
            new TimecycleBox(tunnel, new(100, 100, 0), new(110, 110, 10), 100, 20, 0, 0),
            // Out from 20:00 to the end of 22:00.
            new TimecycleBox(zone, new(0, 0, 0), new(10, 10, 10), 100, 20, 20, 22),
            new TimecycleBox(new Hash32("streetlighting").Value, new(0, 0, 0), new(10, 10, 10), 100, 20, 0, 0),
            new TimecycleBox(new Hash32("underwater").Value, new(0, 0, 0), new(10, 10, 10), 100, 20, 0, 0),
            new TimecycleBox(new Hash32("v_room").Value, new(0, 0, 0), new(10, 10, 10), 100, 20, 0, 0),
            new TimecycleBox(new Hash32("unknown").Value, new(0, 0, 0), new(10, 10, 10), 100, 20, 0, 0),
        ]);
        Assert.Equal(3, zones.ModifierBoxes);
        var place = new TimecyclePlace();
        // Inside the first box, at noon: the tunnel at its box's half, inside Los Santos' first URBAN box.
        zones.Evaluate(new Vector3(5, 5, 5), 12, place);
        Assert.Equal(1, place.Region);
        Assert.Equal((Modifier("tunnel"), .5f), Assert.Single(place.Modifiers));
        // Five metres out of it, a quarter of the way out over its range.
        zones.Evaluate(new Vector3(15, 5, 5), 12, place);
        Assert.Equal(.375f, Assert.Single(place.Modifiers).Strength, 4);
        // Ten metres over it counts thrice: out of its range.
        zones.Evaluate(new Vector3(5, 5, 20), 12, place);
        Assert.Empty(place.Modifiers);
        // The second box is the tunnel's strongest there.
        zones.Evaluate(new Vector3(105, 105, 5), 12, place);
        Assert.Equal(1, Assert.Single(place.Modifiers).Strength);
        // In its hours, the zone too, after the tunnel as the game orders them; half an hour before, half of it.
        zones.Evaluate(new Vector3(5, 5, 5), 21, place);
        Assert.Equal([Modifier("tunnel"), Modifier("zone")], place.Modifiers.Select(pair => pair.Modifier));
        zones.Evaluate(new Vector3(5, 5, 5), 19.5f, place);
        Assert.Equal(.5f, place.Modifiers[1].Strength, 4);
        zones.Evaluate(new Vector3(5, 5, 5), 23.25f, place);
        Assert.Equal(.75f, place.Modifiers[1].Strength, 4);
        zones.Evaluate(new Vector3(5, 5, 5), 12, place);
        Assert.Single(place.Modifiers);
        // The URBAN boxes fade out over 750 m: 375 m east of the first box, half; 100 m over it, thrice as fast.
        zones.Evaluate(new Vector3(1264.279f + 375, -3000, 100), 12, place);
        Assert.Equal(.5f, place.Region, 3);
        zones.Evaluate(new Vector3(0, -1000, 600), 12, place);
        Assert.Equal(.6f, place.Region, 3);
        zones.Evaluate(new Vector3(5000, 5000, 0), 12, place);
        Assert.Equal(0, place.Region);
        Assert.True(place.IsGlobal);
    }

    [Fact]
    public void A_box_s_hours_fade_in_over_the_hour_before_and_out_over_the_last()
    {
        Assert.Equal(1, TimecycleZones.TimeStrength(0, 0, 13, .5f));
        Assert.Equal(.25f, TimecycleZones.TimeStrength(20, 23, 19, .25f));
        Assert.Equal(1, TimecycleZones.TimeStrength(20, 23, 21, .25f));
        Assert.Equal(.75f, TimecycleZones.TimeStrength(20, 23, 23, .25f));
        Assert.Equal(0, TimecycleZones.TimeStrength(20, 23, 12, .25f));
        // Over midnight.
        Assert.Equal(1, TimecycleZones.TimeStrength(22, 4, 1, 0));
        Assert.Equal(0, TimecycleZones.TimeStrength(22, 4, 12, 0));
    }

    [Fact]
    public void Without_an_urban_region_the_region_boxes_do_nothing()
    {
        var global = GameTimecycle.From(GameTimecycle.ParseWeather(Weather), []);
        var place = new TimecyclePlace();
        new TimecycleZones(global, []).Evaluate(new Vector3(5, 5, 5), 12, place);
        Assert.True(place.IsGlobal);
    }

    [Fact]
    public void The_view_takes_the_camera_s_place_and_drops_it_outside_a_world()
    {
        GpuDevice gpu = GpuDevice.Shared;
        try
        {
            Timecycle.Use(Game);
            lock (gpu.Gate)
            {
                var renderer = new ViewportRenderer(gpu);
                WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
                {
                    Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                    Dimension = WebGpuSharp.TextureDimension.D2,
                    Size = new WebGpuSharp.Extent3D(64, 32, 1),
                    Format = renderer.PresentFormat,
                    MipLevelCount = 1,
                    SampleCount = 1,
                });
                gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
                renderer.Resize(64, 32);
                renderer.WorldShown = true;
                renderer.SetTime(13);
                Assert.Equal(6, renderer.TimecycleSample.Get("light_dir_mult"));
                renderer.SetTimecyclePlace(Place(1, ("tunnel", 1)));
                Assert.Equal(.5f * 9 + .5f * 2, renderer.TimecycleSample.Get("light_dir_mult"), 4);
                // Another hour keeps the place.
                renderer.SetTime(13.5f);
                Assert.Equal(.5f * 9 + .5f * 2, renderer.TimecycleSample.Get("light_dir_mult"), 4);
                renderer.RenderTo(target.CreateView());
                renderer.WorldShown = false;
                Assert.Equal(6 + .5f / 3, renderer.TimecycleSample.Get("light_dir_mult"), 4);
                var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
                Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
                renderer.Dispose();
                target.Destroy();
            }
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    [Fact]
    public void The_game_s_clear_weather_has_its_urban_region_lighting_the_city_differently()
    {
        string root = TestGames.Legacy();
        var keys = new Rpf7Keys(GameKeys.Load(Path.Combine(root, "GTA5.exe")));
        GameTimecycle game = GameTimecycle.ForGame(new LoadedGame(root, FiveFury.GameTarget.Legacy, keys.Crypto))!;
        Assert.Equal(["GLOBAL", "URBAN"], game.Regions.Select(region => region.Name));
        try
        {
            Timecycle.Use(game);
            var frame = new TimecycleFrame();
            // 13:00: the city's natural ambient from above is half the open country's, and so is its bloom.
            TimecycleSample city = Timecycle.Resolve(13, Place(1), frame);
            Assert.Equal(2.5f, city.Get("light_natural_amb_down_intensity"));
            Assert.Equal(5, Timecycle.Sample(13).Get("light_natural_amb_down_intensity"));
            Assert.Equal(.05f, city.Get("postfx_intensity_bloom"), 3);
            // Its zones: Legion Square is in the city, the desert out of it.
            var zones = new TimecycleZones(game, []);
            var place = new TimecyclePlace();
            zones.Evaluate(new Vector3(195, -935, 32), 13, place);
            Assert.Equal(1, place.Region);
            zones.Evaluate(new Vector3(1700, 3600, 35), 13, place);
            Assert.Equal(0, place.Region);
        }
        finally
        {
            Timecycle.Use(null);
        }
    }
}
