using System.Numerics;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's fog over its world: its constants as the game sets them for the camera, none where no world's sky is shown,
// and a frame of a far surface fogged by the world's fog pass.
[Collection(ExplorerCollection.Name)]
public sealed class GtaFogTests
{
    private static string Weather(params (string Field, float Value)[] fields) => $"""
        <timecycle_keyframe_data version="1.000000">
          <cycle name="EXTRASUNNY" regions="1">
            <region name="GLOBAL">
        {string.Join("\n", fields.Select(f => $"      <{f.Field}>{string.Join(' ', Enumerable.Repeat(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), 13))}</{f.Field}>"))}
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    private static readonly GtaSkyVisuals Visuals = GtaSkyVisuals.From(null);

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

    // The fog's end (globalFogParams[0].y) for a camera at this height.
    private static float FogEnd(float height, GtaSkyVisuals visuals) => WithWeather(() =>
    {
        var values = new Vector4[GtaSky.Bytes / 16];
        GtaSky.Write(values, visuals, 13, null, new Vector3(0, 0, height), 0);
        return values[GtaSky.FogStart].Y;
    }, ("far_clip", 10000), ("fog_start", 0), ("fog_horizon_tint_scale", 4));

    [Fact]
    public void The_fog_ends_further_out_the_higher_the_camera_as_visualsettings_grows_the_far_clip()
    {
        // The game's defaults: twice as far from 100 metres up to 250.
        Assert.Equal(10000, FogEnd(50, Visuals));
        Assert.Equal(15000, FogEnd(175, Visuals), 1);
        Assert.Equal(20000, FogEnd(400, Visuals));
        // The file's own, where it has them.
        GtaSkyVisuals file = GtaSkyVisuals.From(GameVisualSettings.Parse("misc.Multiplier.heightStart 0\nmisc.Multiplier.heightEnd 100\nmisc.Multiplier.farClipMultiplier 3\n"));
        Assert.Equal(20000, FogEnd(50, file), 1);
        Assert.Equal(30000, FogEnd(400, file));
        // The horizon's tint is over that end.
        WithWeather(() =>
        {
            var values = new Vector4[GtaSky.Bytes / 16];
            GtaSky.Write(values, Visuals, 13, null, new Vector3(0, 0, 400), 0);
            Assert.Equal(4f / 20000, values[GtaSky.FogStart + 1].Z, 9);
            return 0;
        }, ("far_clip", 10000), ("fog_horizon_tint_scale", 4));
    }

    [Fact]
    public void With_no_worlds_sky_the_world_has_no_fog_and_its_sums_stay_finite()
    {
        var values = new Vector4[GtaSky.FogVectors];
        values.AsSpan().Fill(new Vector4(7));
        GtaSky.NoFog(values);
        // No ground fog and no haze: their density and alpha nought; a falloff to divide by, and powers of one.
        Assert.Equal(0, values[1].W);
        Assert.Equal(0, values[1].Y);
        Assert.Equal(0, values[2].Y);
        Assert.Equal(1, values[2].Z);
        Assert.Equal(new Vector4(0, 0, 1, 1), values[3]);
        Assert.Equal(new Vector4(0, 0, 1, 1), values[4]);
        Assert.All(values[5..], value => Assert.Equal(Vector4.Zero, value));
    }

    // The fog along each ray (Gta5/fog.wgsl's gta_fog, with its haze) for these constants, worked out on the GPU.
    private static Vector4[] Fog(ReadOnlySpan<Vector4> constants, Vector3[] rays)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            HikariHopper.Gpu.Device device = gpu.Device;
            device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            HikariHopper.Gpu.ShaderModule shader = device.CreateShaderModuleWGSL(new HikariHopper.Gpu.ShaderModuleWGSLDescriptor
            {
                Label = "test/gta-fog-rays",
                Code = TestShaders.Module("test/gta-fog-rays"),
            });
            HikariHopper.Gpu.BindGroupLayout layout = device.CreateBindGroupLayout(new HikariHopper.Gpu.BindGroupLayoutDescriptor
            {
                Entries =
                [
                    new HikariHopper.Gpu.BindGroupLayoutEntry { Binding = 0, Visibility = HikariHopper.Gpu.ShaderStage.Compute, Buffer = new HikariHopper.Gpu.BufferBindingLayout { Type = HikariHopper.Gpu.BufferBindingType.Uniform } },
                    new HikariHopper.Gpu.BindGroupLayoutEntry { Binding = 1, Visibility = HikariHopper.Gpu.ShaderStage.Compute, Buffer = new HikariHopper.Gpu.BufferBindingLayout { Type = HikariHopper.Gpu.BufferBindingType.ReadOnlyStorage } },
                    new HikariHopper.Gpu.BindGroupLayoutEntry { Binding = 2, Visibility = HikariHopper.Gpu.ShaderStage.Compute, Buffer = new HikariHopper.Gpu.BufferBindingLayout { Type = HikariHopper.Gpu.BufferBindingType.Storage } },
                ],
            });
            HikariHopper.Gpu.ComputePipeline pipeline = device.CreateComputePipelineSync(new HikariHopper.Gpu.ComputePipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new HikariHopper.Gpu.PipelineLayoutDescriptor { BindGroupLayouts = [layout] }),
                Compute = new HikariHopper.Gpu.ComputeState { Module = shader, EntryPoint = "cs_fog" },
            });
            ulong bytes = (ulong)rays.Length * 16;
            HikariHopper.Gpu.Buffer uniforms = device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor { Size = (ulong)constants.Length * 16, Usage = HikariHopper.Gpu.BufferUsage.Uniform | HikariHopper.Gpu.BufferUsage.CopyDst });
            HikariHopper.Gpu.Buffer input = device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor { Size = bytes, Usage = HikariHopper.Gpu.BufferUsage.Storage | HikariHopper.Gpu.BufferUsage.CopyDst });
            HikariHopper.Gpu.Buffer output = device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor { Size = bytes, Usage = HikariHopper.Gpu.BufferUsage.Storage | HikariHopper.Gpu.BufferUsage.CopySrc });
            HikariHopper.Gpu.Buffer readback = device.CreateBuffer(new HikariHopper.Gpu.BufferDescriptor { Size = bytes, Usage = HikariHopper.Gpu.BufferUsage.CopyDst | HikariHopper.Gpu.BufferUsage.MapRead });
            gpu.Queue.WriteBuffer(uniforms, 0, constants);
            gpu.Queue.WriteBuffer(input, 0, (ReadOnlySpan<Vector4>)[.. rays.Select(ray => new Vector4(ray, 0))]);
            HikariHopper.Gpu.BindGroup group = device.CreateBindGroup(new HikariHopper.Gpu.BindGroupDescriptor
            {
                Layout = layout,
                Entries =
                [
                    new HikariHopper.Gpu.BindGroupEntry { Binding = 0, Buffer = uniforms, Size = uniforms.GetSize() },
                    new HikariHopper.Gpu.BindGroupEntry { Binding = 1, Buffer = input, Size = bytes },
                    new HikariHopper.Gpu.BindGroupEntry { Binding = 2, Buffer = output, Size = bytes },
                ],
            });
            HikariHopper.Gpu.CommandEncoder encoder = device.CreateCommandEncoder();
            HikariHopper.Gpu.ComputePassEncoder pass = encoder.BeginComputePass();
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, group);
            pass.DispatchWorkgroups((uint)rays.Length, 1, 1);
            pass.End();
            encoder.CopyBufferToBuffer(output, 0, readback, 0, bytes);
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(HikariHopper.Gpu.MapMode.Read);
            Vector4[] fogged = readback.GetConstMappedRange<Vector4[], Vector4>(0, (nuint)rays.Length, span => span.ToArray());
            readback.Unmap();
            var (type, message) = device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            foreach (HikariHopper.Gpu.Buffer buffer in new[] { uniforms, input, output, readback })
                buffer.Destroy();
            return fogged;
        }
    }

    // Rays far down, level, a little down and up, as a camera high over the world casts them.
    private static readonly Vector3[] Rays = [new(0, 0, -5000), new(3000, 0, -4000), new(5000, 0, 0), new(5000, 0, -20), new(0, 3000, 1000)];

    [Fact]
    public void With_no_fog_none_is_drawn_along_any_ray_even_far_down()
    {
        var values = new Vector4[GtaSky.FogVectors];
        GtaSky.NoFog(values);
        // The falloff of one would raise e past what a float holds looking five kilometres down.
        Assert.All(Fog(values, Rays), fog =>
        {
            Assert.True(float.IsFinite(fog.X) && float.IsFinite(fog.Y) && float.IsFinite(fog.Z), $"{fog}");
            Assert.Equal(0, fog.W);
        });
    }

    [Fact]
    public void A_ground_fog_with_no_falloff_is_as_thick_along_a_ray_as_its_density_makes_it()
    {
        // Ground fog only, half as dense as would fill a kilometre: no haze, no falloff with height.
        var values = new Vector4[GtaSky.FogVectors];
        GtaSky.NoFog(values);
        const float Density = .5f / 1000;
        values[1].W = -Density;
        values[2] = new(0, 1, 0, 0);
        Vector4[] fogged = Fog(values, Rays);
        for (int i = 0; i < Rays.Length; i++)
            Assert.Equal(1 - MathF.Exp(-Density * Rays[i].Length()), fogged[i].W, 4);
    }

    [Fact]
    public void A_far_surface_is_fogged_by_the_worlds_fog_pass_and_its_fog_rays_with_no_GPU_errors()
    {
        // Thick red fog by day, and the same weather with none.
        (string, float)[] sky = [("sky_hdr", 1), ("sky_zenith_col_b", 1), ("sky_zenith_col_inten", 1)];
        (string, float)[] fog = [.. sky, ("fog_start", 0), ("far_clip", 10000), ("fog_density", 50), ("fog_falloff", .1f), ("fog_alpha", 1),
            ("fog_haze_alpha", 1), ("fog_haze_density", 5), ("fog_hdr", 4), ("fog_haze_hdr", 4), ("fogray_intensity", .9f), ("fogray_farfade", 200),
            .. new[] { "fog_col", "fog_east_col", "fog_near_col", "fog_haze_col", "fog_moon_col" }.SelectMany(c => new[] { (c + "_r", 1f), (c + "_g", 0f), (c + "_b", 0f) })];
        (string, float)[] clear = [.. sky, ("fog_start", 0), ("far_clip", 10000), ("fog_density", 0), ("fog_alpha", 0), ("fog_haze_alpha", 0)];
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            const int Width = 64, Height = 32;
            HikariHopper.Gpu.Texture target = gpu.Device.CreateTexture(new HikariHopper.Gpu.TextureDescriptor
            {
                Usage = HikariHopper.Gpu.TextureUsage.RenderAttachment | HikariHopper.Gpu.TextureUsage.CopySrc,
                Dimension = HikariHopper.Gpu.TextureDimension.D2,
                Size = new HikariHopper.Gpu.Extent3D(Width, Height, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            HikariHopper.Gpu.TextureView view = target.CreateView();
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            renderer.Resize(Width, Height);
            renderer.WorldShown = true;
            renderer.Shadows = true;
            var eye = new Vector3(0, 0, 1500);
            renderer.Camera.Frame(eye, 20);
            renderer.Camera.Free = true;
            // Looking down at a great triangle on the ground, about one and a half kilometres away.
            renderer.Camera.Place(eye, MathF.PI / 4, 1.2f);
            renderer.SetTime(13);
            FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
            var key = new RedFury.World.WorldAssetKey(1, 2);
            Assert.True(renderer.AddWorldAsset(Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]),
                new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            Matrix4x4 placed = Matrix4x4.CreateScale(20000) * Matrix4x4.CreateTranslation(-10000, -10000, 0);
            var node = new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, 0), null, key, placed, Vector3.Zero, 20000, 100000, 0, 0);
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([node], [key]));
            renderer.ShowWorldSky(GtaSkyTests.Octahedron());
            uint Middle(params (string, float)[] weather) => WithWeather(() =>
            {
                renderer.RenderTo(view);
                return GtaSkyTests.Pixel(gpu, target, Width / 2, Height / 2);
            }, weather);
            uint clearPixel = Middle(clear);
            Assert.True(renderer.WorldFogged);
            Assert.False(renderer.WorldFogRays);
            uint foggedPixel = Middle(fog);
            // In the sun's shadow's reach, with the timecycle's fog rays: they're drawn.
            Assert.True(renderer.WorldFogRays);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            // Red fog over the white surface: its green all but gone, its red and blue apart.
            static (int Low, int Green, int High) Channels(uint pixel) => ((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF));
            var (clearLow, clearGreen, clearHigh) = Channels(clearPixel);
            var (fogLow, fogGreen, fogHigh) = Channels(foggedPixel);
            Assert.True(clearGreen > 128 && Math.Abs(clearLow - clearHigh) < 16, $"{clearPixel:X8}");
            Assert.True(fogGreen < clearGreen / 2 && Math.Abs(fogLow - fogHigh) > 64, $"{clearPixel:X8} {foggedPixel:X8}");
            renderer.ClearWorld();
            Assert.False(renderer.HasWorldSky);
            renderer.Dispose();
            target.Destroy();
        }
    }
}
