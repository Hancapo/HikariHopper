using System.Numerics;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's sky in its world: its constants as the game derives them from the timecycle, the passes it draws, and a frame
// with it behind the world.
[Collection(ExplorerCollection.Name)]
public sealed class GtaSkyTests
{
    // A clear weather with every keyframe the same: the fields the sky's passes and fog test.
    private static string Weather(params (string Field, float Value)[] fields) => $"""
        <timecycle_keyframe_data version="1.000000">
          <cycle name="EXTRASUNNY" regions="1">
            <region name="GLOBAL">
        {string.Join("\n", fields.Select(f => $"      <{f.Field}>{string.Join(' ', Enumerable.Repeat(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), 13))}</{f.Field}>"))}
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    // visualsettings.dat's, as a file has them.
    private static readonly GtaSkyVisuals Visuals = GtaSkyVisuals.From(GameVisualSettings.Parse("""
        cloudgen.edge.detail.scale		16.0
        cloudgen.overlay.detail.scale	8.0
        cloud.speed.large	5.0 # the large clouds
        cloud.speed.small	1.0
        cloud.speed.overall	1.0
        cloud.speed.edge	1.0
        """));

    [Fact]
    public void Visualsettings_are_read_line_by_line_past_their_comments_with_the_games_defaults_where_a_file_has_none()
    {
        Assert.Equal(new GtaSkyVisuals(16, 8, 5, 1, 1, 1), Visuals);
        GameVisualSettings file = GameVisualSettings.Parse("# sky\ncloud.speed.large 7.5\t2\nnot.a.number abc\n");
        Assert.Equal(7.5f, file.Get("cloud.speed.large", 5));
        Assert.Equal(3, file.Get("not.a.number", 3));
        Assert.Equal(new GtaSkyVisuals(16, 8, 7.5f, 1, 1, 1), GtaSkyVisuals.From(file));
    }

    private static T WithWeather<T>(Func<T> read, params (string Field, float Value)[] fields)
    {
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather(Weather(fields)), []));
            return read();
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    [Fact]
    public void The_sun_and_moon_sit_on_the_dome_round_the_camera_their_disc_colours_squared_times_the_suns_hdr()
    {
        var camera = new Vector3(100, -200, 60);
        var values = new Vector4[GtaSky.Bytes / 16];
        GtaSky.Passes passes = WithWeather(() => GtaSky.Write(values, Visuals, 13, null, camera, 0),
            ("sky_sun_hdr", 255), ("sky_sun_disc_col_r", .5f), ("sky_sun_disc_size", 2), ("sky_moon_iten", 2), ("sky_stars_iten", .8f),
            ("sky_cloud_density_mult", 0), ("sky_small_cloud_detail_strength", 0));
        Vector3 sun = DayCycle.SunDirection(13), moon = DayCycle.MoonDirection(13);
        var centre = new Vector3(camera.X, camera.Y, 0);
        Assert.True(Vector3.Distance(centre + sun * 20000, values[12].AsVector3()) < .1f);
        Assert.True(Vector3.Distance(centre + moon * 20000, values[13].AsVector3()) < .1f);
        // As the game sends it: where the sun is, made unit from the world's origin.
        Assert.True(Vector3.Distance(Vector3.Normalize(centre + sun * 20000), values[10].AsVector3()) < 1e-5f);
        Assert.Equal(.25f * 255, values[8].X, 3);
        // The sun's disc is two by 250 metres across at the dome.
        Assert.Equal(500, values[6].W);
        Assert.Equal(new GtaSky.Passes(Sun: true, Moon: moon.Z >= -1e-5f, Clouds: false, Stars: true), passes);
        Assert.Equal(camera, values[38].AsVector3());
    }

    [Fact]
    public void The_clouds_are_drawn_where_their_density_outweighs_its_bias_and_the_sun_not_at_night()
    {
        var values = new Vector4[GtaSky.Bytes / 16];
        Assert.True(WithWeather(() => GtaSky.Write(values, Visuals, 13, null, Vector3.Zero, 0), ("sky_cloud_density_mult", 3), ("sky_cloud_density_bias", .8f)).Clouds);
        Assert.False(WithWeather(() => GtaSky.Write(values, Visuals, 13, null, Vector3.Zero, 0), ("sky_cloud_density_mult", 0), ("sky_cloud_density_bias", .13f), ("sky_small_cloud_detail_strength", 0)).Clouds);
        // The small filler clouds alone, where their detail has any strength.
        Assert.True(WithWeather(() => GtaSky.Write(values, Visuals, 13, null, Vector3.Zero, 0),
            ("sky_small_cloud_density_mult", 2), ("sky_small_cloud_density_bias", 1), ("sky_small_cloud_detail_strength", .5f)).Clouds);
        Assert.False(WithWeather(() => GtaSky.Write(values, Visuals, 2, null, Vector3.Zero, 0), ("sky_sun_hdr", 255)).Sun);
    }

    [Fact]
    public void The_fog_is_the_games_height_fog_thinner_above_its_base_its_far_colour_towards_its_near_one_where_dense()
    {
        var values = new Vector4[GtaSky.Bytes / 16];
        (string, float)[] fog = [("fog_density", 10), ("fog_falloff", 10), ("fog_base_height", 0), ("far_clip", 10000), ("fog_horizon_tint_scale", 4),
            ("fog_hdr", 2), ("fog_east_col_r", 1), ("fog_east_col_g", 0), ("fog_east_col_b", 0), ("fog_near_col_r", 0), ("fog_near_col_g", 1), ("fog_near_col_b", 0)];
        WithWeather(() => GtaSky.Write(values, Visuals, 13, null, new Vector3(0, 0, 0), 0), fog);
        // At the fog's base the camera is in it all: the ground fog's density there, negated, and its colours alone.
        Assert.Equal(-.001f, values[30].W, 6);
        Assert.Equal(.0004f, values[30].Z, 6);
        Assert.Equal(new Vector3(0, 2, 0), values[35].AsVector3());
        WithWeather(() => GtaSky.Write(values, Visuals, 13, null, new Vector3(0, 0, 1000), 0), fog);
        // A kilometre up there's next to none of it, and the far colour stays mostly its own.
        Assert.Equal(-.001f * MathF.Exp(-10), values[30].W, 9);
        Assert.True(values[35].X > 1.5f && values[35].Y < .5f, $"{values[35]}");
    }

    [Fact]
    public void Fields_a_timecycle_hasnt_take_the_games_own_defaults_so_the_fog_never_divides_by_nought()
    {
        // The app's stand-in timecycle has the sky's gradient but none of its fog.
        var values = new Vector4[GtaSky.Bytes / 16];
        GtaSky.Write(values, Visuals, 13, null, new Vector3(0, 0, 50), 0);
        Assert.All(values, value => Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W), $"{value}"));
        // far_clip's 1500 past fog_start's 73; fog_falloff's 0.5, per kilometre.
        Assert.Equal(new Vector2(73, 1500), new Vector2(values[29].X, values[29].Y));
        Assert.Equal(.0005f, values[31].Z, 6);
    }

    [Fact]
    public void The_sun_fades_out_and_the_moon_in_over_the_night_as_the_game_fades_them()
    {
        Assert.Equal((1f, 0f), DayCycle.Fades(13));
        Assert.Equal(.6f, DayCycle.Fades(21.6f).Sun, 4);
        Assert.Equal(0, DayCycle.Fades(21.6f).Moon);
        Assert.Equal(0, DayCycle.Fades(23).Sun);
        Assert.Equal(Math.Clamp(DayCycle.MoonDirection(23).Z / .1f, 0, 1), DayCycle.Fades(23).Moon, 4);
        Assert.Equal(1, DayCycle.Fades(5).Sun);
    }

    [Fact]
    public void A_worlds_sky_is_drawn_behind_it_with_no_GPU_errors_until_the_world_goes() => WithWeather(() =>
    {
        DrawSky();
        return 0;
    }, ("sky_hdr", 4), ("sky_zenith_col_r", 1), ("sky_zenith_col_inten", 1), ("sky_zenith_transition_col_r", 1), ("sky_zenith_transition_col_inten", 1),
        ("sky_azimuth_east_col_r", 1), ("sky_azimuth_east_col_inten", 1), ("sky_azimuth_west_col_r", 1), ("sky_azimuth_west_col_inten", 1),
        ("sky_azimuth_transition_col_r", 1), ("sky_azimuth_transition_col_inten", 1), ("sky_azimuth_transition_position", .5f),
        ("sky_zenith_transition_position", .5f), ("sky_zenith_transition_east_blend", .5f), ("sky_zenith_transition_west_blend", .5f),
        ("sky_sun_hdr", 50), ("sky_sun_disc_col_r", 1), ("sky_sun_disc_size", 2), ("sky_stars_iten", 1));

    private static void DrawSky()
    {
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
            renderer.Camera.Frame(new Vector3(0, 0, 50), 20);
            renderer.Camera.Free = true;
            // Looking up at the sky, towards the sun.
            Vector3 sun = DayCycle.SunDirection(14);
            renderer.Camera.Place(new Vector3(0, 0, 50), MathF.Atan2(-sun.Y, -sun.X), -.4f);
            renderer.SetTime(14);
            uint Middle()
            {
                renderer.RenderTo(target.CreateView());
                return Pixel(gpu, target, Width / 2, 2);
            }
            uint backdrop = Middle();
            renderer.ShowWorldSky(Octahedron());
            Assert.True(renderer.HasWorldSky);
            uint day = Middle();
            renderer.SetTime(23);
            Middle();
            Assert.True(renderer.WorldSkyPasses.Stars);
            Assert.False(renderer.WorldSkyPasses.Sun);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            Assert.NotEqual(backdrop, day);
            renderer.ClearWorld();
            Assert.False(renderer.HasWorldSky);
            renderer.Dispose();
            target.Destroy();
        }
    }

    // A dome of eight triangles, its coordinates across it, with no textures: each is the renderer's stand-in.
    private static GtaSkyDome Octahedron()
    {
        Vector3[] corners = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        float[] vertices = [.. corners.SelectMany(c => new[] { c.X, c.Y, c.Z, c.X * .5f + .5f, c.Y * .5f + .5f, c.X * .5f + .5f, c.Z * .5f + .5f })];
        uint[] indices = [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5];
        return new GtaSkyDome(vertices, indices, null, null, null, null, null, Visuals);
    }

    private static uint Pixel(GpuDevice gpu, WebGpuSharp.Texture target, int x, int y)
    {
        const uint Row = 256;
        WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
        {
            Size = Row * 32u,
            Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
        });
        WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = target },
            new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = Row, RowsPerImage = 32u } },
            new WebGpuSharp.Extent3D(64u, 32u, 1));
        gpu.Queue.Submit(encoder.Finish());
        readback.MapSync(WebGpuSharp.MapMode.Read);
        uint pixel = readback.GetConstMappedRange<uint[], uint>(0, Row * 32u / 4, span => span.ToArray())[(int)(y * Row / 4 + x)];
        readback.Unmap();
        readback.Destroy();
        return pixel;
    }
}
