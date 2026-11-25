using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's exposure in its world: measured on the frame as it is, and eased towards the target over the frames as the
// game's post-process eases it (PS_CalculateExposure); taken at once on a camera cut and in the 3D viewer.
[Collection(ExplorerCollection.Name)]
public sealed class ExposureAdaptationTests
{
    private const float Frame = 1 / 60f;

    // A world's sky at 14:00 over an empty world, looking level; the test, given the renderer and a frame to draw.
    private static void WithWorldSky(Action<ViewportRenderer, Action> test)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            const int Width = 128, Height = 64;
            HikariHopper.Gpu.Texture target = gpu.Device.CreateTexture(new HikariHopper.Gpu.TextureDescriptor
            {
                Usage = HikariHopper.Gpu.TextureUsage.RenderAttachment | HikariHopper.Gpu.TextureUsage.CopySrc,
                Dimension = HikariHopper.Gpu.TextureDimension.D2,
                Size = new HikariHopper.Gpu.Extent3D(Width, Height, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(HikariHopper.Gpu.ErrorFilter.Validation);
            renderer.Resize(Width, Height);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(0, 0, 50), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(0, 0, 50), 0, 0);
            renderer.SetTime(14);
            renderer.ShowWorldSky(GtaSkyTests.Octahedron());
            Assert.True(renderer.HasWorldSky);
            HikariHopper.Gpu.TextureView view = target.CreateView();
            test(renderer, () => renderer.RenderTo(view));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == HikariHopper.Gpu.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_worlds_exposure_eases_towards_its_target_as_the_game_does_and_takes_it_at_once_on_a_cut() => WithWorldSky((renderer, render) =>
    {
        GtaToneMapVisuals curve = Timecycle.ToneMap;
        // A frame given no time takes its target at once.
        render();
        var (_, settled, target, _) = renderer.MeasuredExposure();
        Assert.Equal(target, settled, 4);
        // The target two stops lower: each frame moves the exposure by the gap times saturate(mult x dt) / dt stops a
        // second, at most the largest step, over the frame's time.
        renderer.Exposure = -2;
        renderer.FrameSeconds = Frame;
        render();
        var (_, first, lower, _) = renderer.MeasuredExposure();
        Assert.True(lower < target - 1, $"{target} -> {lower}");
        float rate = MathF.Min(curve.AdaptationMaxStep, (settled - lower) * Math.Clamp(curve.AdaptationStepMult * Frame, 0, 1) / Frame);
        Assert.Equal(settled - rate * Frame, first, 3);
        Assert.True(first > lower + .5f, $"{first} jumped to {lower}");
        // Frame after frame it gets there, and stays.
        float last = first;
        for (int i = 0; i < 600 && MathF.Abs(last - lower) > 1e-4f; i++)
        {
            renderer.FrameSeconds = Frame;
            render();
            float now = renderer.MeasuredExposure().Exposure;
            Assert.True(now <= last + 1e-5f && now >= lower - 1e-5f, $"{last} -> {now}, towards {lower}");
            last = now;
        }
        Assert.Equal(lower, last, 3);
        // Back up two stops, then a cut: the camera a kilometre away in one frame takes the target at once.
        renderer.Exposure = 0;
        renderer.FrameSeconds = Frame;
        render();
        Assert.True(renderer.MeasuredExposure().Exposure < target - 1);
        renderer.Camera.Place(new Vector3(1000, 0, 50), 0, 0);
        renderer.FrameSeconds = Frame;
        render();
        var (_, cut, again, _) = renderer.MeasuredExposure();
        Assert.Equal(again, cut, 4);
    });

    [Fact]
    public void A_worlds_exposure_is_measured_on_its_frame() => WithWorldSky((renderer, render) =>
    {
        // Up at the sky, then down below the horizon, where the empty world's sky is dark: the frame's mean follows what
        // the camera sees, as the game's does; the viewer's fixed view wouldn't move.
        renderer.Camera.Place(new Vector3(0, 0, 50), 0, -MathF.PI / 2 + .01f);
        render();
        float up = renderer.MeasuredExposure().Mean;
        renderer.Camera.Place(new Vector3(0, 0, 50), 0, MathF.PI / 2 - .01f);
        render();
        float down = renderer.MeasuredExposure().Mean;
        Assert.True(down < up * .8f, $"{up} {down}");
    });

    [Fact]
    public void The_viewers_exposure_takes_its_target_at_once()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            HikariHopper.Gpu.Texture target = gpu.Device.CreateTexture(new HikariHopper.Gpu.TextureDescriptor
            {
                Usage = HikariHopper.Gpu.TextureUsage.RenderAttachment | HikariHopper.Gpu.TextureUsage.CopySrc,
                Dimension = HikariHopper.Gpu.TextureDimension.D2,
                Size = new HikariHopper.Gpu.Extent3D(64, 64, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            renderer.Resize(64, 64);
            renderer.Show(new ViewportMesh(Vector3.Zero));
            renderer.SetTime(14);
            HikariHopper.Gpu.TextureView view = target.CreateView();
            renderer.RenderTo(view);
            // The exposure two stops down, a frame with a time: the viewer's fixed view takes the target at once.
            renderer.Exposure = -2;
            renderer.FrameSeconds = Frame;
            renderer.RenderTo(view);
            var (_, exposure, wanted, _) = renderer.MeasuredExposure();
            Assert.Equal(wanted, exposure, 4);
            renderer.Dispose();
            target.Destroy();
        }
    }
}
