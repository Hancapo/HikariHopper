using System.Buffers.Binary;
using System.Numerics;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Tests;

// GTA V's coronas on its world's lights: the settings they're drawn with, what the game registers from a map light and
// from a LOD light, which LOD lights have one, the height map the LOD lights' reach follows, and a frame with one.
[Collection(ExplorerCollection.Name)]
public sealed class GtaCoronaTests
{
    // visualsettings.dat's, as a file has them: tabs between, a comment, and a setting given twice (the later one kept).
    private static readonly GtaCoronaVisuals Visuals = GtaCoronaVisuals.From(GameVisualSettings.Parse("""
        # coronas
        misc.coronas.sizeScaleGlobal					0.75
        misc.coronas.intensityScaleGlobal				0.5
        misc.coronas.zBias.max				0.35
        misc.coronas.zBias.fromFinalSizeMultiplier 		0.1
        misc.coronas.m_doNGCoronas						1.0
        misc.coronas.m_layer1Muliplier					0.5
        misc.coronas.screenEdgeMinDistForFade	1.5
        lights.cutoff.map.distance				1004.0
        lodlight.medium.corona.range	180.0
        lodlight.corona.size			2.0
        lodlight.sphere.expand.size.mult	2
        lodlight.sphere.expand.start		50.0
        lodlight.sphere.expand.end			500.0
        """));

    private static WorldLightDefinition Light(byte type = 1, uint flags = 0, float intensity = 32) => new(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitX,
        new Vector3(1, .5f, 0), intensity, 20, 8, type, -1, 0xffffff, flags, 5, 60, new Vector3(3, 0, 0), Vector3.Zero);

    private static WorldCoronaLight Corona(WorldLightDefinition light, float size = 2, float intensity = .2f, byte fade = 0, bool interior = false) =>
        new(new Vector3(100, 200, 30), light.Direction, light, new DrawableLightCorona(size, intensity, fade), interior);

    [Fact]
    public void The_settings_are_the_files_with_the_games_defaults_where_it_hasnt_them()
    {
        Assert.Equal(.75f, Visuals.SizeScaleGlobal);
        Assert.Equal(.5f, Visuals.IntensityScaleGlobal);
        Assert.Equal(.35f, Visuals.ZBiasMax);
        Assert.Equal(.1f, Visuals.ZBiasFromFinalSizeMultiplier);
        Assert.Equal(1, Visuals.DoNgCoronas);
        Assert.Equal(.5f, Visuals.Layer1Multiplier);
        // The game's defaults where the file is silent, and the screen edge saturated as the game reads it.
        Assert.Equal(1, Visuals.Layer2Multiplier);
        Assert.Equal(32, Visuals.OcclusionSizeMax);
        Assert.Equal(1, Visuals.ScreenEdgeMinDistanceForFade);
        Assert.Equal(new Vector3(.01f, .02f, .05f), Visuals.FlareColourShift);
        Assert.Equal(new Vector3(150, 180, 1250), Visuals.LodLightCoronaRange);
        Assert.Equal(1004, Visuals.MapLightFadeDistance);
        Assert.Equal(1000, Visuals.LightCutoffDistance);
        // The LOD lights reach further the higher the camera is over the ground: twice as far from 500 m up.
        Assert.Equal(1, Visuals.SphereExpansion(10));
        Assert.Equal(1.5f, Visuals.SphereExpansion(275));
        Assert.Equal(2, Visuals.SphereExpansion(900));
        Assert.Equal(new GtaCoronaVisuals(), GtaCoronaVisuals.From(null));
    }

    [Fact]
    public void A_map_light_registers_its_corona_as_the_game_does()
    {
        GtaCoronaInstance spot = GtaCoronas.FromLight(Corona(Light(type: 2, flags: 1u << 16)), Visuals)!.Value;
        // Directional, not reflected, its cone in its bytes; its corona intensity and its light's apart.
        Assert.Equal(GtaCoronas.HasDirection | GtaCoronas.DontReflect, spot.Packed & 0xffff);
        Assert.Equal(5u, (spot.Packed >> 16) & 0xff);
        Assert.Equal(60u, spot.Packed >> 24);
        Assert.Equal(.2f, spot.Intensity);
        Assert.Equal(32, spot.LightIntensity);
        Assert.Equal(2u << GtaCoronas.KindTypeShift, spot.Kind);
        Assert.Equal(0x0080ffu, spot.Colour);
        // A map light outside fades at the map's distance, another at its own and keeps its size.
        Assert.Equal(1004, spot.FadeDistance);
        GtaCoronaInstance fading = GtaCoronas.FromLight(Corona(Light(), fade: 40), Visuals)!.Value;
        Assert.Equal(40, fading.FadeDistance);
        Assert.NotEqual(0u, fading.Kind & GtaCoronas.KindFixedSize);
        // Inside: the lights' own cutoff, and no timecycle scaling of its sprite.
        GtaCoronaInstance inside = GtaCoronas.FromLight(Corona(Light(), interior: true), Visuals)!.Value;
        Assert.Equal(1000, inside.FadeDistance);
        Assert.NotEqual(0u, inside.Kind & GtaCoronas.KindInterior);
        Assert.NotEqual(0u, GtaCoronas.FromLight(Corona(Light(flags: 1)), Visuals)!.Value.Kind & GtaCoronas.KindFixedSize);
        // A corona-only light keeps the light's default radius; a capsule its length.
        Assert.Equal(1, GtaCoronas.FromLight(Corona(Light(flags: 1u << 15)), Visuals)!.Value.Falloff);
        Assert.Equal(3, GtaCoronas.FromLight(Corona(Light(type: 4)), Visuals)!.Value.Extent);
        // Cones wider than the coronas' bytes take are clamped to 90 degrees.
        Assert.Equal(90u, GtaCoronas.FromLight(Corona(Light(type: 2) with { ConeOuterAngle = 120 }), Visuals)!.Value.Packed >> 24);
        // None without a corona, a light, or outside reflections.
        Assert.Null(GtaCoronas.FromLight(Corona(Light(), size: 0), Visuals));
        Assert.Null(GtaCoronas.FromLight(Corona(Light(), intensity: 0), Visuals));
        Assert.Null(GtaCoronas.FromLight(Corona(Light(intensity: 0)), Visuals));
        Assert.Null(GtaCoronas.FromLight(Corona(Light(flags: 1u << 17)), Visuals));
    }

    private static GtaLodLights.Light Lod(Vector3 position, byte category = 1, uint type = 2, uint hours = 0xffffff, byte corona = 8) =>
        new(position, 0x3FFF8000, Vector3.UnitY, hours | type << 26, 102, corona, category);

    [Fact]
    public void A_lod_light_registers_its_corona_at_its_hours_from_its_packed_values()
    {
        GtaCoronaInstance spot = GtaCoronas.FromLodLight(Lod(Vector3.Zero), 23)!.Value;
        Assert.Equal(GtaCoronas.DontRenderUnderwater | GtaCoronas.FromLod | GtaCoronas.DontRotate | GtaCoronas.DontRampUp | GtaCoronas.DontReflect
            | GtaCoronas.HasDirection | GtaCoronas.FadeOffDirMult, spot.Packed & 0xffff);
        // Its intensity out of 48 and its corona's out of 32; its cone's outer angle out of 180, clamped to 90.
        Assert.Equal(63 * 48 / 255f * (8 * 32 / 255f), spot.Intensity, 4);
        Assert.Equal(0u, (spot.Packed >> 16) & 0xff);
        Assert.Equal(72u, spot.Packed >> 24);
        Assert.Equal(0x0080ffu, spot.Colour);
        Assert.Equal(GtaCoronas.KindLod | 1u << 1 | 2u << GtaCoronas.KindTypeShift, spot.Kind);
        // A small spot doesn't fade its direction off; a point light's cone is the default.
        Assert.Equal(0u, GtaCoronas.FromLodLight(Lod(Vector3.Zero, category: 0), 23)!.Value.Packed & GtaCoronas.FadeOffDirMult);
        Assert.Equal(90u, GtaCoronas.FromLodLight(Lod(Vector3.Zero, type: 1), 23)!.Value.Packed >> 24);
        Assert.Null(GtaCoronas.FromLodLight(Lod(Vector3.Zero, hours: 1u << 22), 23));
        Assert.Null(GtaCoronas.FromLodLight(Lod(Vector3.Zero, corona: 0), 23));
    }

    [Fact]
    public void Lod_lights_have_coronas_at_night_in_their_reach_where_their_map_light_isnt_in_the_cut()
    {
        var eye = new Vector3(1000, 1000, 40);
        var lods = new GtaLodLights([
            Lod(eye + new Vector3(100, 0, 0)),
            Lod(eye + new Vector3(0, 100, 0)),
            // Beyond the medium lights' reach, and a large one within its own.
            Lod(eye + new Vector3(0, 400, 0)),
            Lod(eye + new Vector3(0, -900, 0), category: 2),
        ]);
        WorldCoronaLight[] lights = [Corona(Light())];
        var coronas = new List<GtaCoronaInstance>();
        GtaCoronas.Build(coronas, lights, [eye + new Vector3(0, 100.05f, 0)], lods, Visuals, eye, 23, 0);
        Assert.Equal(3, coronas.Count);
        Assert.Equal(lights[0].Position, coronas[0].Position);
        Assert.Contains(coronas, corona => corona.Position == eye + new Vector3(100, 0, 0));
        Assert.Contains(coronas, corona => corona.Position == eye + new Vector3(0, -900, 0));
        // By day the LOD lights aren't loaded.
        GtaCoronas.Build(coronas, lights, [], lods, Visuals, eye, 12, 0);
        Assert.Single(coronas);
        GtaCoronas.Build(coronas, lights, [], lods, Visuals, eye, 19, 0);
        Assert.Equal(4, coronas.Count);
    }

    [Fact]
    public void The_cut_bound_with_coronas_asked_for_gives_each_lights_corona_values_where_it_is()
    {
        var node = new WorldLodNode(new(1, 0), null, new(2, 2), Matrix4x4.CreateTranslation(10, 20, 30), Vector3.Zero, 10, 100, 0, 0);
        var selection = new WorldSelection([], [node.Asset]) { Coverage = [node] };
        WorldLightDefinition on = Light() with { Position = Vector3.UnitX }, off = Light() with { TimeFlags = 1u << 3 };
        WorldLightBones bones = WorldLightBones.Carrying([on, off], [new DrawableLightCorona(2, .5f, 0), new DrawableLightCorona(1, 1, 0)]);
        WorldLightBinding bound = WorldLightBinding.Resolve(selection, _ => bones, hour: 22, interior: _ => true);
        WorldCoronaLight corona = Assert.Single(bound.Coronas);
        Assert.Equal(new Vector3(11, 20, 30), corona.Position);
        Assert.Equal(new DrawableLightCorona(2, .5f, 0), corona.Corona);
        Assert.True(corona.Interior);
        Assert.Equal(new[] { new Vector3(11, 20, 30) }, bound.Positions);
        // Without asking, none.
        Assert.Empty(WorldLightBinding.Resolve(selection, _ => bones, hour: 22).Coronas);
    }

    // A height map file: 4 by 4 cells over 40 m, heights 0 to 255 m, each cell's lowest its column times ten plus its row.
    private static byte[] HeightMap(bool runs)
    {
        const int Side = 4;
        int rle = runs ? Side * 8 : 0;
        var file = new byte[44 + rle + 2 * Side * Side];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x484D4150);
        file[5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), runs ? 1u : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), Side);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(14), Side);
        float[] bounds = [0, 0, 0, 40, 40, 255];
        for (int i = 0; i < 6; i++)
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(16 + i * 4), bounds[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), (uint)(rle + 2 * Side * Side));
        for (int j = 0; j < Side; j++)
        {
            if (runs)
            {
                // Each row whole, but the last, which has only its third column.
                int start = j == Side - 1 ? 2 : 0;
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(44 + j * 8), (ushort)start);
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(46 + j * 8), (ushort)(j == Side - 1 ? 1 : Side));
                BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(48 + j * 8), j * Side);
            }
            for (int i = 0; i < Side; i++)
                file[44 + rle + Side * Side + j * Side + i] = (byte)(i * 10 + j);
        }
        return file;
    }

    [Fact]
    public void The_height_map_gives_the_lowest_ground_round_a_point_from_its_cells_whether_run_length_coded_or_not()
    {
        foreach (bool runs in new[] { false, true })
        {
            GtaHeightMap map = GtaHeightMap.Read(HeightMap(runs))!;
            Assert.Equal(21, map.MinimumAt(2, 1), 3);
            // Outside the map, or a row's run, the bounds' bottom.
            Assert.Equal(0, map.MinimumAt(-1, 1));
            Assert.Equal(0, map.MinimumAt(1, 7));
            Assert.Equal(runs ? 0 : 13, map.MinimumAt(1, 3), 3);
            // Each corner the lowest of the nine cells round it (the last row's missing cells the bottom), then
            // interpolated.
            Assert.Equal(runs ? 0 : 11, map.MinimumInterpolated(25, 25), 3);
            Assert.Equal(runs ? 2.5f : 5.5f, map.MinimumInterpolated(20, 20), 3);
        }
        Assert.Null(GtaHeightMap.Read(new byte[44]));
    }

    [Fact]
    public void A_height_map_whose_cells_or_runs_arent_all_in_it_isnt_read()
    {
        static byte[] Edited(bool runs, Action<byte[]> edit)
        {
            byte[] file = HeightMap(runs);
            edit(file);
            return file;
        }
        // Its data a cell short.
        Assert.Null(GtaHeightMap.Read(Edited(false, file => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), 2 * 16 - 1))));
        // More rows' runs than there is data.
        Assert.Null(GtaHeightMap.Read(Edited(true, file => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(14), 200))));
        // A run past the row's columns, one starting past the data, and one whose offset reads as negative.
        Assert.Null(GtaHeightMap.Read(Edited(true, file => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(46), 5))));
        Assert.Null(GtaHeightMap.Read(Edited(true, file => BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(48 + 8), 13))));
        Assert.Null(GtaHeightMap.Read(Edited(true, file => BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(48 + 8), -4))));
        // An empty run, wherever it says it starts, is fine.
        GtaHeightMap map = GtaHeightMap.Read(Edited(true, file =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(46 + 8), 0);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(48 + 8), 1000);
        }))!;
        Assert.Equal(0, map.MinimumAt(2, 1));
        Assert.Equal(22, map.MinimumAt(2, 2), 3);
    }

    [Fact]
    public void The_frames_constants_take_the_timecycles_sprites_with_the_games_defaults_where_it_hasnt_them()
    {
        var values = new Vector4[GtaCoronas.Bytes / 16];
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather("""
                <timecycle_keyframe_data version="1.000000">
                  <cycle name="EXTRASUNNY" regions="1">
                    <region name="GLOBAL">
                      <sprite_size>2 2 2 2 2 2 2 2 2 2 2 2 2</sprite_size>
                    </region>
                  </cycle>
                </timecycle_keyframe_data>
                """), []));
            GtaCoronas.Write(values, Visuals, 23, Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, .5f, 2, 1000, 10, 275, 7);
        }
        finally
        {
            Timecycle.Use(null);
        }
        // 2.5 times the sprites' size; their brightness and the light falloff's multiplier the game's defaults.
        Assert.Equal(5, values[2].X);
        Assert.Equal(1, values[2].Y);
        Assert.Equal(1, values[8].Z);
        // The LOD lights' corona size and its z-bias, clamped.
        Assert.Equal(10, values[2].W);
        Assert.Equal(.35f, values[5].Z);
        // A metre a pixel away: tan of half the horizontal field of view over the width.
        Assert.Equal(.001f, values[1].Z, 6);
        // The small lights' reach half again as far at 275 m up.
        Assert.Equal(225 * 225, values[9].X, 1);
        Assert.Equal(7, BitConverter.SingleToInt32Bits(values[14].X));
    }


    [Fact]
    public void A_worlds_coronas_are_added_over_it_with_no_GPU_errors_until_the_world_goes()
    {
        // A plain night sky (blue, no clouds, moon or stars), with the fog and cloud fields the sky can't do without.
        (string Field, float Value)[] fields = [("sky_hdr", 1), ("sky_zenith_col_b", .1f), ("sky_zenith_col_inten", 1), ("sky_azimuth_east_col_b", .1f),
            ("sky_azimuth_east_col_inten", 1), ("sky_azimuth_west_col_b", .1f), ("sky_azimuth_west_col_inten", 1), ("sky_cloud_density_mult", 0),
            ("sky_small_cloud_density_mult", 0), ("sky_moon_iten", 0), ("sky_stars_iten", 0), ("sky_sun_influence_radius", 1),
            ("sky_moon_influence_radius", 1), ("fog_sun_lighting_calc_pow", 8), ("fog_moon_lighting_calc_pow", 8), ("sky_cloud_gen_softness", .2f),
            ("fog_falloff", 10), ("far_clip", 10000)];
        string weather = $$"""
            <timecycle_keyframe_data version="1.000000">
              <cycle name="EXTRASUNNY" regions="1">
                <region name="GLOBAL">
            {{string.Join('\n', fields.Select(f => $"      <{f.Field}>{string.Join(' ', Enumerable.Repeat(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), 13))}</{f.Field}>"))}}
                </region>
              </cycle>
            </timecycle_keyframe_data>
            """;
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather(weather), []));
            DrawCorona();
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    private static void DrawCorona()
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
            var eye = new Vector3(0, 0, 50);
            renderer.Camera.Frame(eye, 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(eye, 0, 0);
            renderer.SetTime(23);
            renderer.ShowWorldSky(Dome());
            // A white glow, and a point light's corona straight ahead.
            var glow = new FiveFury.Textures.Texture("corona", 16, 16, CommonFury.Textures.TextureFormat.R8Unorm, 1, Enumerable.Repeat((byte)255, 256).ToArray());
            renderer.ShowWorldCoronas(new GtaCoronaData(glow, null, new GtaCoronaVisuals(), GtaLodLights.Empty, null));
            Assert.True(renderer.HasWorldCoronas);
            GtaCoronaInstance corona = GtaCoronas.FromLight(new WorldCoronaLight(eye + renderer.Camera.Forward * 30, -Vector3.UnitZ, Light(intensity: 10),
                new DrawableLightCorona(2, 1, 0), false), new GtaCoronaVisuals())!.Value;
            // The HDR target's red where the corona is, before the tone map.
            float Middle()
            {
                renderer.RenderTo(target.CreateView());
                return HdrRed(gpu, renderer, Width / 2, Height / 2);
            }
            renderer.SetWorldCoronas([]);
            float without = Middle();
            renderer.SetWorldCoronas([corona]);
            Assert.Equal(1, renderer.WorldCoronaCount);
            float with = Middle();
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            // Its light's colour is red at full: the glow adds its intensity, the light's ten times sprite_brightness.
            Assert.True(with > without + 5, $"{with} over {without}");
            renderer.ClearWorld();
            Assert.False(renderer.HasWorldCoronas);
            renderer.Dispose();
            target.Destroy();
        }
    }

    // A dome of eight triangles, with no textures: each is the renderer's stand-in.
    internal static GtaSkyDome Dome()
    {
        Vector3[] corners = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        float[] vertices = [.. corners.SelectMany(c => new[] { c.X, c.Y, c.Z, c.X * .5f + .5f, c.Y * .5f + .5f, c.X * .5f + .5f, c.Z * .5f + .5f })];
        uint[] indices = [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5];
        return new GtaSkyDome(vertices, indices, null, null, null, null, null, GtaSkyVisuals.From(null));
    }

    internal static float HdrRed(GpuDevice gpu, ViewportRenderer renderer, int x, int y)
    {
        var hdr = (WebGpuSharp.Texture)typeof(ViewportRenderer).GetField("hdr", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(renderer)!;
        uint width = hdr.GetWidth(), height = hdr.GetHeight(), row = (width * 8 + 255) / 256 * 256;
        WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
        {
            Size = row * height,
            Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
        });
        WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = hdr },
            new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = row, RowsPerImage = height } },
            new WebGpuSharp.Extent3D(width, height, 1));
        gpu.Queue.Submit(encoder.Finish());
        readback.MapSync(WebGpuSharp.MapMode.Read);
        byte[] bytes = readback.GetConstMappedRange<byte[], byte>(0, (nuint)(row * height), span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        int texel = (int)((uint)(y * (int)height / 32) * row + (uint)(x * (int)width / 64) * 8);
        return (float)BitConverter.ToHalf(bytes, texel);
    }
}
