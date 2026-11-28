using System.Numerics;
using CommonFury.Numerics;
using FiveFury.Colors;
using FiveFury.Ymap;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class GtaDistantLightsTests
{
    [Theory]
    [InlineData(12, 0)]
    [InlineData(18.5f, 0)]
    [InlineData(19, 0)]
    [InlineData(19.5f, .5f)]
    [InlineData(20, 1)]
    [InlineData(23.75f, 1)]
    [InlineData(0, 1)]
    [InlineData(4.9f, 1)]
    [InlineData(5.25f, .75f)]
    [InlineData(6, 0)]
    [InlineData(7, 0)]
    public void The_distant_lights_fade_in_the_hour_before_they_start_and_out_the_hour_before_they_end(float hours, float expected) =>
        Assert.Equal(expected, GtaDistantLights.HourFade(hours, 20, 6), 4);

    // A distlodlights_* map: its lights in a row along x, the first of them its street lights, streamed in within its box.
    private static Ymap Map(LodLightCategory category, int count, int streetLights, Aabb3 streaming, Vector3 at, uint rgbi = 0xFFFF0000)
    {
        var ymap = new Ymap { StreamingExtents = streaming };
        ymap.DistantLights.Category = category;
        ymap.DistantLights.StreetLightCount = (ushort)streetLights;
        for (int i = 0; i < count; i++)
            ymap.DistantLights.Lights.Add(new DistantLodLight { Position = at + new Vector3(i, 0, 0), Color = RgbIntensity.FromPacked(rgbi) });
        return ymap;
    }

    [Fact]
    public void Only_the_medium_and_large_maps_lights_are_kept_and_each_maps_first_are_its_street_lights()
    {
        var everywhere = new Aabb3(new Vector3(-10000), new Vector3(10000));
        GtaDistantLights lights = GtaDistantLights.From(
        [
            Map(LodLightCategory.Small, 5, 0, everywhere, Vector3.Zero),
            Map(LodLightCategory.Medium, 3, 2, everywhere, Vector3.Zero),
            Map(LodLightCategory.Large, 2, 0, new Aabb3(new Vector3(500, -100, -100), new Vector3(900, 100, 100)), Vector3.Zero),
        ]);
        Assert.Equal(5, lights.Lights.Length);
        Assert.Equal([1u | GtaDistantLights.KindStreet, 1u | GtaDistantLights.KindStreet, 1u, 2u, 2u], lights.Lights.Select(light => light.Kind));
        Assert.Equal(0xFFFF0000u, lights.Lights[0].Rgbi);
        // At the origin only the medium map is streamed in; inside the large one's box, both, as one run.
        var runs = new List<(int First, int Count)>();
        lights.Streamed(Vector3.Zero, runs);
        Assert.Equal([(0, 3)], runs);
        lights.Streamed(new Vector3(600, 0, 0), runs);
        Assert.Equal([(0, 5)], runs);
    }

    [Fact]
    public void A_distant_light_far_ahead_is_drawn_at_night_and_not_by_day()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            // Wide enough for its smallest (12/1024 of the half width) to cover pixels.
            const int Width = 1024, Height = 512;
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
            renderer.ShowWorldSky(GtaCoronaTests.Dome());
            // A white sprite, and a medium light 600 m straight ahead, past its corona range, its map streamed in here.
            var glow = new FiveFury.Textures.Texture("distant_light", 16, 16, CommonFury.Textures.TextureFormat.R8Unorm, 1, Enumerable.Repeat((byte)255, 256).ToArray());
            GtaDistantLights lights = GtaDistantLights.From([Map(LodLightCategory.Medium, 1, 0, new Aabb3(new Vector3(-5000), new Vector3(5000)),
                eye + renderer.Camera.Forward * 600)]);
            renderer.ShowWorldCoronas(new GtaCoronaData(glow, null, new GtaCoronaVisuals(), GtaLodLights.Empty, null, lights, glow, null));
            float Middle(float hours)
            {
                renderer.SetTime(hours);
                renderer.RenderTo(target.CreateView());
                // HdrRed reads at a 64 by 32 grid over the target: its middle.
                return GtaCoronaTests.HdrRed(gpu, renderer, 32, 16);
            }
            float day = Middle(12);
            Assert.Equal(0, renderer.WorldDistantLightsDrawn);
            float night = Middle(23);
            Assert.Equal(1, renderer.WorldDistantLightsDrawn);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            Assert.True(night > day + .5f, $"{night} at night, {day} by day");
            renderer.ClearWorld();
            renderer.Dispose();
            target.Destroy();
        }
    }
}
