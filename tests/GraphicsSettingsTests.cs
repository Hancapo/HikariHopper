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
    [Fact]
    public void They_start_as_the_viewer_draws_and_keep_what_is_chosen()
    {
        Assert.True(GraphicsSettings.Antialiasing);
        Assert.True(GraphicsSettings.Bloom);
        Assert.True(GraphicsSettings.Shadows);
        Assert.Equal(2048, GraphicsSettings.ShadowResolution);
        Assert.Equal(8, GraphicsSettings.Anisotropy);

        int changes = 0;
        void Count() => changes++;
        GraphicsSettings.Changed += Count;
        try
        {
            GraphicsSettings.Antialiasing = false;
            GraphicsSettings.Bloom = false;
            GraphicsSettings.ShadowResolution = 4096;
            GraphicsSettings.Anisotropy = 16;
            Assert.Equal(4, changes);
            // Setting what is there already tells no one.
            GraphicsSettings.Anisotropy = 16;
            GraphicsSettings.Bloom = false;
            Assert.Equal(4, changes);
        }
        finally
        {
            GraphicsSettings.Changed -= Count;
        }
        Assert.False(GraphicsSettings.Antialiasing);
        Assert.False(GraphicsSettings.Bloom);
        Assert.Equal(4096, GraphicsSettings.ShadowResolution);
        Assert.Equal(16, GraphicsSettings.Anisotropy);

        // A value that isn't a choice, saved by hand, reads as the nearest one.
        AppSettings.SetInt(GraphicsSettings.ShadowResolutionKey, 3000);
        AppSettings.SetInt(GraphicsSettings.AnisotropyKey, 2);
        Assert.Equal(2048, GraphicsSettings.ShadowResolution);
        Assert.Equal(1, GraphicsSettings.Anisotropy);
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
        window.Close();

        // Opened again, it shows them as they were left.
        var again = new SettingsDialog();
        Assert.False(again.AntialiasingSwitch.IsOn);
        Assert.False(again.BloomSwitch.IsOn);
        Assert.Equal(3, again.ShadowSwitch.SelectedIndex);
        Assert.Equal(0, again.FilteringSwitch.SelectedIndex);

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
        window.Close();
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
