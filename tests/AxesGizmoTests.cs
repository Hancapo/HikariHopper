using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class AxesGizmoTests
{
    [Fact]
    public void The_camera_looks_from_along_an_axis()
    {
        var camera = new OrbitCamera();
        var center = new Vector3(10, 20, 5);
        camera.Frame(center, 2);
        // From the side, level, at the same distance, looking at the centre.
        camera.LookFrom(Vector3.UnitX);
        Assert.Equal((0f, 0f), (camera.Yaw, camera.Pitch));
        Assert.Equal(Vector3.Normalize(camera.Eye - center).X, 1, 4);
        camera.LookFrom(-Vector3.UnitY);
        Assert.Equal(-1, Vector3.Normalize(camera.Eye - center).Y, 4);
        Assert.Equal(camera.Distance, Vector3.Distance(camera.Eye, center), 0.001);
        // From above: straight down, +Y up the screen; from below, straight up, the same way up.
        camera.LookFrom(Vector3.UnitZ);
        Assert.Equal(OrbitCamera.MaxPitch, camera.Pitch);
        Assert.True(camera.Forward.Z < -.9999f);
        Assert.True(camera.Up.Y > .9999f);
        camera.LookFrom(-Vector3.UnitZ);
        Assert.Equal(-OrbitCamera.MaxPitch, camera.Pitch);
        Assert.True(camera.Forward.Z > .9999f);
        // A drag from there tilts no further than the view does.
        camera.Orbit(0, -500);
        Assert.Equal(-OrbitCamera.MaxPitch, camera.Pitch);
    }

    [AvaloniaFact]
    public void A_picked_axis_stops_flying_and_every_ball_is_one_size()
    {
        var viewer = new Views.ModelViewer();
        viewer.SetFreeCamera(true);
        Assert.True(viewer.Viewport.FreeCamera);
        viewer.Gizmo.Pick(1);
        Assert.False(viewer.Viewport.FreeCamera);
        // Every ball, front or back, is under the pointer as far out from its centre.
        var gizmo = new Views.AxesGizmo { Basis = From(Vector3.UnitX) };
        Avalonia.Point back = gizmo.Place(-Vector3.UnitY).Centre, front = gizmo.Place(Vector3.UnitY).Centre;
        Assert.Equal(4, gizmo.AxisAt(new Avalonia.Point(back.X + 8, back.Y)));
        Assert.Equal(1, gizmo.AxisAt(new Avalonia.Point(front.X + 8, front.Y)));
    }

    // The camera's basis looking from a side.
    private static (Vector3, Vector3, Vector3) From(Vector3 side)
    {
        var camera = new OrbitCamera();
        camera.Frame(Vector3.Zero, 1);
        camera.LookFrom(side);
        return (camera.Right, camera.Up, camera.Forward);
    }

    [AvaloniaFact]
    public void A_click_on_an_axis_looks_from_it_and_a_drag_orbits()
    {
        var gizmo = new Views.AxesGizmo
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Basis = From(Vector3.UnitX),
        };
        var window = new Window { Width = 200, Height = 200, Content = gizmo };
        window.Show();
        Assert.Equal(new Avalonia.Size(72, 72), gizmo.Bounds.Size);
        // From +X: Y points right, Z up, X straight at the eye, in the middle.
        Assert.Equal(new Avalonia.Point(60, 36), gizmo.Place(Vector3.UnitY).Centre);
        Assert.Equal(new Avalonia.Point(36, 12), gizmo.Place(Vector3.UnitZ).Centre);
        Assert.True(gizmo.Place(Vector3.UnitX).Depth < gizmo.Place(-Vector3.UnitX).Depth);
        // In the middle, the nearer of the two X balls is under the pointer.
        Assert.Equal(0, gizmo.AxisAt(new Avalonia.Point(36, 36)));
        Assert.Equal(-1, gizmo.AxisAt(new Avalonia.Point(5, 5)));

        var picked = new List<Vector3>();
        gizmo.AxisPicked += picked.Add;
        var drags = new List<(float, float)>();
        gizmo.Dragged += (dx, dy) => drags.Add((dx, dy));
        Avalonia.Point On(Vector3 axis) => gizmo.TranslatePoint(gizmo.Place(axis).Centre, window)!.Value;
        // Z clicked: look from above. X, faced already: from the other side.
        window.MouseDown(On(Vector3.UnitZ), MouseButton.Left);
        window.MouseUp(On(Vector3.UnitZ), MouseButton.Left);
        window.MouseDown(On(Vector3.UnitX), MouseButton.Left);
        window.MouseUp(On(Vector3.UnitX), MouseButton.Left);
        Assert.Equal([Vector3.UnitZ, -Vector3.UnitX], picked);
        Assert.Empty(drags);

        // A drag orbits by how far the pointer goes, and picks nothing.
        Avalonia.Point start = On(Vector3.UnitY);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Avalonia.Point(start.X - 10, start.Y));
        window.MouseMove(new Avalonia.Point(start.X - 15, start.Y + 4));
        window.MouseUp(new Avalonia.Point(start.X - 15, start.Y + 4), MouseButton.Left);
        Assert.Equal([(-10f, 0f), (-5f, 4f)], drags);
        Assert.Equal(2, picked.Count);
        window.Close();
    }
}
