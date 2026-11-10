using System.Numerics;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
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
        // The parts panel lists them under what they are: the ground, and the edges drawn on it.
        MeshMark water = mesh.Marks.Single(mark => mark.Name == "Water"), lowClimb = mesh.Marks.Single(mark => mark.Name == "Low climb");
        Assert.Equal((NavmeshMesh.GroundSection, NavmeshMesh.EdgeSection), (water.Section, lowClimb.Section));
        // Hiding the climb takes its band away and leaves the polygons and their outlines; hiding the water takes those.
        int all = mesh.ToArray().Length, lines = mesh.SegmentArray().Length;
        mesh.Show(lowClimb.Index, false);
        Assert.Equal(all - 6, mesh.ToArray().Length);
        Assert.Equal(lines, mesh.SegmentArray().Length);
        mesh.Show(water.Index, false);
        Assert.True(mesh.ToArray().Length < all - 6);
        Assert.True(mesh.SegmentArray().Length < lines);
        mesh.ShowAll();
        Assert.Equal(all, mesh.ToArray().Length);
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
        // However big what is framed, a surface a centimetre in front of the camera isn't cut off by the near plane.
        Vector4 close = Vector4.Transform(new Vector4(camera.Eye + camera.Forward * .01f, 1), camera.ViewProjection(16f / 9));
        Assert.InRange(close.Z / close.W, 0f, 1f);

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
    public void The_free_camera_looks_about_its_eye_and_flies()
    {
        var camera = new OrbitCamera();
        camera.Frame(new Vector3(10, 20, 5), 2);
        Assert.Equal(3, camera.Speed, 0.001);

        // Looking turns about the eye: it stays put, the view turns, and the centre stays the distance ahead.
        Vector3 eye = camera.Eye, forward = camera.Forward;
        camera.Look(120, -40);
        Assert.Equal(0, Vector3.Distance(eye, camera.Eye), 0.001);
        Assert.NotEqual(forward, camera.Forward);
        Assert.Equal(camera.Distance, Vector3.Distance(camera.Eye, camera.Center), 0.001);
        // Dragging up looks up, and dragging right looks right, as orbiting turns the same way.
        float before = camera.Forward.Z;
        camera.Look(0, -50);
        Assert.True(camera.Forward.Z > before);
        Vector3 right = camera.Right;
        forward = camera.Forward;
        camera.Look(30, 0);
        Assert.True(Vector3.Dot(camera.Forward - forward, right) > 0);

        // Flying a second forward moves the eye its speed along the view; diagonals are no faster; Shift's boost is.
        eye = camera.Eye;
        forward = camera.Forward;
        camera.Fly(1, 0, 0, 1);
        Assert.Equal(0, Vector3.Distance(eye + forward * 3, camera.Eye), 0.001);
        eye = camera.Eye;
        camera.Fly(1, 1, 1, 1);
        Assert.Equal(3, Vector3.Distance(eye, camera.Eye), 0.001);
        eye = camera.Eye;
        camera.Fly(0, 0, -1, .5f, 4);
        Assert.Equal(0, Vector3.Distance(eye - Vector3.UnitZ * 6, camera.Eye), 0.001);

        // The wheel sets the speed, within limits from what is framed.
        camera.Accelerate(1);
        Assert.Equal(3 * MathF.Exp(.2f), camera.Speed, 0.001);
        camera.Accelerate(-1000);
        Assert.Equal(2 * .02f, camera.Speed, 0.0001);
        camera.Accelerate(1000);
        Assert.Equal(2 * 200f, camera.Speed, 0.01);
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

        int opened = 0;
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr) { OpenTextures = _ => opened++ });
        Settle(viewer);
        // No embedded textures: the parts panel has no row for them, and T does nothing.
        Assert.False(viewer.Parts.TexturesRow.IsVisible);
        Assert.False(viewer.Shortcut('T'));
        // Of the model, the toolbar has its shadows, and its levels of detail when it has more than one: its state
        // and textures are in the parts panel.
        Assert.Equal([viewer.ShadowsButton], viewer.ModelTools.GetLogicalDescendants().OfType<Views.Controls.ChromeToolButton>());
        Assert.False(viewer.LodSwitch.IsVisible);
        Assert.Equal(0, opened);
        // The game's afternoon to start with, in quarter hours, on the time button and in its dropdown.
        Assert.Equal("14:30", viewer.TimeButton.Text);
        Assert.Equal("14:30", viewer.SunText.Text);
        Assert.False(viewer.TimeOpen);
        Assert.Equal(58, viewer.SunSlider.Value);
        Assert.Equal(14.5f, viewer.Hours);

        // The toolbar and the stats: one triangle, three corners, three edges.
        Assert.True(viewer.Toolbar.IsVisible);
        // What is drawn over the model, in the tool strip over the viewport, as in Blender, not in the toolbar: its
        // skeleton, collision and lights.
        Assert.True(viewer.ToolStripOverlay.IsWanted);
        Assert.True(viewer.PartsTabOverlay.IsWanted);
        Views.Controls.ChromeToolButton[] strip = [.. viewer.ToolStrip.GetLogicalDescendants().OfType<Views.Controls.ChromeToolButton>()];
        Assert.Equal([viewer.SkeletonButton, viewer.SolidButton, viewer.LightsButton], strip);
        Assert.DoesNotContain(viewer.SkeletonButton, viewer.GetLogicalDescendants());
        Assert.Equal(("1", "3", "3"), (viewer.TrianglesText.Text, viewer.VerticesText.Text, viewer.EdgesText.Text));
        // No material count or size, and nothing missing to say.
        Assert.Equal("", viewer.Location);
        // Shadows are on, and H turns them off and on; the button sits raised while they're on.
        Assert.True(viewer.Viewport.Shadows);
        Assert.True(viewer.ShadowsButton.Sunken);
        Assert.True(viewer.Shortcut('h'));
        Assert.False(viewer.Viewport.Shadows);
        Assert.False(viewer.ShadowsButton.Sunken);
        Assert.True(viewer.Shortcut('H'));
        Assert.True(viewer.Viewport.Shadows);
        // A key the toolbar doesn't use is left alone.
        Assert.False(viewer.Shortcut('W'));
        // No skeleton, collision or lights: their buttons are off, and their keys do nothing.
        Assert.False(viewer.SkeletonButton.IsEnabled);
        Assert.False(viewer.SolidButton.IsEnabled);
        Assert.False(viewer.LightsButton.IsEnabled);
        Assert.False(viewer.Shortcut('B'));
        Assert.False(viewer.Shortcut('K'));
        Assert.False(viewer.Shortcut('L'));
        Assert.False(viewer.SolidButton.IsEnabled);
        Assert.False(viewer.Shortcut('K'));
        // Not a fragment with a damaged state: the parts panel has no State section, and X does nothing.
        Assert.DoesNotContain(viewer.Parts.Rows, row => row.Node.Kind == PartKind.State);
        Assert.False(viewer.Shortcut('X'));
        // Off, the toggles keep the button's own faint ink.
        Assert.False(viewer.SkeletonButton.IsSet(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty));
        // The camera orbits to start with; V flies it, with its speed in the status bar, and F frames the model and
        // orbits again.
        Assert.False(viewer.Viewport.FreeCamera);
        Assert.True(viewer.OrbitButton.Sunken);
        Assert.False(viewer.SpeedStat.IsVisible);
        Assert.True(viewer.Shortcut('V'));
        Assert.True(viewer.Viewport.FreeCamera);
        Assert.True(viewer.FreeButton.Sunken);
        Assert.False(viewer.OrbitButton.Sunken);
        Assert.True(viewer.SpeedStat.IsVisible);
        Assert.True(viewer.Shortcut('F'));
        Assert.False(viewer.Viewport.FreeCamera);
        Assert.False(viewer.SpeedStat.IsVisible);
        Assert.True(viewer.Shortcut('v'));
        Assert.True(viewer.Shortcut('V'));
        Assert.False(viewer.Viewport.FreeCamera);
        // The parts panel shows the drawable, and P hides and shows it; the drawable has only the high level, so 2
        // changes nothing.
        Assert.True(viewer.PartsShown);
        Assert.True(viewer.PartsHandle.IsVisible);
        Assert.Equal("1 / 1", viewer.Parts.CountText);
        // The drawable, open, and its one geometry under it.
        Assert.Equal(["tri", "default"], viewer.Parts.Rows.Select(row => row.Node.Name));
        Assert.True(viewer.Shortcut('P'));
        Assert.False(viewer.PartsShown);
        // Closed, its edge goes, and the arrow over the viewport points the way it would open.
        Assert.False(viewer.PartsHandle.IsVisible);
        Assert.Equal("forward", viewer.PartsArrow.IconKind);
        viewer.PartsArrow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(viewer.PartsShown);
        Assert.Equal("back", viewer.PartsArrow.IconKind);
        // Its edge resizes it, within limits.
        double width = Views.ModelViewer.PartsWidth;
        viewer.ResizeParts(300);
        Assert.Equal(300, Views.ModelViewer.PartsWidth);
        Assert.Equal(300, viewer.Parts.Width);
        viewer.ResizeParts(5000);
        Assert.Equal(520, Views.ModelViewer.PartsWidth);
        viewer.ResizeParts(width);
        // Only the high level: no level segments, and 2 changes nothing.
        Assert.False(viewer.LodSwitch.IsVisible);
        viewer.Shortcut('2');
        Assert.Equal(0, viewer.Visibility!.Lod);
        // Hiding the drawable leaves nothing counted.
        viewer.Parts.Flip(viewer.Parts.Rows[0].Node);
        Assert.Equal(("0", "0", "0"), (viewer.TrianglesText.Text, viewer.VerticesText.Text, viewer.EdgesText.Text));
        Assert.Equal("0 / 1", viewer.Parts.CountText);
        viewer.Parts.Flip(viewer.Parts.Rows[0].Node);

        viewer.SetHours(18.25f);
        Assert.Equal(18.25f, viewer.Hours);
        Assert.Equal("18:15", viewer.SunText.Text);
        Assert.Equal("18:15", viewer.TimeButton.Text);
        Assert.Equal(73, viewer.SunSlider.Value);
        // Midnight at either end.
        viewer.SetHours(24);
        Assert.Equal("00:00", viewer.SunText.Text);
    }

    [AvaloniaFact]
    public void The_textures_button_opens_a_drawables_embedded_textures()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        // A textured material needs a UV set.
        mesh.SetChannel(CommonFury.Vertices.VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(0, 1, 0, 0)]);
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
        var material = new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference(
            new FiveFury.Textures.Texture("tri_d", 4, 4, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[8]));
        builder.Materials.Add(material);
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();

        int opened = 0;
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr) { OpenTextures = _ => opened++ });
        Settle(viewer);
        // The parts panel's foot has their row, with how many; T, or a press on it, opens them.
        Assert.True(viewer.Parts.TexturesRow.IsVisible);
        Assert.Equal("1", viewer.Parts.TexturesCountText);
        Assert.Contains(viewer.Parts.TexturesRow.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>(), text => text.Text == "Embedded textures");
        Assert.True(viewer.Shortcut('T'));
        Assert.True(viewer.OpenTextures());
        viewer.Parts.RequestTextures();
        Assert.Equal(3, opened);

        // A file that can't open its textures has no row.
        var lone = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        Settle(lone);
        Assert.False(lone.Parts.TexturesRow.IsVisible);
        lone.Parts.RequestTextures();
        Assert.False(lone.OpenTextures());
    }

    [AvaloniaFact]
    public void Textures_saved_in_the_editor_show_on_the_model_once_it_closes()
    {
        byte[] Triangle(int size)
        {
            var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
            mesh.SetChannel(CommonFury.Vertices.VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(0, 1, 0, 0)]);
            var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
            var material = new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" };
            material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference(
                new FiveFury.Textures.Texture("tri_d", size, size, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[size * size / 2]));
            builder.Materials.Add(material);
            var model = new FiveFury.Drawables.DrawableModelInput();
            model.Meshes.Add(mesh);
            builder.Models.Add(model);
            return new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        }
        void Wait(Task task)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && clock.ElapsedMilliseconds < 10_000)
                ExplorerStateRenders.Pump(10);
            Assert.True(task.IsCompletedSuccessfully);
        }

        // A .ydr: read again from the file as saved, its texture now 8 across.
        Action<SavedDocument>? tell = null;
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", Triangle(4)) { OpenTextures = saved => tell = saved });
        Settle(viewer);
        viewer.Viewport.SetTime(20);
        Assert.Equal(4, viewer.Surfaces!.Materials.Single().Diffuse!.Width);
        Assert.True(viewer.OpenTextures());
        byte[] saved = Triangle(8);
        tell!(new SavedDocument([.. Rendering.ModelContent.EmbeddedTextures("tri.ydr", saved).Textures], saved));
        Wait(viewer.TexturesSaved);
        Assert.Equal(8, viewer.Surfaces!.Materials.Single().Diffuse!.Width);
        // As it was: the time of day stays.
        Assert.Equal(20, viewer.Viewport.Hours);

        // A drawable dictionary or fragment can't take them back: they're shown over its own.
        Action<SavedDocument>? tellOther = null;
        var dictionary = new FiveFury.Ydd.Ydd(FiveFury.GameTarget.Legacy);
        dictionary.Drawables.Add(new FiveFury.Ydd.YddEntry(new CommonFury.Hashing.Hash32("tri"), FiveFury.Ydr.Ydr.Read(Triangle(4)).Drawable));
        byte[] ydd = dictionary.Write();
        var other = new Views.ModelViewer(new ModelFile("tri.ydd", ydd) { OpenTextures = saved => tellOther = saved });
        Settle(other);
        Assert.True(other.OpenTextures());
        var edited = new FiveFury.Textures.Texture("tri_d", 16, 16, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[128]);
        tellOther!(new SavedDocument([edited], []));
        Wait(other.TexturesSaved);
        Assert.Same(edited, other.Surfaces!.Materials.Single().Diffuse);
    }

    [AvaloniaFact]
    public void The_parts_arrow_and_the_tool_strip_are_drawn_over_the_viewports_left_edge()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri", Bound = new BoundBox(new Aabb3(new Vector3(0), new Vector3(1))) };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        var window = new Views.MainWindow { Width = 1360, Height = 840 };
        window.Show();
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr)) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        Settle(viewer);
        ExplorerStateRenders.Pump(400);

        // Each in a window of its own, over the main one: the viewport is a native window, which covers what's drawn in it.
        Assert.True(viewer.ToolStripOverlay.IsShown);
        Assert.True(viewer.PartsTabOverlay.IsShown);
        Avalonia.Controls.Window tools = viewer.ToolStripOverlay.Window!, tab = viewer.PartsTabOverlay.Window!;
        Assert.NotSame(tools, tab);
        Assert.Same(window, tools.Owner);
        Assert.Same(window, tab.Owner);
        Assert.Same(tools, Avalonia.Controls.TopLevel.GetTopLevel(viewer.SkeletonButton));
        Assert.Same(tab, Avalonia.Controls.TopLevel.GetTopLevel(viewer.PartsArrow));
        Assert.False(tools.ShowActivated);
        Assert.False(tab.ShowActivated);

        Views.Controls.ChromeToolButton[] strip = [viewer.SkeletonButton, viewer.SolidButton, viewer.LightsButton];
        Assert.All(strip, button => Assert.Equal(new Avalonia.Size(26, 26), button.Bounds.Size));
        // One above the other, joined by a one-pixel seam.
        Assert.Equal(new Avalonia.Point(0, 27), Avalonia.VisualExtensions.TranslatePoint(strip[1], default, strip[0]));
        Assert.Equal(new Avalonia.Point(0, 27), Avalonia.VisualExtensions.TranslatePoint(strip[2], default, strip[1]));
        // Joined segments with no panel behind them, as Blender's: the collision this model has is a solid segment; the
        // skeleton and lights it hasn't have their glyphs faded right out, so they don't read as merely off, and their
        // tooltips still show, to say why.
        Assert.All(strip, button => Assert.True(button.Segment));
        Assert.True(viewer.SolidButton.IsEnabled);
        Assert.False(viewer.LightsButton.IsEnabled);
        Assert.True(Avalonia.Controls.ToolTip.GetShowOnDisabled(viewer.LightsButton));
        double Glyph(Views.Controls.ChromeToolButton button) => button.GetVisualDescendants().OfType<Views.Controls.ChromeIcon>().Single().Opacity;
        Assert.Equal(1, Glyph(viewer.SolidButton));
        Assert.Equal(Views.Controls.ChromeToolButton.DisabledOpacity, Glyph(viewer.LightsButton));
        Avalonia.Controls.ToolTip.SetIsOpen(viewer.LightsButton, true);
        Assert.Equal("No lights", Views.Controls.WindowToolTip.ShownText);
        // A disabled tool gets no pointer events of its own, so the strip's window tells when the pointer leaves it.
        tools.MouseMove(Avalonia.VisualExtensions.TranslatePoint(viewer.LightsButton, new Avalonia.Point(13, 13), tools)!.Value);
        Assert.Equal("No lights", Views.Controls.WindowToolTip.ShownText);
        tools.MouseMove(Avalonia.VisualExtensions.TranslatePoint(viewer.SolidButton, new Avalonia.Point(13, 13), tools)!.Value);
        Assert.Null(Views.Controls.WindowToolTip.ShownText);
        // The arrow on the viewport's left edge, at its top; the strip a little in from the edge, centred on its height.
        void Centred()
        {
            Avalonia.PixelPoint edge = Avalonia.VisualExtensions.PointToScreen(viewer.Viewport, default);
            double scale = tools.RenderScaling;
            double middle = edge.Y / scale + viewer.Viewport.Bounds.Height / 2;
            Avalonia.Rect On(Avalonia.Controls.Control control)
            {
                Avalonia.PixelPoint corner = Avalonia.VisualExtensions.PointToScreen(control, default);
                return new Avalonia.Rect(corner.X / scale, corner.Y / scale, control.Bounds.Width, control.Bounds.Height);
            }
            Avalonia.Rect arrow = On(viewer.PartsTab), well = On(viewer.ToolStrip);
            Assert.Equal(edge.X / scale, arrow.X, 0.5);
            Assert.Equal(edge.Y / scale + Ui.Theme.PartsTabTop, arrow.Y, 0.5);
            Assert.Equal(edge.X / scale + Ui.Theme.ToolStripInset, well.X, 0.5);
            Assert.Equal(middle, well.Center.Y, 1.0);
            // Apart: the arrow takes nothing from the strip.
            Assert.True(arrow.Bottom < well.Y);
        }
        Centred();
        // It follows the viewport: closing the parts panel moves the viewport's edge, and the tools with it.
        viewer.SetPartsShown(false);
        ExplorerStateRenders.Pump(400);
        Assert.False(viewer.PartsHandle.IsVisible);
        Centred();
        // So does the main window, moved.
        window.Position = new Avalonia.PixelPoint(window.Position.X + 40, window.Position.Y + 30);
        ExplorerStateRenders.Pump(100);
        Centred();

        // The pointer doesn't go through the tab or the strip to the viewport.
        Avalonia.Point Middle(Avalonia.Controls.Control control, Avalonia.Controls.Window on) =>
            Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), on)!.Value;
        Assert.True(viewer.PartsTabOverlay.Covers(Middle(viewer.PartsArrow, tab)));
        Assert.True(viewer.ToolStripOverlay.Covers(Middle(viewer.SolidButton, tools)));

        // It hides while the viewport does, under another overlay, and shows again after.
        Views.ModalOverlay above = Assert.IsType<Views.ModalOverlay>(Views.ModalOverlay.Show(window.ExplorerScreenHost, new Avalonia.Controls.Border { Width = 200, Height = 100 }));
        ExplorerStateRenders.Pump(50);
        Assert.False(viewer.ToolStripOverlay.IsShown);
        Assert.False(viewer.PartsTabOverlay.IsShown);
        above.Close();
        ExplorerStateRenders.Pump(400);
        Assert.True(viewer.ToolStripOverlay.IsShown);
        Assert.True(viewer.PartsTabOverlay.IsShown);
        Centred();

        // And goes with the viewer.
        viewer.Close();
        ExplorerStateRenders.Pump(300);
        Assert.False(viewer.ToolStripOverlay.IsShown);
        Assert.False(tools.IsVisible);
        Assert.False(tab.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void A_drawables_collision_is_drawn_over_it_with_K()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri", Bound = new BoundBox(new Aabb3(new Vector3(0), new Vector3(1))) };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();

        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        Settle(viewer);
        Assert.False(viewer.SkeletonButton.IsEnabled);
        // C, which drew its edges, does nothing now: the collision is drawn solid, with its edges, with K.
        Assert.False(viewer.Shortcut('C'));
        Assert.True(viewer.SolidButton.IsEnabled);
        Assert.False(viewer.OverlayShown(Views.ModelViewer.Overlay.Solid));
        Assert.True(viewer.Shortcut('K'));
        Assert.True(viewer.OverlayShown(Views.ModelViewer.Overlay.Solid));
        Assert.True(viewer.SolidButton.Sunken);
        Assert.True(viewer.Shortcut('k'));
        Assert.False(viewer.SolidButton.Sunken);
    }

    [AvaloniaFact]
    public void The_viewport_hides_while_another_overlay_is_over_the_viewer()
    {
        var window = new Views.MainWindow { Width = 1360, Height = 840 };
        window.Show();
        var box = new Aabb3(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var viewer = new Views.ModelViewer(new ModelFile("box.ybn", new Ybn(new BoundBox(box)).Write())) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        ExplorerStateRenders.Pump(400);
        Assert.True(viewer.Viewport.IsVisible);

        // The viewport is a native window, which would cover anything above it: it hides while something is.
        Views.ModalOverlay above = Assert.IsType<Views.ModalOverlay>(Views.ModalOverlay.Show(window.ExplorerScreenHost, new Avalonia.Controls.Border { Width = 200, Height = 100 }));
        ExplorerStateRenders.Pump(50);
        Assert.False(viewer.Viewport.IsVisible);
        above.Close();
        ExplorerStateRenders.Pump(400);
        Assert.True(viewer.Viewport.IsVisible);

        viewer.Close();
        ExplorerStateRenders.Pump(300);
        window.Close();
    }

    [AvaloniaFact]
    public void The_time_of_day_opens_in_a_dropdown_window_over_the_viewport()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        var window = new Views.MainWindow { Width = 1360, Height = 840 };
        window.Show();
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr)) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        Settle(viewer);
        ExplorerStateRenders.Pump(400);

        // A window of its own (the viewport would cover a popup drawn in the main one), and the viewport stays.
        viewer.ToggleTime();
        Assert.True(viewer.TimeOpen);
        Assert.True(viewer.TimeButton.Sunken);
        Assert.Contains(viewer.SunSlider, Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(viewer.SunSlider).OfType<Avalonia.Controls.Window>()
            .SelectMany(owner => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(owner)));
        Assert.True(viewer.Viewport.IsVisible);
        viewer.ToggleTime();
        Assert.False(viewer.TimeOpen);
        Assert.False(viewer.TimeButton.Sunken);

        // Another window taking the keyboard closes it, once the change of window is over, not in the middle of it
        // (Windows then gave the keyboard to another app, and the main window went behind it). Past the moment in which a
        // press that closed it doesn't open it again.
        ExplorerStateRenders.Pump(450);
        viewer.ToggleTime();
        Assert.True(viewer.TimeOpen);
        var dropdown = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(viewer.SunSlider).OfType<Avalonia.Controls.Window>().Single();
        // As the platform says it lost the keyboard (the headless one doesn't move it between windows itself).
        object platform = dropdown.PlatformImpl!;
        var deactivated = (Action?)platform.GetType().GetInterfaces().Append(platform.GetType())
            .Select(type => type.GetProperty("Deactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .First(property => property is not null)!.GetValue(platform);
        deactivated!();
        Assert.True(viewer.TimeOpen);
        ExplorerStateRenders.Pump(100);
        Assert.False(viewer.TimeOpen);

        viewer.Close();
        ExplorerStateRenders.Pump(300);
        window.Close();
    }

    [AvaloniaFact]
    public void The_viewers_tooltips_open_in_windows_of_their_own_over_the_viewport()
    {
        var window = new Views.MainWindow { Width = 1360, Height = 840 };
        window.Show();
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", [])) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        ExplorerStateRenders.Pump(300);

        // In the viewer: drawn in a window of its own, in place of the in-window tooltip the viewport would cover.
        Avalonia.Controls.ToolTip.SetIsOpen(viewer.FrameButton, true);
        Assert.False(Avalonia.Controls.ToolTip.GetIsOpen(viewer.FrameButton));
        Assert.Equal("Frame the model, orbiting it  (F)", Views.Controls.WindowToolTip.ShownText);
        Assert.Contains(window.OwnedWindows, owned => owned.Content is Avalonia.Controls.Border);
        // Held at its own size in the window's corner: Windows keeps such a window at least 39 pixels high, which
        // stretched a one-line tooltip.
        var body = (Avalonia.Controls.Border)window.OwnedWindows.Single(owned => owned.Content is Avalonia.Controls.Border).Content!;
        Assert.Equal((Avalonia.Layout.HorizontalAlignment.Left, Avalonia.Layout.VerticalAlignment.Top), (body.HorizontalAlignment, body.VerticalAlignment));
        // A tooltip that changes, as the parts arrow's does when it's pressed, closes it.
        Avalonia.Controls.ToolTip.SetTip(viewer.FrameButton, "Changed");
        Assert.Null(Views.Controls.WindowToolTip.ShownText);
        // So does the viewer closing.
        Avalonia.Controls.ToolTip.SetIsOpen(viewer.TimeButton, true);
        Assert.Equal("Time of day", Views.Controls.WindowToolTip.ShownText);
        viewer.Close();
        ExplorerStateRenders.Pump(300);
        Assert.Null(Views.Controls.WindowToolTip.ShownText);

        // Outside it, tooltips are drawn in the window as before.
        var outside = new Avalonia.Controls.Button();
        Views.Controls.ThemedToolTip.Set(outside, "Elsewhere");
        window.ExplorerScreenHost.Content = outside;
        ExplorerStateRenders.Pump(100);
        Avalonia.Controls.ToolTip.SetIsOpen(outside, true);
        Assert.True(Avalonia.Controls.ToolTip.GetIsOpen(outside));
        Assert.Null(Views.Controls.WindowToolTip.ShownText);
        Avalonia.Controls.ToolTip.SetIsOpen(outside, false);
        window.Close();
    }

    [Fact]
    public void A_window_tooltip_reads_text_tips_only()
    {
        Assert.Equal("a", Views.Controls.WindowToolTip.TextOf("a"));
        Assert.Equal("b", Views.Controls.WindowToolTip.TextOf(new Avalonia.Controls.TextBlock { Text = "b" }));
        Assert.Equal("c", Views.Controls.WindowToolTip.TextOf(new Avalonia.Controls.ToolTip { Content = "c" }));
        Assert.Equal("d", Views.Controls.WindowToolTip.TextOf(new Avalonia.Controls.ToolTip { Content = new Avalonia.Controls.TextBlock { Text = "d" } }));
        Assert.Null(Views.Controls.WindowToolTip.TextOf(new Avalonia.Controls.Border()));
        Assert.Null(Views.Controls.WindowToolTip.TextOf(null));
    }

    [AvaloniaFact]
    public void A_drawable_shows_at_once_and_gets_the_games_textures_when_they_come()
    {
        // A triangle whose texture is only in the game's dictionaries, whose tables are still being worked out.
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        mesh.SetChannel(CommonFury.Vertices.VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(0, 1, 0, 0)]);
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "sign" };
        var material = new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("sign_d");
        builder.Materials.Add(material);
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        var tables = new TaskCompletionSource<IReadOnlyList<NearbyDictionary>>();

        var viewer = new Views.ModelViewer(new ModelFile("sign.ydr", ydr) { GameDictionaries = tables.Task });
        Settle(viewer);
        // Shown without waiting, its texture missing for now; a part hidden meanwhile.
        Assert.True(viewer.Toolbar.IsVisible);
        Assert.Equal("1 texture not found", viewer.Location);
        Assert.False(viewer.GameTexturesAdded.IsCompleted);
        viewer.Parts.Flip(viewer.Parts.Rows[1].Node);

        // The tables come: the drawable is read again with the game's dictionary, and shown as it was.
        byte[] dictionary = Textures.TextureTestKit.Bytes(Textures.TextureTestKit.Dictionary("sign_d"));
        tables.SetResult([new NearbyDictionary("sign_txd.ytd", dictionary.Length, () => dictionary)]);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.GameTexturesAdded.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        Assert.True(viewer.GameTexturesAdded.IsCompletedSuccessfully);
        Assert.Equal("", viewer.Location);
        Assert.Equal([false], viewer.Visibility!.Mask());
    }

    [AvaloniaFact]
    public void The_levels_a_drawable_has_are_segments_and_picking_one_shows_it()
    {
        // A triangle at the high level and another at the medium one.
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "sign" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        foreach (var lod in new[] { CommonFury.Assets.DrawableLod.High, CommonFury.Assets.DrawableLod.Medium })
        {
            var model = new FiveFury.Drawables.DrawableModelInput { Lod = lod };
            model.Meshes.Add(new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface"));
            builder.Models.Add(model);
        }
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();

        var viewer = new Views.ModelViewer(new ModelFile("sign.ydr", ydr));
        Settle(viewer);
        Assert.True(viewer.LodSwitch.IsVisible);
        Assert.Equal(["High", "Med"], viewer.LodSwitch.Sides.Select(side => side.Text));
        Assert.Equal(0, viewer.LodSwitch.SelectedIndex);
        Assert.True(viewer.Shortcut('2'));
        Assert.Equal(1, viewer.Visibility!.Lod);
        Assert.Equal(1, viewer.LodSwitch.SelectedIndex);
        // A level it lacks is left alone.
        viewer.Shortcut('3');
        Assert.Equal(1, viewer.LodSwitch.SelectedIndex);
    }

    [AvaloniaFact]
    public void Typing_in_the_parts_filter_is_not_a_shortcut_and_a_framed_part_turns_the_camera()
    {
        var mesh = new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface");
        var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tri" };
        builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new FiveFury.Drawables.DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] ydr = new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write();
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr));
        // In a window, so a key bubbles from the panel up through the viewer.
        var window = new Avalonia.Controls.Window { Width = 900, Height = 600, Content = viewer };
        window.Show();
        Settle(viewer);
        ExplorerStateRenders.Pump(100);
        Assert.True(viewer.Viewport.Shadows);
        Avalonia.Input.KeyEventArgs Press(Avalonia.Interactivity.Interactive source) => new()
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.H,
            Source = source,
        };
        // H in the filter is a letter of its text, not the shadows' key.
        viewer.Parts.Filter.RaiseEvent(Press(viewer.Parts.Filter));
        Assert.True(viewer.Viewport.Shadows);
        // In the panel itself, it is still the viewer's.
        viewer.Parts.RaiseEvent(Press(viewer.Parts));
        Assert.False(viewer.Viewport.Shadows);
        // Framing a part asks the viewport to turn to its box, which has none to turn yet without a GPU.
        viewer.FrameParts([viewer.Parts.Rows[0].Node]);
        window.Close();
    }

    [AvaloniaFact]
    public void A_collision_has_the_cameras_buttons_and_no_time_of_day()
    {
        var box = new Aabb3(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var viewer = new Views.ModelViewer(new ModelFile("box.ybn", new Ybn(new BoundBox(box)).Write()));
        Settle(viewer);
        // Of the toolbar, a collision or a navmesh has the camera: orbit or fly, and frame. Nothing of a drawable's.
        Assert.True(viewer.Toolbar.IsVisible);
        Assert.False(viewer.ModelTools.IsVisible);
        Assert.False(viewer.TimeWell.IsVisible);
        Assert.True(viewer.OrbitButton.Sunken);
        Assert.False(viewer.StatsPanel.IsVisible);
        Assert.True(viewer.Shortcut('V'));
        Assert.True(viewer.Viewport.FreeCamera);
        Assert.True(viewer.FreeButton.Sunken);
        // Flying, the status bar says how fast, without a drawable's counts.
        Assert.True(viewer.StatsPanel.IsVisible);
        Assert.True(viewer.SpeedStat.IsVisible);
        Assert.False(viewer.TrianglesStat.IsVisible);
        Assert.False(viewer.VerticesStat.IsVisible);
        Assert.False(viewer.EdgesStat.IsVisible);
        Assert.True(viewer.Shortcut('f'));
        Assert.False(viewer.Viewport.FreeCamera);
        Assert.False(viewer.Shortcut('T'));
        Assert.False(viewer.ToolStripOverlay.IsWanted);
        Assert.False(viewer.StatsPanel.IsVisible);
        Assert.False(viewer.Shortcut('H'));

        // Its materials are in the parts panel, open, each with its colour: the box's one, the default.
        Assert.True(viewer.PartsTabOverlay.IsWanted);
        Assert.True(viewer.PartsShown);
        Assert.True(viewer.PartsHandle.IsVisible);
        MeshParts marks = Assert.IsType<MeshParts>(viewer.Parts.Parts);
        Assert.Same(marks, viewer.Marks);
        Assert.Equal(CollisionMesh.Section, Assert.Single(viewer.Parts.Sections).Title);
        PartNode material = Assert.Single(viewer.Parts.Rows).Node;
        Assert.Equal(("Default", PartKind.Mark), (material.Name, material.Kind));
        Assert.NotNull(material.Colour);
        Assert.Equal("1 / 1", viewer.Parts.CountText);
        // Hidden from the panel, it isn't drawn; the panel closes and opens with P, as a drawable's does.
        viewer.Parts.Flip(material);
        Assert.Empty(marks.Mesh.ToArray());
        Assert.Equal("0 / 1", viewer.Parts.CountText);
        viewer.Parts.ShowAll();
        Assert.Equal(36, marks.Mesh.ToArray().Length);
        Assert.True(viewer.Shortcut('P'));
        Assert.False(viewer.PartsShown);
        Assert.True(viewer.Shortcut('p'));
        Assert.True(viewer.PartsShown);
        // Framing it asks the viewport to turn to its box, which has none to turn yet without a GPU.
        viewer.FrameParts([material]);
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
