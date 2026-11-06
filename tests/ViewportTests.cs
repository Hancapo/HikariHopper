using System.Numerics;
using Avalonia.Headless;
using CommonFury.Numerics;
using FiveFury.Bounds;
using FiveFury.Ybn;
using FiveFury.Ynv;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class ViewportTests
{
    [Fact]
    public void ACollisionIsDrawnWhereTheWorldPutsIt()
    {
        // Collisions sit at their map coordinates, not around the origin.
        var box = new Aabb3(new Vector3(246, 400, 117), new Vector3(467, 602, 185));
        var bound = new BoundBox(box);
        bound.Material.Type = (BoundMaterialType)4;
        byte[] ybn = new Ybn(bound).Write();

        ModelContent content = ModelContent.Read("ch2_04_15.ybn", ybn);

        Assert.Equal(box.Minimum, content.Box.Minimum);
        Assert.Equal(box.Maximum, content.Box.Maximum);
        // Six faces of two triangles, drawn relative to the box's middle so far-out content stays precise.
        Assert.Equal(12, content.Mesh.TriangleCount);
        Assert.Equal(box.Center, content.Mesh.Origin);
        Assert.All(content.Mesh.Vertices, vertex =>
        {
            Vector3 world = vertex.Position + content.Mesh.Origin;
            Assert.InRange(world.X, 246 - 0.01f, 467 + 0.01f);
            Assert.InRange(world.Z, 117 - 0.01f, 185 + 0.01f);
            // In the material's display colour: tarmac's.
            Assert.Equal(ViewportMesh.Pack(129, 118, 154), vertex.Colour);
        });
        Assert.Empty(content.Mesh.Skipped);
    }

    [Fact]
    public void ACompositesChildrenAreDrawnWhereTheirTransformsPutThem()
    {
        var geometry = new BoundGeometry();
        geometry.Vertices.AddRange([new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)]);
        geometry.Materials.Add(new BoundMaterial { Type = (BoundMaterialType)1 });
        geometry.Polygons.Add(new BoundTriangle(0, 1, 2));
        var composite = new BoundComposite();
        composite.Children.Add(new BoundChild(geometry) { Transform = new BoundTransform(Matrix4x4.CreateTranslation(1000, 2000, 30)) });

        ViewportMesh mesh = CollisionMesh.Build(new Ybn(composite));

        Assert.Equal(1, mesh.TriangleCount);
        Vector3[] corners = [.. mesh.Vertices.Select(vertex => vertex.Position + mesh.Origin)];
        Assert.Equal([new(1000, 2000, 30), new(1002, 2000, 30), new(1000, 2002, 30)], corners);
        // Flat, facing up, in concrete's colour.
        Assert.All(mesh.Vertices, vertex => Assert.Equal(Vector3.UnitZ, vertex.Normal));
        Assert.Equal(CollisionMesh.Colour((BoundMaterialType)1), mesh.Vertices[0].Colour);
    }

    [Fact]
    public void ANavmeshIsDrawnWhereItsVerticesAre()
    {
        Vector3[] vertices = [new(1010, 2020, 30), new(1040, 2020, 31), new(1040, 2050, 32), new(1010, 2050, 30)];
        Ynv navmesh = YnvAuthoring.FromMesh(vertices, [0, 1, 2, 0, 2, 3]).Single().Mesh;

        ModelContent content = ModelContent.Read("navmesh.ynv", navmesh.Write());

        // Navmesh vertices are quantized within their cell: close, not exact.
        Assert.Equal(1010, content.Box.Minimum.X, 0.1);
        Assert.Equal(2020, content.Box.Minimum.Y, 0.1);
        Assert.Equal(1040, content.Box.Maximum.X, 0.1);
        Assert.Equal(2050, content.Box.Maximum.Y, 0.1);
        Assert.Equal(32, content.Box.Maximum.Z, 0.1);
        // Its polygons, fanned into triangles, cover the square.
        Assert.True(content.Mesh.TriangleCount >= 2);
        float area = 0;
        for (int i = 0; i < content.Mesh.Vertices.Count; i += 3)
        {
            Vector3 a = content.Mesh.Vertices[i].Position, b = content.Mesh.Vertices[i + 1].Position, c = content.Mesh.Vertices[i + 2].Position;
            area += Vector3.Cross(b - a, c - a).Length() / 2;
        }
        Assert.Equal(900, area, 900 * 0.02);
    }

    [Fact]
    public void AFileThatIsntACollisionOrNavmeshSaysSo()
    {
        Assert.ThrowsAny<InvalidDataException>(() => ModelContent.Read("broken.ybn", [1, 2, 3, 4]));
        Assert.Throws<InvalidDataException>(() => ModelContent.Read("model.ydr", [1, 2, 3, 4]));
        Assert.True(ExplorerViewModel.IsModelName("CH2_04_15.YBN"));
        Assert.True(ExplorerViewModel.IsModelName("navmesh.ynv"));
        Assert.False(ExplorerViewModel.IsModelName("model.ydr"));
    }

    [Fact]
    public void TheCameraOrbitsAndZoomsAroundWhatItFrames()
    {
        var camera = new OrbitCamera();
        var center = new Vector3(356.4f, 501, 151.4f);
        camera.Frame(center, 150);
        Assert.Equal(150 * 2.8f, camera.Distance, 0.01);
        Assert.Equal(camera.Distance, Vector3.Distance(camera.Eye, center), 0.01);

        // What is framed is in the middle of the screen, in front of the camera (reversed Z: depth above zero).
        Vector4 clip = Vector4.Transform(new Vector4(center, 1), camera.ViewProjection(16f / 9));
        Assert.Equal(0, clip.X / clip.W, 0.0001);
        Assert.Equal(0, clip.Y / clip.W, 0.0001);
        Assert.InRange(clip.Z / clip.W, 0f, 1f);

        // Orbiting keeps the distance; it stops short of the poles; zooming stays within the size's limits.
        camera.Orbit(400, 10000);
        Assert.Equal(1.5f, camera.Pitch);
        Assert.Equal(camera.Distance, Vector3.Distance(camera.Eye, center), 0.01);
        camera.Zoom(1000);
        Assert.Equal(150 * .02f, camera.Distance, 0.001);
        camera.Zoom(-1000);
        Assert.Equal(150 * 200f, camera.Distance, 0.1);
    }

    [Fact]
    public void TheGridIsAPowerOfTenAboutATenthOfWhatIsFramed()
    {
        Assert.Equal(1, ViewportRenderer.GridSpacing(5), 0.0001);
        Assert.Equal(10, ViewportRenderer.GridSpacing(155), 0.0001);
        Assert.Equal(100, ViewportRenderer.GridSpacing(2000), 0.0001);
        Assert.Equal(0.1, ViewportRenderer.GridSpacing(0.01f), 0.0001);
    }

    [Fact]
    public void TheBackdropIsTheThemesNightInLinearLight()
    {
        Vector4[] colours = ViewportPalette.Linear();
        Assert.Equal(7, colours.Length);
        // #121322 at the zenith, the chrome's #2C2D43 at the horizon, #1B1C2C for the void.
        Assert.Equal(ViewportPalette.ToLinear(Avalonia.Media.Color.Parse("#121322")), colours[0]);
        Assert.Equal(ViewportPalette.ToLinear(Avalonia.Media.Color.Parse("#2C2D43")), colours[1]);
        Assert.Equal(ViewportPalette.ToLinear(Avalonia.Media.Color.Parse("#1B1C2C")), colours[2]);
        // sRGB's own curve: mid grey is about a fifth of white's light.
        Assert.Equal(0.2158f, ViewportPalette.ToLinear(Avalonia.Media.Color.Parse("#808080")).X, 0.0001);
        Assert.Equal(Vector4.One, ViewportPalette.ToLinear(Avalonia.Media.Colors.White));
    }
}

/// <summary>Opening a collision from the explorer.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ModelViewerTests : ExplorerTestBase
{
    [AvaloniaFact]
    public void OpeningACollisionInTheExplorerSaysWhereItIs()
    {
        MakeGame();
        var box = new Aabb3(new Vector3(246, 400, 117), new Vector3(467, 602, 185.5f));
        File.WriteAllBytes(Path.Combine(Work, "ch2_04_15.ybn"), new Ybn(new BoundBox(box)).Write());
        var session = new ExplorerSession();
        var window = new Views.MainWindow { Width = 1360, Height = 840 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        ExplorerStateRenders.Pump(200);
        int row = Enumerable.Range(0, bridge.EntriesModel.Count).Single(index => bridge.EntriesModel[index].Name == "ch2_04_15.ybn");
        bridge.ActivateEntry(row);

        Views.ModelViewer viewer = Assert.IsType<Views.ModelViewer>(session.ModelViewer);
        ExplorerStateRenders.Pump(400);
        Assert.True(viewer.IsOpen);
        Assert.Equal("Opened ch2_04_15.ybn", bridge.Status);
        Assert.Equal("ch2_04_15.ybn", viewer.TitleText.Text);
        Assert.Equal("12 triangles · 221 × 202 × 68.5 m at 356.5, 501, 151.3", viewer.Location);
        // The viewport, a native window, shows once the sheet has settled.
        Assert.True(viewer.Viewport.IsVisible);

        // Escape closes it, the viewport first, and the session lets it go.
        window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        window.KeyRelease(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        ExplorerStateRenders.Pump(300);
        Assert.False(viewer.IsOpen);
        Assert.False(viewer.Viewport.IsVisible);
        Assert.Null(session.ModelViewer);
        window.Close();
    }

    [AvaloniaFact]
    public void AFileThatCantBeReadSaysWhy()
    {
        var viewer = new Views.ModelViewer(new ModelFile("broken.ybn", [1, 2, 3, 4]));
        Assert.StartsWith("Couldn't read broken.ybn: ", viewer.Location);
    }
}
