using System.Numerics;
using CommonFury.Hashing;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;

namespace HikariHopper.Tests;

// GTA V's timecycle as the game's files have it, and the interior rooms' ambient made from it.
[Collection(ExplorerCollection.Name)]
public sealed class GameTimecycleTests
{
    private const string Weather = """
        <timecycle_keyframe_data version="1.000000">
          <cycle name="EXTRASUNNY" regions="2">
            <region name="GLOBAL">
              <light_dir_mult>1 2 3 4 5 6 7 8 9 10 11 12 13</light_dir_mult>
              <light_artificial_ext_down_col_r> 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000 0.5000</light_artificial_ext_down_col_r>
              <light_artificial_ext_down_intensity>2 2 2 2 2 2 2 2 2 2 2 2 2</light_artificial_ext_down_intensity>
              <light_artificial_int_down_intensity>0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5</light_artificial_int_down_intensity>
            </region>
            <region name="URBAN">
              <light_dir_mult>9 9 9 9 9 9 9 9 9 9 9 9 9</light_dir_mult>
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    private const string Modifiers = """
        <timecycle_modifier_data version="1.000000">
          <modifier name="V_Room" numMods="2" userFlags="0">
            <light_artificial_int_down_col_r>0.800 1.000</light_artificial_int_down_col_r>
            <artificial_int_ambient_multiplier>0.500 0.000</artificial_int_ambient_multiplier>
          </modifier>
          <modifier name="v_house" numMods="2" userFlags="0">
            <light_artificial_int_down_intensity>2.000 0.000</light_artificial_int_down_intensity>
            <natural_ambient_multiplier>0.200 0.000</natural_ambient_multiplier>
          </modifier>
        </timecycle_modifier_data>
        """;

    [Fact]
    public void The_weathers_global_region_and_each_modifiers_first_values_are_read()
    {
        Dictionary<string, float[]> weather = GameTimecycle.ParseWeather(Weather);
        Assert.Equal(Enumerable.Range(1, 13).Select(i => (float)i), weather["light_dir_mult"]);
        Assert.Equal(.5f, weather["light_artificial_ext_down_col_r"][4]);
        var modifiers = GameTimecycle.ParseModifiers(Modifiers).ToDictionary(m => m.Name, m => m.Fields);
        Assert.Equal(.8f, modifiers["V_Room"]["light_artificial_int_down_col_r"]);
        Assert.Equal(.5f, modifiers["V_Room"]["artificial_int_ambient_multiplier"]);
        Assert.Equal(2, modifiers["v_house"].Count);
    }

    [Fact]
    public void A_rooms_ambient_takes_its_primary_modifier_over_its_secondary_over_the_weather()
    {
        var timecycle = GameTimecycle.From(GameTimecycle.ParseWeather(Weather),
            GameTimecycle.ParseModifiers(Modifiers).ToDictionary(m => new Hash32(m.Name.ToLowerInvariant()).Value, m => m.Fields));
        InteriorAmbient room = InteriorAmbient.For(new Hash32("v_room").Value, new Hash32("v_house").Value, timecycle);
        // Red: the room's .8, by the house's intensity 2, by the room's multiplier .5; the weather has no green.
        Assert.Equal(.8f, room.Down.X, 3);
        Assert.Equal(0, room.Down.Y);
        Assert.Equal(.2f, room.Natural, 3);
        // Without modifiers: the weather's, whose interior multiplier it doesn't have.
        Assert.Equal(Vector3.Zero, InteriorAmbient.For(0, 0, timecycle).Down);
    }

    [Fact]
    public void A_packed_ambient_keeps_a_byte_a_channel_over_its_range()
    {
        float packed = InteriorAmbient.Pack(new Vector3(4, 2, 0));
        uint bits = (uint)packed;
        Assert.Equal(255u, bits & 255);
        Assert.Equal(128u, (bits >> 8) & 255);
        Assert.Equal(0u, bits >> 16);
        Assert.Equal(255u * 65536, (uint)InteriorAmbient.Pack(new Vector3(0, 0, 99)));
    }

    [Fact]
    public void The_game_weather_replaces_the_kept_copy_field_by_field_and_can_be_put_back()
    {
        float kept = Timecycle.Evaluate(13).Directional.X;
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather(Weather), []));
            TimecycleLight light = Timecycle.Evaluate(13);
            // light_dir_mult's sixth keyframe (13:00) is 6 here; its colour stays the kept copy's.
            Assert.Equal(6, light.Directional.X / Timecycle.Sample(13).Colour("light_dir_col").X, 3);
            Assert.Equal(1, light.ArtificialDown.X);
        }
        finally
        {
            Timecycle.Use(null);
        }
        Assert.Equal(kept, Timecycle.Evaluate(13).Directional.X);
        Assert.Equal(Vector3.Zero, Timecycle.Evaluate(13).ArtificialDown);
    }
}
