using System.Numerics;
using Avalonia.Headless.XUnit;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class PickingTests
{
    [Fact]
    public void A_pixels_ray_goes_from_the_eye_through_it()
    {
        var camera = new OrbitCamera();
        camera.Frame(new Vector3(3, 4, 5), 2);
        // The middle of the view looks along the camera; the right edge as far right as the field of view goes.
        (Vector3 from, Vector3 direction) = camera.Ray(99.5f, 49.5f, 200, 100);
        Assert.Equal(camera.Eye, from);
        Assert.True(Vector3.Dot(direction, camera.Forward) > .9999f);
        (_, Vector3 edge) = camera.Ray(199.5f, 49.5f, 200, 100);
        Assert.Equal(OrbitCamera.TanHalf * 2, Vector3.Dot(edge, camera.Right) / Vector3.Dot(edge, camera.Forward), 2);
        (_, Vector3 top) = camera.Ray(99.5f, -.5f, 200, 100);
        Assert.Equal(OrbitCamera.TanHalf, Vector3.Dot(top, camera.Up) / Vector3.Dot(top, camera.Forward), 2);
    }

    [Fact]
    public void A_ray_picks_the_nearest_drawn_part_it_meets()
    {
        DrawableScene scene = FragmentViewerTests.DamagedScene();
        var visibility = new PartVisibility(scene);
        // Four triangles side by side on the ground, a chassis, a door and its handle, an extra: straight down onto each.
        int? Down(float x) => visibility.PartAt(new Vector3(x, .25f, 5), -Vector3.UnitZ);
        string? Group(int? part) => part is int index ? scene.Parts[index].Group : null;
        Assert.Equal("chassis", Group(Down(.25f)));
        Assert.Equal("door_dside_f", Group(Down(1.25f)));
        Assert.Equal("door_dside_f", Group(Down(2.25f)));
        Assert.Equal("extra_1", Group(Down(3.25f)));
        // From below too: both sides count. Off the model, nothing.
        Assert.Equal("chassis", Group(visibility.PartAt(new Vector3(.25f, .25f, -5), Vector3.UnitZ)));
        Assert.Null(Down(9));
        Assert.Null(visibility.PartAt(new Vector3(.25f, .25f, 5), Vector3.UnitZ));
        // What isn't drawn isn't picked: a hidden part, the other state's.
        PartNode chassis = visibility.Sections()[0].Nodes[0].Children[0];
        visibility.Show(chassis, false);
        Assert.Null(Down(.25f));
        visibility.ShowAll();
        visibility.Damaged = true;
        Assert.Null(Down(3.25f));
        Assert.Equal(1, scene.Models.Count - 1);
        Assert.Equal(1, scene.Parts[(int)Down(.25f)!].Model);
    }

    [AvaloniaFact]
    public void A_click_on_the_model_selects_its_geometry_in_the_parts_panel_and_outlines_it()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        // The triangle is where the file puts it, in the world.
        Vector3 origin = Vector3.Zero;
        int told = 0;
        viewer.Parts.SelectionChanged += () => told++;

        // On the triangle: its geometry's row, under the drawable's, selected, and the part outlined.
        Assert.True(viewer.PickAt(origin + new Vector3(.2f, .2f, 3), -Vector3.UnitZ));
        PartNode picked = Assert.Single(viewer.Parts.Selection);
        Assert.Equal((PartKind.Geometry, "default"), (picked.Kind, picked.Name));
        Assert.Equal([0], viewer.Viewport.Picked);
        Assert.Equal(1, told);
        // Selecting in the panel outlines too: the drawable's row, all its geometries.
        viewer.Parts.Select(viewer.Parts.Rows[0].Node);
        Assert.Equal([0], viewer.Viewport.Picked);
        Assert.Equal(2, told);
        // Another level: the row stays selected, and the outline moves to that level's parts.
        viewer.Viewport.Pick([]);
        viewer.SetLevel(0);
        Assert.Equal([0], viewer.Viewport.Picked);
        // Off the model: nothing picked, nothing outlined.
        Assert.False(viewer.PickAt(origin + new Vector3(5, 5, 3), -Vector3.UnitZ));
        Assert.Empty(viewer.Parts.Selection);
        Assert.Empty(viewer.Viewport.Picked);
        Assert.Equal(3, told);
    }

    [AvaloniaFact]
    public void An_extra_picked_on_the_model_is_not_a_row_to_select()
    {
        var panel = new Views.PartsPanel { Parts = new PartVisibility(FragmentViewerTests.DamagedScene()) };
        // The extra is a toggle, not a row: nothing to select. The door is.
        Assert.False(panel.Reveal(node => node.Name == "extra_1"));
        Assert.Empty(panel.Selection);
        Assert.True(panel.Reveal(node => node.Name == "door_dside_f"));
        Assert.Equal("door_dside_f", Assert.Single(panel.Selection).Name);
    }

    [Fact]
    public void The_picked_geometry_is_outlined_in_the_accent_on_the_gpu()
    {
        GpuDevice gpu;
        try
        {
            gpu = GpuDevice.Shared;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or WebGpuSharp.WebGPUException)
        {
            Assert.Skip($"No GPU to test on: {error.Message}");
            return;
        }
        const int Size = 128;
        DrawableScene scene = FragmentViewerTests.DamagedScene();
        byte[] plain, outlined;
        (WebGpuSharp.ErrorType Type, string Message) result;
        bool bgra;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            renderer.Show(scene);
            renderer.Frame(new CommonFury.Numerics.Aabb3(new Vector3(0, 0, 0), new Vector3(4, 1, 0)));
            renderer.Antialiasing = false;
            bgra = renderer.PresentFormat is WebGpuSharp.TextureFormat.BGRA8UnormSrgb or WebGpuSharp.TextureFormat.BGRA8Unorm;
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
            byte[] Draw()
            {
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
                byte[] pixels = readback.GetConstMappedRange<byte[], byte>(0, Size * Size * 4, span => span.ToArray());
                readback.Unmap();
                readback.Destroy();
                return pixels;
            }
            plain = Draw();
            renderer.Pick([0]);
            Assert.Equal([0], renderer.Picked);
            outlined = Draw();
            renderer.Pick([]);
            Assert.Equal(plain, Draw());
            target.Destroy();
            renderer.Dispose();
            result = gpu.Device.PopErrorScopeSync(10_000_000_000);
        }
        Assert.True(result.Type == WebGpuSharp.ErrorType.NoError, $"{result.Type}: {result.Message}");

        // Pixels round the picked triangle take the accent, #5E8BD0, give or take the dither; inside it nothing is
        // drawn over it, so every pixel the pick changes is the outline's.
        int accent = 0, changed = 0;
        for (int at = 0; at < plain.Length; at += 4)
        {
            if (plain[at] == outlined[at] && plain[at + 1] == outlined[at + 1] && plain[at + 2] == outlined[at + 2])
                continue;
            changed++;
            (int r, int g, int b) = bgra ? (outlined[at + 2], outlined[at + 1], outlined[at]) : (outlined[at], outlined[at + 1], outlined[at + 2]);
            if (Math.Abs(r - 0x5E) <= 2 && Math.Abs(g - 0x8B) <= 2 && Math.Abs(b - 0xD0) <= 2)
                accent++;
        }
        Assert.True(accent > 20, $"{accent} accent pixels of {changed} changed");
        Assert.Equal(changed, accent);
        Assert.True(changed < Size * Size / 3, $"{changed} pixels changed");
    }
}
