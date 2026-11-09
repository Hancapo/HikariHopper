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
        // The legend names the material, with how many shapes wear it.
        Assert.Equal([new LegendEntry("Tarmac", ViewportMesh.Pack(129, 118, 154), 1)], content.Mesh.Legend);
    }

    [Fact]
    public void TheLegendCountsOnlyShapesThatAreDrawn()
    {
        // A disc isn't drawn: its material isn't in the legend, whose counts stay those of what shows.
        var composite = new BoundComposite();
        var box = new BoundBox(new Aabb3(Vector3.Zero, Vector3.One));
        box.Material.Type = (BoundMaterialType)4;
        var disc = new BoundDisc(new Vector3(3, 0, 0), 1);
        disc.Material.Type = (BoundMaterialType)1;
        composite.Children.Add(new BoundChild(box));
        composite.Children.Add(new BoundChild(disc));

        ViewportMesh mesh = CollisionMesh.Build(new Ybn(composite));

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(1, mesh.Skipped["Disc"]);
        Assert.Equal([new LegendEntry("Tarmac", ViewportMesh.Pack(129, 118, 154), 1)], mesh.Legend);
    }

    [Fact]
    public void TrianglesThatArentFiniteAreLeftOut()
    {
        var mesh = new ViewportMesh(Vector3.Zero);
        Vector3 nan = new(float.NaN, 0, 0);
        mesh.Triangle(nan, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, 0);
        mesh.Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, nan, Vector3.UnitZ, Vector3.UnitZ, 0);
        mesh.Triangle(nan, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.Equal(0, mesh.TriangleCount);
        mesh.Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, 0);
        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void OpenIsOfferedForCollisionsAndNavmeshes()
    {
        Assert.True(ExplorerViewModel.Opens(new HikariHopper.Core.EntryRecord("ch2_04_15.ybn", "ch2_04_15.ybn", "ybn", 1)));
        Assert.True(ExplorerViewModel.Opens(new HikariHopper.Core.EntryRecord("navmesh.ynv", "navmesh.ynv", "ynv", 1)));
        Assert.False(ExplorerViewModel.Opens(new HikariHopper.Core.EntryRecord("model.ymap", "model.ymap", "ymap", 1)));
    }

    [Fact]
    public void ANavmeshIsColouredByItsGroundAndShowsWherePedsClimbAndDrop()
    {
        Vector3[] vertices = [new(1010, 2020, 30), new(1040, 2020, 30), new(1040, 2050, 30), new(1010, 2050, 30)];
        Ynv navmesh = YnvAuthoring.FromMesh(vertices, [0, 1, 2, 0, 2, 3]).Single().Mesh;
        YnvPoly polygon = navmesh.Polygons[0];
        polygon.Flags0 |= YnvPolyFlags0.IsWater;
        navmesh.Edges[polygon.IndexId].Current.AdjacencyType = YnvAdjacencyType.ClimbLow;

        ViewportMesh mesh = NavmeshMesh.Build(navmesh, new Vector3(1025, 2035, 30));

        string[] names = [.. mesh.Legend.Select(entry => entry.Name)];
        Assert.Contains("Water", names);
        Assert.Contains("Low climb", names);
        // Polygons are drawn whole: the triangles they were split into show no edges of their own.
        Assert.All(mesh.Vertices, vertex => Assert.Equal(0u, vertex.Colour >> 24));
        // Every polygon edge has its outline; the climb also gets a band in its colour, lying on the polygon.
        Assert.Equal(navmesh.Polygons.Sum(p => p.IndexCount), mesh.Segments.Count);
        uint climb = mesh.Legend.Single(entry => entry.Name == "Low climb").Colour & 0xFFFFFF;
        ViewportVertex[] band = [.. mesh.Vertices.Where(vertex => (vertex.Colour & 0xFFFFFF) == climb)];
        Assert.Equal(6, band.Length);
        Assert.All(band, vertex => Assert.Equal(30.02f, vertex.Position.Z + mesh.Origin.Z, 0.001));
    }

    [Fact]
    public void ALadderUpAndDownIsOneSolidLinkPointingBothWays()
    {
        Vector3[] vertices = [new(1010, 2020, 30), new(1040, 2020, 30), new(1040, 2050, 30), new(1010, 2050, 30)];
        Ynv navmesh = YnvAuthoring.FromMesh(vertices, [0, 1, 2, 0, 2, 3]).Single().Mesh;
        Vector3 foot = new(1012, 2022, 30), top = new(1012, 2022.2f, 36);
        navmesh.Portals.Add(new YnvPortal { Type = YnvPortalType.ClimbLadder, PositionFrom = foot, PositionTo = top });
        navmesh.Portals.Add(new YnvPortal { Type = YnvPortalType.DescendLadder, PositionFrom = top, PositionTo = foot });
        ViewportMesh withoutLadder = NavmeshMesh.Build(YnvAuthoring.FromMesh(vertices, [0, 1, 2, 0, 2, 3]).Single().Mesh, foot);

        ViewportMesh mesh = NavmeshMesh.Build(navmesh, foot);

        // One entry for the pair, solid shapes (not lines) in its colour, and dark pucks at its ends.
        LegendEntry ladder = Assert.Single(mesh.Legend, entry => entry.Name == "Ladder");
        Assert.Equal(1, ladder.Count);
        Assert.Equal(withoutLadder.Segments.Count, mesh.Segments.Count);
        ViewportVertex[] shapes = [.. mesh.Vertices.Skip(withoutLadder.Vertices.Count)];
        Assert.Contains(shapes, vertex => (vertex.Colour & 0xFFFFFF) == (ladder.Colour & 0xFFFFFF));
        Assert.Contains(shapes, vertex => (vertex.Colour & 0xFFFFFF) == (ViewportMesh.Pack(Ui.Theme.ChromeBg) & 0xFFFFFF));
        // Straight up, as a ladder goes, and pointing both ways: its colour reaches near both ends but no further.
        Vector3[] link = [.. shapes.Where(vertex => (vertex.Colour & 0xFFFFFF) == (ladder.Colour & 0xFFFFFF)).Select(vertex => vertex.Position + mesh.Origin)];
        Assert.All(link, point => Assert.InRange(point.X, 1011.7f, 1012.3f));
        Assert.InRange(link.Min(point => point.Z), 30, 30.3f);
        Assert.InRange(link.Max(point => point.Z), 35.7f, 36);
    }

    [Fact]
    public void ACoverPointIsAPanelInFrontOfWhereThePedStands()
    {
        var foot = new Vector3(1000, 2000, 30);
        var mesh = new ViewportMesh(foot);

        // Facing north (+Y): low cover, half a ped high.
        NavmeshMesh.AddCoverPoint(mesh, foot, 0, YnvPointType.LowWall, disabled: false);

        LegendEntry entry = Assert.Single(mesh.Legend);
        Assert.Equal("Cover point, low", entry.Name);
        Vector3[] panel = [.. mesh.Vertices.Where(v => (v.Colour & 0xFFFFFF) == (entry.Colour & 0xFFFFFF)).Select(v => v.Position + mesh.Origin)];
        Vector3[] puck = [.. mesh.Vertices.Where(v => (v.Colour & 0xFFFFFF) == (ViewportMesh.Pack(Ui.Theme.ChromeBg) & 0xFFFFFF)).Select(v => v.Position + mesh.Origin)];
        // The puck is where the ped stands; the panel stands in front of it, the way it faces, as wide as a ped.
        Assert.All(puck, point => Assert.True(new Vector2(point.X - foot.X, point.Y - foot.Y).Length() < 0.2f));
        Assert.All(panel, point => Assert.InRange(point.Y, 2000.25f, 2000.4f));
        Assert.Equal(0.5f, panel.Max(point => point.X) - panel.Min(point => point.X), 0.001);
        Assert.Equal(0.55f, panel.Max(point => point.Z) - panel.Min(point => point.Z), 0.001);

        // A wall's panel is as high as a ped; one turned a quarter to the west is on the ped's west side.
        var wall = new ViewportMesh(foot);
        NavmeshMesh.AddCoverPoint(wall, foot, MathF.PI / 2, YnvPointType.WallToNeither, disabled: false);
        LegendEntry wallEntry = Assert.Single(wall.Legend);
        Assert.Equal("Cover point, wall", wallEntry.Name);
        Vector3[] wallPanel = [.. wall.Vertices.Where(v => (v.Colour & 0xFFFFFF) == (wallEntry.Colour & 0xFFFFFF)).Select(v => v.Position + wall.Origin)];
        Assert.Equal(1.1f, wallPanel.Max(point => point.Z) - wallPanel.Min(point => point.Z), 0.001);
        Assert.All(wallPanel, point => Assert.InRange(point.X, 999.6f, 999.75f));
    }

    [Fact]
    public void AnArrowStopsJustClearOfThePuckItPointsAt()
    {
        // Landing from straight above: out through the top. From below: through the bottom. Coming in flat: the rim.
        Assert.Equal(.05f + .04f, NavmeshMesh.Clearance(-Vector3.UnitZ, .2f), 0.0001);
        Assert.Equal(.03f + .04f, NavmeshMesh.Clearance(Vector3.UnitZ, .2f), 0.0001);
        Assert.Equal(.2f + .04f, NavmeshMesh.Clearance(Vector3.UnitX, .2f), 0.0001);
        // At a slant, whichever way out comes first.
        Vector3 slant = Vector3.Normalize(new Vector3(1, 0, -1));
        Assert.Equal(Math.Min(.09f / slant.X, .24f / slant.X), NavmeshMesh.Clearance(slant, .2f), 0.0001);
    }

    [Fact]
    public void ANavmeshPolygonTakesItsMostTellingGround()
    {
        Assert.Equal("Walkable", NavmeshMesh.Ground(new YnvPoly()));
        Assert.Equal("Road", NavmeshMesh.Ground(new YnvPoly { Flags1 = YnvPolyFlags1.IsRoad }));
        Assert.Equal("Pavement", NavmeshMesh.Ground(new YnvPoly { Flags0 = YnvPolyFlags0.IsPavement | YnvPolyFlags0.InShelter }));
        Assert.Equal("Too steep", NavmeshMesh.Ground(new YnvPoly { Flags0 = YnvPolyFlags0.TooSteepToWalkOn | YnvPolyFlags0.IsWater }));
        Assert.Equal("Shallow water", NavmeshMesh.Ground(new YnvPoly { Flags1 = YnvPolyFlags1.IsShallowWater | YnvPolyFlags1.IsRoad }));
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
        Assert.True(ExplorerViewModel.IsModelName("model.ydr"));
        Assert.False(ExplorerViewModel.IsModelName("model.ytd"));
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
        Settle(viewer);
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
        // It's read in the background, and says so meanwhile.
        Assert.Equal("Reading broken.ybn…", viewer.StatusText.Text);
        Settle(viewer);
        Assert.StartsWith("Couldn't read broken.ybn: ", viewer.Location);
    }

    [AvaloniaFact]
    public void A_drawable_is_lit_at_the_time_of_day_its_slider_sets()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();

        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        Settle(viewer);
        // The game's afternoon to start with, in quarter hours.
        Assert.True(viewer.SunBar.IsVisible);
        Assert.Equal("14:30", viewer.SunText.Text);
        Assert.Equal(58, viewer.SunSlider.Value);
        Assert.Equal(14.5f, viewer.Hours);

        viewer.SetHours(18.25f);
        Assert.Equal(18.25f, viewer.Hours);
        Assert.Equal("18:15", viewer.SunText.Text);
        Assert.Equal(73, viewer.SunSlider.Value);
        // Midnight at either end.
        viewer.SetHours(24);
        Assert.Equal("00:00", viewer.SunText.Text);
    }

    [AvaloniaFact]
    public void A_collision_has_no_time_of_day()
    {
        var box = new Aabb3(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var viewer = new Views.ModelViewer(new ModelFile("box.ybn", new Ybn(new BoundBox(box)).Write()));
        Settle(viewer);
        Assert.False(viewer.SunBar.IsVisible);
    }

    /// <summary>Waits for a viewer's background read to finish and be shown.</summary>
    private static void Settle(Views.ModelViewer viewer)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        Assert.True(viewer.Reading.IsCompletedSuccessfully);
    }
}
