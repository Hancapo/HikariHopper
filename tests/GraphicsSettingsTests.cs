using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

// The graphics settings, kept in a scratch registry key (ExplorerTestBase).
public sealed class GraphicsSettingsTests : ExplorerTestBase
{
    // On the UI thread, where the app changes them: a viewer another test left open hears each change.
    [AvaloniaFact]
    public void They_start_as_the_viewer_draws_and_keep_what_is_chosen()
    {
        Assert.True(GraphicsSettings.Antialiasing);
        Assert.True(GraphicsSettings.Bloom);
        Assert.True(GraphicsSettings.Shadows);
        Assert.Equal(2048, GraphicsSettings.ShadowResolution);
        Assert.Equal(8, GraphicsSettings.Anisotropy);
        Assert.Equal(100, GraphicsSettings.RenderScale);
        Assert.Equal(512, GraphicsSettings.ReflectionWidth);

        int changes = 0;
        void Count() => changes++;
        GraphicsSettings.Changed += Count;
        try
        {
            GraphicsSettings.Antialiasing = false;
            GraphicsSettings.Bloom = false;
            GraphicsSettings.ShadowResolution = 4096;
            GraphicsSettings.Anisotropy = 16;
            GraphicsSettings.RenderScale = 150;
            GraphicsSettings.ReflectionWidth = 1024;
            Assert.Equal(6, changes);
            // Setting what is there already tells no one.
            GraphicsSettings.Anisotropy = 16;
            GraphicsSettings.Bloom = false;
            Assert.Equal(6, changes);
        }
        finally
        {
            GraphicsSettings.Changed -= Count;
        }
        Assert.False(GraphicsSettings.Antialiasing);
        Assert.False(GraphicsSettings.Bloom);
        Assert.Equal(4096, GraphicsSettings.ShadowResolution);
        Assert.Equal(16, GraphicsSettings.Anisotropy);
        Assert.Equal(150, GraphicsSettings.RenderScale);
        Assert.Equal(1024, GraphicsSettings.ReflectionWidth);

        // A value that isn't a choice, saved by hand, reads as the nearest one.
        AppSettings.SetInt(GraphicsSettings.ShadowResolutionKey, 3000);
        AppSettings.SetInt(GraphicsSettings.AnisotropyKey, 2);
        Assert.Equal(2048, GraphicsSettings.ShadowResolution);
        Assert.Equal(1, GraphicsSettings.Anisotropy);
        // Even one as far off as a number goes.
        AppSettings.SetInt(GraphicsSettings.ShadowResolutionKey, int.MinValue + 1024);
        Assert.Equal(1024, GraphicsSettings.ShadowResolution);
    }

    [AvaloniaFact]
    public void The_graphics_page_shows_and_sets_them()
    {
        var settings = new SettingsDialog();
        var window = new Window { Width = 880, Height = 640, Content = settings };
        window.Show();
        settings.GraphicsGroup.Choose();
        TestWait.Pump(50);
        Assert.Equal("Graphics", settings.Group);
        Assert.True(settings.GraphicsPage.IsVisible);
        Assert.False(settings.GamesPage.IsVisible);
        // As drawn now: CMAA and bloom on, High shadows, 8× filtering.
        Assert.True(settings.AntialiasingSwitch.IsOn);
        Assert.True(settings.BloomSwitch.IsOn);
        Assert.Equal(["Off", "Medium", "High", "Ultra"], settings.ShadowSwitch.Sides.Select(side => side.Text));
        Assert.Equal(2, settings.ShadowSwitch.SelectedIndex);
        Assert.Equal(["Trilinear", "4×", "8×", "16×"], settings.FilteringSwitch.Sides.Select(side => side.Text));
        Assert.Equal(2, settings.FilteringSwitch.SelectedIndex);
        Assert.Equal(["75%", "100%", "150%", "200%"], settings.ScaleSwitch.Sides.Select(side => side.Text));
        Assert.Equal(1, settings.ScaleSwitch.SelectedIndex);
        Assert.Equal(["Low", "Medium", "High"], settings.ReflectionSwitch.Sides.Select(side => side.Text));
        Assert.Equal(1, settings.ReflectionSwitch.SelectedIndex);

        settings.AntialiasingSwitch.IsOn = false;
        settings.BloomSwitch.IsOn = false;
        Assert.False(GraphicsSettings.Antialiasing);
        Assert.False(GraphicsSettings.Bloom);
        // Off turns the shadows off and keeps their resolution for when they're back; another side picks one.
        Pick(settings.ShadowSwitch, 0);
        Assert.False(GraphicsSettings.Shadows);
        Assert.Equal(2048, GraphicsSettings.ShadowResolution);
        Assert.Equal(0, settings.ShadowSwitch.SelectedIndex);
        Pick(settings.ShadowSwitch, 3);
        Assert.True(GraphicsSettings.Shadows);
        Assert.Equal(4096, GraphicsSettings.ShadowResolution);
        Pick(settings.FilteringSwitch, 0);
        Assert.Equal(1, GraphicsSettings.Anisotropy);
        Pick(settings.ScaleSwitch, 3);
        Assert.Equal(200, GraphicsSettings.RenderScale);
        Pick(settings.ReflectionSwitch, 2);
        Assert.Equal(1024, GraphicsSettings.ReflectionWidth);
        window.Close();

        // Opened again, it shows them as they were left.
        var again = new SettingsDialog();
        Assert.False(again.AntialiasingSwitch.IsOn);
        Assert.False(again.BloomSwitch.IsOn);
        Assert.Equal(3, again.ShadowSwitch.SelectedIndex);
        Assert.Equal(0, again.FilteringSwitch.SelectedIndex);
        Assert.Equal(3, again.ScaleSwitch.SelectedIndex);
        Assert.Equal(2, again.ReflectionSwitch.SelectedIndex);

        // A side clicked.
        void Pick(Views.Controls.SegmentedSwitch switcher, int side)
        {
            Avalonia.Point point = Avalonia.VisualExtensions.TranslatePoint(switcher,
                new Avalonia.Point(switcher.SideStart(side) + 6, switcher.Bounds.Height / 2), window)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, point, Avalonia.Input.MouseButton.Left);
            TestWait.Settle();
        }
    }

    [AvaloniaFact]
    public void Shadows_turned_in_one_viewer_are_off_in_every_viewer()
    {
        byte[] ydr = TestDrawables.Triangle();
        var first = new ModelViewer(new ModelFile("a.ydr", ydr));
        var second = new ModelViewer(new ModelFile("b.ydr", ydr));
        var window = new Window { Width = 900, Height = 600, Content = new Grid { ColumnDefinitions = new("*,*"), Children = { first, second } } };
        Grid.SetColumn(second, 1);
        window.Show();
        TestWait.Until(() => first.Reading.IsCompleted && second.Reading.IsCompleted, "the models to be read");
        TestWait.Pump(50);
        Assert.True(first.ShadowsButton.Sunken && second.ShadowsButton.Sunken);
        Assert.True(first.Shortcut('H'));
        Assert.False(GraphicsSettings.Shadows);
        Assert.False(first.ShadowsButton.Sunken);
        Assert.False(second.ShadowsButton.Sunken);
        // From Settings too.
        GraphicsSettings.Shadows = true;
        Assert.True(first.ShadowsButton.Sunken && second.ShadowsButton.Sunken);

        // A viewer read before it is shown, while shadows were turned off, shows them off once it is.
        var later = new ModelViewer(new ModelFile("c.ydr", ydr));
        TestWait.Until(() => later.Reading.IsCompleted, "the model to be read");
        TestWait.Pump(50);
        Assert.True(later.ShadowsButton.Sunken);
        GraphicsSettings.Shadows = false;
        var other = new Window { Width = 400, Height = 300, Content = later };
        other.Show();
        TestWait.Pump(50);
        Assert.False(later.ShadowsButton.Sunken);
        other.Close();
        window.Close();
    }

    [Fact]
    public void A_render_scale_draws_the_same_picture_into_the_window_but_along_its_edges()
    {
        Assert.Equal((1500, 750), ViewportRenderer.ScaledSize(1000, 500, 1.5f));
        Assert.Equal((75, 38), ViewportRenderer.ScaledSize(100, 50, .75f));
        Assert.Equal((8192, 4000), ViewportRenderer.ScaledSize(6000, 2000, 2));
        GpuDevice gpu;
        try
        {
            gpu = TestGpu.Device();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        // The same model at every scale: the same picture but along its edges, which supersampling smooths and a
        // smaller target softens.
        DrawableScene scene = ModelContent.Read("tri.ydr", TestDrawables.Triangle()).Surfaces!;
        // Readback rows are a multiple of 256 bytes.
        const int Size = 128;
        var frames = new Dictionary<int, byte[]>();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            renderer.Antialiasing = false;
            renderer.Show(scene);
            renderer.Frame(scene.Box);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            foreach (int scale in GraphicsSettings.RenderScales)
            {
                renderer.RenderScale = scale / 100f;
                renderer.RenderTo(view);
                WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
                {
                    Size = Size * Size * 4,
                    Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
                });
                WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
                encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = target },
                    new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = Size * 4, RowsPerImage = Size } },
                    new WebGpuSharp.Extent3D(Size, Size, 1));
                gpu.Queue.Submit(encoder.Finish());
                readback.MapSync(WebGpuSharp.MapMode.Read);
                frames[scale] = readback.GetConstMappedRange<byte[], byte>(0, Size * Size * 4, span => span.ToArray());
                readback.Unmap();
                readback.Destroy();
            }
            target.Destroy();
            renderer.Dispose();
        }
        int Different(byte[] a, byte[] b)
        {
            int count = 0;
            for (int at = 0; at < a.Length; at += 4)
                if (Math.Abs(a[at] - b[at]) + Math.Abs(a[at + 1] - b[at + 1]) + Math.Abs(a[at + 2] - b[at + 2]) > 24)
                    count++;
            return count;
        }
        // The model is there to see at every scale.
        Assert.All(frames.Values, frame => Assert.True(Different(frame, new byte[frame.Length]) > 200));
        foreach (int scale in new[] { 75, 150, 200 })
        {
            int changed = Different(frames[100], frames[scale]);
            Assert.True(changed < Size * Size / 10, $"{changed} pixels differ at {scale}%");
        }
        // Supersampled, the edges are smoothed: some of them change.
        Assert.NotEqual(frames[100], frames[200]);
    }

    [Fact]
    public void The_sky_is_drawn_whole_into_the_reflection_map_at_every_size()
    {
        GpuDevice gpu;
        try
        {
            gpu = TestGpu.Device();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        // The map's last mip is its mean: the same sky, the same mean, whatever its size.
        var means = new List<System.Numerics.Vector3>();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            foreach (int width in GraphicsSettings.ReflectionWidths)
            {
                renderer.ReflectionWidth = (uint)width;
                WebGpuSharp.Texture map = renderer.SkyReflectionMap!;
                WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
                {
                    Size = 256,
                    Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
                });
                WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
                encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = map, MipLevel = renderer.ReflectionMips - 1 },
                    new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = 256, RowsPerImage = 1 } },
                    new WebGpuSharp.Extent3D(1, 1, 1));
                gpu.Queue.Submit(encoder.Finish());
                readback.MapSync(WebGpuSharp.MapMode.Read);
                byte[] texel = readback.GetConstMappedRange<byte[], byte>(0, 8, span => span.ToArray());
                readback.Unmap();
                readback.Destroy();
                float Half(int at) => (float)BitConverter.ToHalf(texel, at);
                means.Add(new(Half(0), Half(2), Half(4)));
            }
            renderer.Dispose();
        }
        Assert.All(means, mean => Assert.True(mean.X + mean.Y + mean.Z > 0, $"{mean}"));
        foreach (System.Numerics.Vector3 mean in means)
            Assert.True((mean - means[1]).Length() < means[1].Length() * .05f, $"{mean} against {means[1]}");
    }

    [Fact]
    public void The_renderer_draws_with_each_of_them_changed_with_a_model_shown()
    {
        GpuDevice gpu;
        try
        {
            gpu = TestGpu.Device();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        DrawableScene scene = ModelContent.Read("tri.ydr", TestDrawables.Triangle()).Surfaces!;
        const int Size = 64;
        (WebGpuSharp.ErrorType Type, string Message) result;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            renderer.Show(scene);
            renderer.Frame(scene.Box);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.RenderTo(view);
            foreach (int resolution in GraphicsSettings.ShadowResolutions)
            {
                renderer.ShadowResolution = resolution;
                Assert.Equal(resolution, renderer.ShadowResolution);
                renderer.RenderTo(view);
            }
            foreach (int anisotropy in GraphicsSettings.Anisotropies)
            {
                renderer.Anisotropy = anisotropy;
                Assert.Equal(anisotropy, renderer.Anisotropy);
                renderer.RenderTo(view);
            }
            foreach (int width in GraphicsSettings.ReflectionWidths)
            {
                renderer.ReflectionWidth = (uint)width;
                Assert.Equal(ViewportRenderer.MipsFor((uint)width), renderer.ReflectionMips);
                renderer.RenderTo(view);
            }
            foreach (int scale in GraphicsSettings.RenderScales)
            {
                renderer.RenderScale = scale / 100f;
                renderer.RenderTo(view);
            }
            renderer.Bloom = false;
            renderer.Antialiasing = false;
            renderer.Shadows = false;
            renderer.RenderTo(view);
            // Changed with nothing shown, then shown again.
            renderer.Show((DrawableScene?)null);
            renderer.Anisotropy = 4;
            renderer.Show(scene);
            renderer.RenderTo(view);
            target.Destroy();
            renderer.Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == WebGpuSharp.ErrorType.NoError, $"{result.Type}: {result.Message}");
    }
}
