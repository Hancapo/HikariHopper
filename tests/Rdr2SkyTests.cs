using System.Globalization;
using System.Numerics;
using System.Text;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// RDR2's sky behind its world: its air worked out from the weather and visualsettings.dat as the game works it out, and a
// frame with it, its tables built once for each air.
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2SkyTests
{
    // A weather with every keyframe the same but where a field is given as several.
    private static string Weather(params (string Field, float[] Values)[] fields) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <timecycle_keyframe_data version="2.000000">
          <cycle name="HIGHPRESSURE" description="" regions="2">
            <region name="GLOBAL">
        {string.Join("\n", fields.Select(f => $"      <{f.Field}>{string.Join(' ', (f.Values.Length == 1 ? Enumerable.Repeat(f.Values[0], 13) : f.Values).Select(v => v.ToString(CultureInfo.InvariantCulture)))}</{f.Field}>"))}
            </region>
            <region name="URBAN">
              <sky_scattering_red_wavelength>1 1 1 1 1 1 1 1 1 1 1 1 1</sky_scattering_red_wavelength>
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    private const string Visuals = """
        stars.dense.intensityGalaxy 3.0
        stars.dense.color.red 		0.549
        stars.dense.color.green 	0.627
        stars.dense.color.blue 		1.0
        #Config for static values for skyhat
        sky.MultipleScattering 0.5
        sky.SunIntensity 100000.0
        sky.MoonIntensity 1.0
        sky.AltitudeRemapFactor 1.0
        sky.AtmosphereThickness 8.0
        """;

    private static Rdr2Sky Sky(params (string Field, float[] Values)[] fields)
    {
        var files = new Dictionary<string, byte[]>
        {
            [Rdr2Sky.Weather] = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Weather(fields))],
            [Rdr2Sky.VisualSettingsPath] = Encoding.UTF8.GetBytes(Visuals),
        };
        return Rdr2Sky.Read(files.GetValueOrDefault) ?? throw new InvalidOperationException("no sky");
    }

    [Fact]
    public void The_air_is_worked_out_from_the_weather_as_the_game_works_it_out()
    {
        Rdr2Sky sky = Sky(("sky_scattering_red_wavelength", [680]), ("sky_scattering_green_wavelength", [550]), ("sky_scattering_blue_wavelength", [440]),
            ("sky_scattering_molecule_desnity_scaler", [1.5f]), ("sky_scattering_aerosol_density_scale", [.5f]), ("sky_scattering_aerosol_absorption_scale", [.25f]),
            ("sky_scattering_aerosol_height", [1.2f]), ("sky_scattering_ozone_concentration", [.3f]), ("sky_scattering_mie_scatter", [.9f]),
            ("sky_scattering_mie_intensity", [.75f]), ("sky_scattering_sun_intensity_multiplier", [.5f]), ("sky_scattering_moon_intensity_multiplier", [.2f]),
            ("sky_scattering_sky_col_r", [.5f]), ("sky_scattering_sun_col_b", [.25f]), ("sky_scattering_multiple_scattering_intensity", [.8f]));
        var values = new Vector4[Rdr2Sky.Vectors];
        sky.Write(values, 12, Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        // The Rayleigh scattering a metre over the wavelength's fourth power and the molecules; the aerosols' extinction.
        Assert.Equal(1169939.4332487835 / (Math.Pow(680, 4) * 1.5), values[1].X, 1e-12);
        Assert.Equal(1169939.4332487835 / (Math.Pow(440, 4) * 1.5), values[1].Z, 1e-12);
        Assert.True(values[1].Z > values[1].Y && values[1].Y > values[1].X);
        Assert.Equal(.5f * 4e-6f * 1.25f, values[1].W, 1e-12);
        Assert.Equal(Rdr2Sky.OzoneAt(550) * .3 * 3.054e15, values[2].Y, 1e-9);
        // The scale heights in metres, the altitude bias the game's default.
        Assert.Equal(new Vector4(1, 8000, 1200, 10), values[0]);
        // The sun's light by its intensity, and the sky clamped to the sun's and the moon's.
        Assert.Equal(new Vector3(.5f, 1, 1) * 50000, new Vector3(values[3].X, values[3].Y, values[3].Z));
        Assert.Equal(50000.2f, values[3].W, 2);
        Assert.Equal(.25f * 50000, values[4].Z);
        // Cornette-Shanks for the Mie scattering, with its intensity.
        Assert.Equal(3 / (8 * MathF.PI) * (1 - .81f) / (2 + .81f), values[5].X, 6);
        Assert.Equal(new Vector3(-1.8f, 1.81f, .75f), new Vector3(values[5].Y, values[5].Z, values[5].W));
        Assert.Equal(Vector3.UnitZ, new Vector3(values[6].X, values[6].Y, values[6].Z));
        // Each later order by the timecycle's intensity times visualsettings.dat's.
        Assert.Equal(.4f, values[8].X, 6);
        // In the game's own units: nothing scales it.
        Assert.Equal(1, values[7].W);
    }

    [Fact]
    public void Fields_follow_the_keyframes_and_only_the_air_rebuilds_the_tables()
    {
        Rdr2Sky sky = Sky(("sky_scattering_sun_intensity_multiplier", [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1]),
            ("sky_scattering_aerosol_density_scale", [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2]));
        Vector4[] noon = new Vector4[Rdr2Sky.Vectors], night = new Vector4[Rdr2Sky.Vectors], late = new Vector4[Rdr2Sky.Vectors];
        sky.Write(noon, 14, Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        sky.Write(night, 2, -Vector3.UnitZ, Vector3.UnitZ, true, Vector3.Zero, 0);
        sky.Write(late, 23, -Vector3.UnitZ, Vector3.UnitZ, true, Vector3.Zero, 0);
        Assert.Equal(100000, noon[3].X);
        Assert.Equal(0, night[3].X);
        Assert.True(Rdr2Sky.SameTables(noon, night));
        Assert.False(Rdr2Sky.SameTables(noon, late));
        Assert.True(late[1].W > noon[1].W);
    }

    [Fact]
    public void Ozone_cross_sections_are_the_games_between_its_nanometres()
    {
        double below = Rdr2Sky.OzoneAt(550), above = Rdr2Sky.OzoneAt(551);
        Assert.InRange(below, 1e-21, 1e-20);
        Assert.Equal((below + above) / 2, Rdr2Sky.OzoneAt(550.5f), 1e-30);
        Assert.Equal(Rdr2Sky.OzoneAt(214), Rdr2Sky.OzoneAt(100));
    }

    [Fact]
    public void A_dome_that_fails_to_read_leaves_the_sky_on_its_sphere()
    {
        var files = new Dictionary<string, byte[]>
        {
            [Rdr2Sky.Weather] = Encoding.UTF8.GetBytes(Weather()),
            [Rdr2Sky.VisualSettingsPath] = Encoding.UTF8.GetBytes(Visuals),
        };
        Rdr2Sky? sky = Rdr2Sky.Read(files.GetValueOrDefault, () => throw new InvalidOperationException("a broken dome"));
        Assert.NotNull(sky);
        Assert.Null(sky.Dome);
        // And the weather's colour tables, without grading by weather.
        Rdr2Sky? ungraded = Rdr2Sky.Read(files.GetValueOrDefault, null, () => throw new InvalidOperationException("broken tables"));
        Assert.NotNull(ungraded);
        Assert.Null(ungraded.Grading);
    }

    [Fact]
    public void The_world_is_exposed_by_the_games_curve_its_tweak_and_its_range()
    {
        Rdr2Sky sky = Sky(("postfx_exposure", [-1]), ("postfx_exposure_min", [-11]), ("postfx_exposure_max", [32]),
            ("postfx_exposure_adaption_step_mult", [.6475f]));
        Rdr2Exposure exposure = sky.Exposure(12);
        // scale ln(L) + vertical offset - 10, the game's defaults (-1.15 and 6.9), plus the tweak.
        Assert.Equal(-1.15f * MathF.Log(1) + 6.9f - 10 - 1, exposure.TargetFor(1, 1), 4);
        Assert.Equal(-1.15f * MathF.Log(100) + 6.9f - 10 - 1, exposure.TargetFor(100, 100), 4);
        // Kept in the timecycle's range.
        Assert.Equal(-11, exposure.TargetFor(1e6f, 1e6f));
        Assert.Equal(.6475f, exposure.StepMult, 5);
        // The filmic curves the file has.
        Assert.Equal(.22f, exposure.ToneMap.Bright.A, 5);
        // Metered over the whole frame where the weather has no spot meter, nothing left out, from nought.
        Assert.Equal(new Vector4(0, 0, 1, 1), exposure.SpotMeter);
        Assert.Equal(0, exposure.CubemapAmount);
        // The aerial perspective out to visualsettings.dat's far clip (the game's default where it hasn't it).
        Assert.Equal(10000, sky.AerialFarClip);
    }

    [Fact]
    public void The_height_fog_is_the_local_atmospheres_from_the_timecycle()
    {
        Rdr2Sky sky = Sky(("local_atmosphere_fog_density", [.0005f]), ("local_atmosphere_fog_height", [100]), ("local_atmosphere_fog_falloff", [500]),
            ("local_atmosphere_anisotropy_high_density", [.9f]), ("local_atmosphere_sky_col_r", [.5f]), ("local_atmosphere_sky_intensity", [2]),
            ("local_atmosphere_fill_ambient_light_color_b", [.2f]), ("local_atmosphere_fill_ambient_light_intensity", [1]),
            ("local_atmosphere_fog_shadow_amount", [.3f]));
        var values = new Vector4[Rdr2Sky.Vectors];
        sky.Write(values, 12, Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        // Its density, the height it's thickest below, one over its falloff, its anisotropy.
        Assert.Equal(new Vector4(.0005f, 100, 1 / 500f, .9f), values[Rdr2Sky.Fog]);
        // The sky's ambient through it by its colour and intensity; its fill over a thousand, as the air glow's.
        Assert.Equal(new Vector3(1, 2, 2), new Vector3(values[Rdr2Sky.Fog + 2].X, values[Rdr2Sky.Fog + 2].Y, values[Rdr2Sky.Fog + 2].Z));
        Assert.Equal(.2f / 1000, values[Rdr2Sky.Fog + 3].Z, 7);
        // How much the sun's shadow in it is lifted.
        Assert.Equal(.3f, values[Rdr2Sky.Fog + 1].W, 6);
    }

    [Fact]
    public void The_exposure_is_metered_over_the_spot_meter_by_the_middle_of_its_histogram_and_the_cube()
    {
        Rdr2Sky sky = Sky(("postfx_exposure_spot_meter_x", [.5f]), ("postfx_exposure_spot_meter_y", [0]), ("postfx_exposure_spot_meter_width", [1]),
            ("postfx_exposure_spot_meter_height", [2]), ("postfx_exposure_lowlights_percent", [.3f]), ("postfx_exposure_highlights_percent", [.8f]),
            ("postfx_exposure_cubemap_amount", [.5f]));
        Rdr2Exposure exposure = sky.Exposure(12);
        // Its middle and size over the frame (-1 to 1) as a span of it (0 to 1).
        Assert.Equal(new Vector4(.5f, 0, 1, 1), exposure.SpotMeter);
        // The lowlights as they are, the highlights at most 0.99 less them.
        Assert.Equal(.3f, exposure.Lowlights, 6);
        Assert.Equal(.69f, exposure.Highlights, 6);
        Assert.Equal(1e-6f, exposure.AbsoluteMin, 9);
        // Halfway from the frame's curve to the cube's.
        float frame = -1.15f * MathF.Log(10) + 6.9f - 10, cube = -1.15f * MathF.Log(1000) + 6.9f - 10;
        Assert.Equal((frame + cube) / 2, exposure.TargetFor(10, 1000), 4);
    }

    [Fact]
    public void The_frame_is_graded_by_the_timecycles_correction_shift_desaturation_and_scotopic_shift()
    {
        Rdr2Sky sky = Sky(("postfx_correct_col_r", [.9f]), ("postfx_correct_cutoff", [0]), ("postfx_shift_col_b", [.8f]), ("postfx_shift_cutoff", [.1277f]),
            ("postfx_desaturation", [.8f]), ("postfx_weather_color_correction", [1]), ("postfx_scotopic_col_r", [.37f]),
            ("postfx_scotopic_blue_shift_intensity", [.7f]), ("postfx_scotopic_weight_scale", [.3f]), ("postfx_scotopic_weight_bias", [-.15f]));
        Rdr2GradeValues grade = sky.GradeAt(12);
        // The correction's cut-off at least 0.0001, as the game takes it.
        Assert.Equal(new Vector4(.9f, 1, 1, .0001f), grade.Correct);
        Assert.Equal(new Vector4(1, 1, .8f, .1277f), grade.Shift);
        Assert.Equal(.8f, grade.Desaturation, 6);
        // The viewer's climate is the middle of the game's base bands: the middle weather table alone.
        Assert.Equal(1, grade.WeatherWeights[4], 6);
        Assert.Equal(1, grade.WeatherWeights.Sum(), 6);
        Assert.Equal(new Vector4(.37f, 1, 1, .7f), grade.Scotopic);
        Assert.Equal(new Vector2(.3f, -.15f), grade.ScotopicWeight);
        // No gradient filter where the weather sets none: white bands, the midpoints at the middle.
        Assert.Equal(new Rdr2Gradient(new Vector4(1, 1, 1, 0), new Vector4(1, 1, 1, .5f), new Vector4(1, 1, 1, 0), new Vector2(2, 2)), grade.Gradient);
    }

    [Fact]
    public void The_gradient_filter_is_the_timecycles_bands_as_the_games_post_process_sets_them_up()
    {
        Rdr2Sky sky = Sky(("postfx_grad_top_col_r", [.25f]), ("postfx_grad_top_col_fade", [.5f]), ("postfx_grad_top_col_mult", [.3f]),
            ("postfx_grad_middle_col_g", [.5f]), ("postfx_grad_middle_col_mult", [.6f]), ("postfx_grad_bottom_col_mult", [1.2f]),
            ("postfx_grad_midpoint", [.4f]), ("postfx_grad_top_middle_midpoint", [.25f]), ("postfx_grad_middle_bottom_midpoint", [.75f]));
        Rdr2Gradient gradient = sky.GradeAt(12).Gradient;
        // The top's colour times its fade, raised to 1/2.2, times its multiplier; one less twice its midpoint.
        float Raised(float value) => MathF.Pow(value, 1 / 2.2f);
        Assert.Equal(.3f * Raised(.25f * .5f), gradient.Top.X, 6);
        Assert.Equal(.3f * Raised(.5f), gradient.Top.Y, 6);
        Assert.Equal(.5f, gradient.Top.W, 6);
        // The middle's times its multiplier alone, not raised, and where it sits.
        Assert.Equal(new Vector4(.6f, .3f, .6f, .4f), gradient.Middle);
        Assert.Equal(new Vector4(1.2f, 1.2f, 1.2f, -.5f), gradient.Bottom);
        Assert.Equal(1 / .6f, gradient.Extra.X, 6);
        Assert.Equal(1 / .4f, gradient.Extra.Y, 6);
    }

    [Fact]
    public void The_gradient_filter_darkens_the_frame_down_from_its_top_before_the_exposure()
    {
        uint[] Frame(params (string Field, float[] Values)[] fields)
        {
            Rdr2Sky sky = Sky(fields);
            GpuDevice gpu = TestGpu.Device();
            lock (gpu.Gate)
            {
                var renderer = new ViewportRenderer(gpu);
                const int Width = 64, Height = 32;
                WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
                {
                    Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                    Dimension = WebGpuSharp.TextureDimension.D2,
                    Size = new WebGpuSharp.Extent3D(Width, Height, 1),
                    Format = renderer.PresentFormat,
                    MipLevelCount = 1,
                    SampleCount = 1,
                });
                renderer.Resize(Width, Height);
                renderer.WorldShown = true;
                renderer.Camera.Frame(new Vector3(0, 0, 100), 20);
                renderer.Camera.Free = true;
                renderer.Camera.Place(new Vector3(0, 0, 100), 0, .3f);
                renderer.SetTime(12);
                try
                {
                    renderer.ShowRdr2Sky(sky);
                    for (int i = 0; i < 60; i++)
                        renderer.RenderTo(target.CreateView());
                    return [GtaSkyTests.Pixel(gpu, target, Width / 2, 1), GtaSkyTests.Pixel(gpu, target, Width / 2, Height - 2)];
                }
                finally
                {
                    target.Destroy();
                    renderer.Dispose();
                }
            }
        }
        static int Green(uint pixel) => (int)((pixel >> 8) & 0xFF);
        uint[] plain = Frame();
        uint[] filtered = Frame(("postfx_grad_top_col_mult", [.25f]));
        // The top a quarter as bright before the exposure, which the filter doesn't change: much darker on screen.
        Assert.True(Green(filtered[0]) < Green(plain[0]) - 20, $"{plain[0]:X8} {filtered[0]:X8}");
        // The bottom, past the middle, as it was.
        Assert.InRange(Green(filtered[1]) - Green(plain[1]), -2, 2);
    }

    [Fact]
    public void The_far_haze_builds_up_out_to_the_far_clip_from_the_timecycle()
    {
        Rdr2Sky sky = Sky(("far_haze_start", [100]), ("far_haze_power", [3]), ("far_haze_ambient_intensity", [1]), ("fog_isotropic_phase", [4]),
            ("far_clip", [10000]));
        var values = new Vector4[Rdr2Sky.Vectors];
        sky.Write(values, 12, Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        // Where it starts, one over the far clip less that, its power, its ambient over the isotropic phase's divisor.
        Assert.Equal(new Vector4(100, 1 / 9900f, 3, .25f), values[Rdr2Sky.FarHaze]);
        // At least as far again as it starts; the game's defaults where the weather hasn't it.
        Rdr2Sky near = Sky(("far_haze_start", [1000]), ("far_clip", [1500]));
        near.Write(values, 12, Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        Assert.Equal(new Vector4(1000, 1 / 1000f, 1, .5f / 4), values[Rdr2Sky.FarHaze]);
    }

    [Fact]
    public void The_constants_the_tables_are_built_from_are_copied_alone()
    {
        Vector4[] from = [.. Enumerable.Range(0, 9).Select(i => new Vector4(i + 1))], to = new Vector4[9];
        Rdr2Sky.CopyTables(from, to);
        Assert.True(Rdr2Sky.SameTables(from, to));
        // The altitude bias and the rest aren't the tables'.
        Assert.Equal(new Vector4(1, 1, 1, 0), to[0]);
        Assert.Equal(Vector4.Zero, to[3]);
    }

    [Fact]
    public void A_game_without_its_weather_has_no_sky() =>
        Assert.Null(Rdr2Sky.Read(_ => null));

    [Fact]
    public void By_night_the_moon_and_stars_are_drawn_from_the_weather_and_visualsettings_as_the_game_draws_them()
    {
        Rdr2Sky sky = Sky(("sky_scattering_sun_intensity_multiplier", [0]), ("sky_scattering_moon_intensity_multiplier", [.5f]),
            ("sky_moon_disc_size", [2]), ("sky_moon_iten", [10]), ("sky_moon_col_r", [.5f]), ("sky_stars_iten", [.01f]),
            ("sky_stars_dense_intensity_mult", [2]), ("sky_air_glow_intensity", [.25f]), ("sky_air_glow_base_intensity", [.5f]));
        var values = new Vector4[Rdr2Sky.Vectors];
        Vector3 moon = Vector3.Normalize(new Vector3(1, 0, 1));
        sky.Write(values, 2, -Vector3.UnitZ, moon, true, Vector3.Zero, 0);
        Span<Vector4> night = values.AsSpan(Rdr2Sky.TableVectors);
        // No sun under the horizon nor with its multiplier off; the moon and the stars on.
        Assert.Equal(new Vector4(0, 1, 1, Rdr2Sky.DomeRadius), night[0]);
        // The moon's disc its size times 250 across from its middle at 20 km, its colour by its intensity, its sky lit by its own.
        Assert.Equal(2 * 250 / 20000f, night[1].W, 6);
        Assert.Equal(moon, new Vector3(night[1].X, night[1].Y, night[1].Z));
        Assert.Equal(new Vector3(5, 10, 10), new Vector3(night[4].X, night[4].Y, night[4].Z));
        Assert.Equal(.5f, night[2].X, 5);
        // The dense stars by their colour and the timecycle's multiplier; the tilings, the fades and the blink's gain.
        Assert.Equal(4, night[6].W);
        Assert.Equal(1 / 3000f, night[9].X, 9);
        Assert.Equal(new Vector2(8, .5f), new Vector2(night[9].Z, night[9].W));
        // The air glow and the base colour (the game's defaults) by their intensities, over a thousand.
        Assert.True(Vector3.Distance(new Vector3(.184f, 1, .494f) * .25f / 1000, new Vector3(night[13].X, night[13].Y, night[13].Z)) < 1e-9f);
        Assert.True(Vector3.Distance(new Vector3(.072f, .35f, 1) * .5f / 1000, new Vector3(night[14].X, night[14].Y, night[14].Z)) < 1e-9f);
        // The stars under the galaxy's light: the dense ones at its intensity.
        Assert.Equal(new Vector3(.549f, .627f, 1) * 2 * 3, new Vector3(night[6].X, night[6].Y, night[6].Z));
        // The moon under the horizon and the stars off: neither is drawn.
        Rdr2Sky dark = Sky(("sky_stars_iten", [0]));
        dark.Write(values, 2, -Vector3.UnitZ, -Vector3.UnitZ, true, Vector3.Zero, 0);
        Assert.Equal(Vector3.Zero, new Vector3(values[Rdr2Sky.TableVectors].Y, values[Rdr2Sky.TableVectors].Z, 0));
    }

    [Fact]
    public void The_stars_turn_with_the_time_of_day_about_the_north_axis()
    {
        Rdr2Sky sky = Sky();
        Matrix4x4 midnight = sky.StarsRotation(0), dawn = sky.StarsRotation(6);
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.Equal(1, Vector3.Transform(axis, dawn).Length(), 5);
        // Between them, a turn about the north axis by the rotation rate times a quarter of a day.
        Assert.True(Matrix4x4.Invert(midnight, out Matrix4x4 back));
        Matrix4x4 between = dawn * back;
        Matrix4x4 expected = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0, .888f, .46f)), .15f * MathF.PI / 2);
        Assert.Equal(expected.M11, between.M11, 4);
        Assert.Equal(expected.M12, between.M12, 4);
        Assert.Equal(expected.M23, between.M23, 4);
        Assert.Equal(expected.M31, between.M31, 4);
    }

    [Fact]
    public void The_domes_coordinates_are_the_skys_angle_from_the_zenith_round_by_its_bearing()
    {
        Assert.Equal(new Vector2(.5f), Rdr2SkyDome.Coordinates(Vector3.UnitZ));
        Vector2 east = Rdr2SkyDome.Coordinates(Vector3.UnitX), south = Rdr2SkyDome.Coordinates(-Vector3.UnitY);
        Assert.Equal(.5f, east.X, 5);
        Assert.Equal(0, east.Y, 5);
        Assert.Equal(1, south.X, 5);
        Assert.Equal(.5f, south.Y, 5);
        Rdr2SkyDome sphere = Rdr2SkyDome.Sphere(8, 16);
        Assert.Equal(9 * 17 * 5, sphere.Vertices.Length);
        Assert.Equal(8 * 16 * 6, sphere.Indices.Length);
    }

    [Fact]
    public void The_games_dome_and_its_textures_are_read_from_its_files()
    {
        var sources = RedFury.World.WorldGameSource.ForGame(TestGames.Rdr2(), Rdr2Keys.Bundled.Crypto);
        Rdr2SkyDome dome = Rdr2SkyDome.Read(sources, Rdr2Keys.Bundled.Crypto) ?? throw new InvalidOperationException("no dome");
        Assert.True(dome.Indices.Length > 1000);
        Assert.NotNull(dome.Stars);
        Assert.NotNull(dome.AirGlow);
        Assert.NotNull(dome.Moon);
        // A unit sphere, its coordinates out from the middle by the angle from the zenith over pi.
        for (int i = 0; i < dome.Vertices.Length; i += 5 * 97)
        {
            var place = new Vector3(dome.Vertices[i], dome.Vertices[i + 1], dome.Vertices[i + 2]);
            Assert.Equal(1, place.Length(), 1);
            if (place.Z > .05f)
                Assert.Equal(MathF.Acos(Vector3.Normalize(place).Z) / MathF.PI,
                    Vector2.Distance(new Vector2(dome.Vertices[i + 3], dome.Vertices[i + 4]), new Vector2(.5f)), .03f);
        }
    }

    [Fact]
    public void An_RDR2_worlds_sky_is_drawn_behind_it_with_no_GPU_errors_its_tables_built_once_for_each_air()
    {
        Rdr2Sky sky = Sky(("sky_scattering_aerosol_density_scale", [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2]));
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            const int Width = 64, Height = 32;
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Width, Height, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.Resize(Width, Height);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(0, 0, 100), 20);
            renderer.Camera.Free = true;
            // Looking up, away from the sun.
            Vector3 sun = DayCycle.SunDirection(12);
            renderer.Camera.Place(new Vector3(0, 0, 100), MathF.Atan2(sun.Y, sun.X), -.6f);
            renderer.SetTime(12);
            uint Middle()
            {
                renderer.RenderTo(target.CreateView());
                return GtaSkyTests.Pixel(gpu, target, Width / 2, 2);
            }
            uint backdrop = Middle();
            renderer.ShowRdr2Sky(sky);
            Assert.True(renderer.HasRdr2Sky);
            Assert.False(renderer.HasWorldSky);
            uint day = Middle();
            Middle();
            Assert.Equal(1, renderer.Rdr2SkyBuilds);
            // The sky's blue: more blue than red (the frame's BGRA).
            Assert.True((day & 0xFF) > ((day >> 16) & 0xFF), $"{day:X8}");
            Assert.NotEqual(backdrop, day);
            // The world's lit by the sky in the game's own units: the sun through the air, the sky above blue, the
            // ground under it sending back a quarter of what falls on it; and exposed by the game's curve for what the
            // frame measures.
            static float Luminance(Vector4 colour) => Vector3.Dot(new Vector3(colour.X, colour.Y, colour.Z), new Vector3(.2126f, .7152f, .0722f));
            Vector4[] noon = renderer.Rdr2SkyLight();
            Assert.Equal(1, noon[0].W);
            Assert.InRange(Luminance(noon[0]), 1e3f, 1e8f);
            Assert.True(noon[3].Z > noon[3].X, $"{noon[3]}");
            Assert.True(noon[6].X > 0 && noon[6].X < noon[0].X, $"{noon[6]}");
            // Metered by the middle of its histogram, and by the sky's ambient cube round the camera.
            var (mean, _, aimed, cube) = renderer.MeasuredExposure();
            Assert.True(mean > 0 && cube > 0, $"{mean} {cube}");
            Assert.Equal(renderer.Rdr2WorldExposure!.TargetFor(mean, cube), aimed, 2);
            // The sun lower, the same air: the tables stay; other air: they're built again.
            renderer.SetTime(15);
            Middle();
            Assert.Equal(1, renderer.Rdr2SkyBuilds);
            // Other air, the hour just moved: the tables wait (the hour may be moving still); once it holds, they're built.
            renderer.Rdr2TablesInterval = TimeSpan.FromHours(1);
            renderer.SetTime(23);
            Middle();
            Assert.Equal(1, renderer.Rdr2SkyBuilds);
            Middle();
            Assert.Equal(2, renderer.Rdr2SkyBuilds);
            Middle();
            Assert.Equal(2, renderer.Rdr2SkyBuilds);
            // By night, the moon's light: thousands of times less.
            Assert.True(Luminance(renderer.Rdr2SkyLight()[0]) < Luminance(noon[0]) / 1000);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.ClearWorld();
            Assert.False(renderer.HasRdr2Sky);
            renderer.Dispose();
            target.Destroy();
        }
    }
}
