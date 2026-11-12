using System.Numerics;
using CommonFury.Hashing;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class CarPaintTests
{
    // Three of carcols' metallic settings: the metallics', the black metallics' and the mattes'.
    private static readonly CarMetallic Metallic = new(.8f, 510, .96f), BlackMetallic = new(.5f, 500, .96f), Matte = new(.5f, 5, .96f);

    // A few of the game's colours, named as carcols has them.
    private static readonly CarPaints Paints = new(
    [
        new(0, CarColourNames.Tidy("0 Metallic Black"), 0xFF080808, "Metallic", BlackMetallic),
        new(4, CarColourNames.Tidy("4 Metallic Silver"), 0xFF5A5E66, "Metallic", Metallic),
        new(12, CarColourNames.Tidy("12 Matte Black"), 0xFF131313, "Matte", Matte),
        new(27, CarColourNames.Tidy("27 Metallic Red"), 0xFFC00E1A, "Metallic", Metallic),
        new(111, CarColourNames.Tidy("111 Metallic White"), 0xFFF0F0F0, "Metallic"),
        new(156, CarColourNames.Tidy("156 DEFAULT ALLOY COLOR"), 0xFF414347, "Other"),
    ],
    [
        [4, 12, 111, 156, 0, 0],
        [27, 0, 111, 156],
    ]);

    private static Vector3 Srgb(uint argb) => new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f);

    // A vehicle paint drawn in its primary slot, with the second specular lobe the pearlescent colours.
    private static byte[] Car(string shader = "vehicle_paint1")
    {
        var mesh = new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], [0, 1, 2], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 3)]);
        var material = new DrawableMaterialInput { Name = "surface", Shader = shader };
        material.Parameters["matDiffuseColor"] = [2, 1, 1];
        material.Parameters["specular2Color_DirLerp"] = [0, .5f, 0, .25f];
        var builder = new DrawableBuilder { Name = "car" };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    // A prop: one triangle, not a vehicle shader.
    private static byte[] Prop()
    {
        var builder = new DrawableBuilder { Name = "prop" };
        builder.Materials.Add(new DrawableMaterialInput { Name = "surface", Shader = "default" });
        var model = new DrawableModelInput();
        model.Meshes.Add(new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], "surface"));
        builder.Models.Add(model);
        return new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    [Theory]
    [InlineData("0 Metallic Black", "Metallic Black", "Metallic")]
    [InlineData("156 DEFAULT ALLOY COLOR", "Default Alloy", "Other")]
    [InlineData("158 MP100 GOLD SATIN", "MP100 Gold Satin", "Other")]
    [InlineData("Mettalic Black Blue", "Metallic Black Blue", "Metallic")]
    [InlineData("Metaillic V Dark Blue", "Metallic V Dark Blue", "Metallic")]
    [InlineData("Uil Lightning blue", "Util Lightning Blue", "Util")]
    [InlineData("155 Matte Foilage Green", "Matte Foliage Green", "Matte")]
    [InlineData("127police car blue", "Police Car Blue", "Other")]
    [InlineData("117 Brushed Steel", "Brushed Steel", "Metals")]
    [InlineData("120 Chrome", "Chrome", "Metals")]
    [InlineData("21 Worn Graphite", "Worn Graphite", "Worn")]
    public void The_games_colour_names_are_tidied_and_grouped_by_finish(string name, string tidy, string finish)
    {
        Assert.Equal(tidy, CarColourNames.Tidy(name));
        Assert.Equal(finish, CarColourNames.Finish(name));
    }

    [Fact]
    public void Each_paint_slot_takes_its_colour_and_the_game_greys_the_rest()
    {
        VehiclePaint paint = VehiclePaint.Of(Paints, [27, 0, 111, 156]);
        Vector3 grey = new(150 / 255f);
        // (2, n, n): 1 to 4 the first four slots, 5 left white, 6 and 7 interior and dashboard, grey where none is set.
        Assert.Equal(Srgb(0xFFC00E1A), paint.Layer(1));
        Assert.Equal(Srgb(0xFF080808), paint.Layer(2));
        Assert.Equal(Srgb(0xFFF0F0F0), paint.Layer(3));
        Assert.Equal(Srgb(0xFF414347), paint.Layer(4));
        Assert.Equal(Vector3.One, paint.Layer(5));
        Assert.Equal(grey, paint.Layer(6));
        Assert.Equal(grey, paint.Layer(7));
        Assert.Equal(Srgb(0xFFC00E1A), SurfaceMaterial.PaintColour([2, 1, 1], paint));
        // A number the game has no colour for is its grey.
        Assert.Equal(grey, VehiclePaint.Of(Paints, [999]).Layer(1));
        Assert.Equal(["Metallic", "Matte", "Other"], Paints.Finishes);
    }

    [Fact]
    public void A_scene_repaints_only_its_vehicle_materials()
    {
        DrawableScene scene = ModelContent.Read("car.ydr", Car()).Surfaces!;
        Assert.True(scene.Paintable);
        // The game's grey to begin with; repainted, the paint slot and the pearlescent lobe take the colours.
        Assert.Equal(150 / 255f, scene.Materials[0].Uniforms[16], 4);
        Assert.Equal([0], scene.Repaint(VehiclePaint.Of(Paints, Paints.Combinations[1])));
        Vector3 red = Srgb(0xFFC00E1A), white = Srgb(0xFFF0F0F0);
        Assert.Equal([red.X, red.Y, red.Z], scene.Materials[0].Uniforms[16..19]);
        Assert.Equal([white.X, white.Y, white.Z], scene.Materials[0].Uniforms[28..31]);
        // What isn't a vehicle shader has no paint.
        Assert.False(ModelContent.Read("prop.ydr", Prop()).Surfaces!.Paintable);
    }

    [Fact]
    public void A_paint_slot_takes_its_colours_metallic_setting_in_place_of_its_materials_specular()
    {
        DrawableScene scene = ModelContent.Read("car.ydr", Car()).Surfaces!;
        float[] own = scene.Materials[0].Uniforms[0..3];
        // Red's setting, the metallics': fresnel, falloff and intensity as specFresnel, specFalloff and specInt.
        scene.Repaint(VehiclePaint.Of(Paints, [27]));
        Assert.Equal([.96f, 510, .8f], scene.Materials[0].Uniforms[0..3]);
        scene.Repaint(VehiclePaint.Of(Paints, [12]));
        Assert.Equal([.96f, 5, .5f], scene.Materials[0].Uniforms[0..3]);
        // A colour without one, or no colours at all, keeps the material's.
        scene.Repaint(VehiclePaint.Of(Paints, [156]));
        Assert.Equal(own, scene.Materials[0].Uniforms[0..3]);
        scene.Repaint(VehiclePaint.Unset);
        Assert.Equal(own, scene.Materials[0].Uniforms[0..3]);
        // The slot the game leaves white has none, and each other slot is its own colour's.
        VehiclePaint paint = VehiclePaint.Of(Paints, [27, 0, 12, 156, 4, 12]);
        Assert.Equal([Metallic, BlackMetallic, Matte, null, null, Metallic, Matte], Enumerable.Range(1, 7).Select(paint.Metallic));
    }

    [Fact]
    public void The_tables_give_a_vehicle_the_games_colours_and_its_combinations()
    {
        var tables = new GameTextures.Tables();
        // No colours, no paint: a game whose carcols couldn't be read.
        Assert.Null(tables.PaintsFor("adder.yft"));
        tables.Colours[0] = ("0 Metallic Black", 0xFF080808, BlackMetallic);
        tables.Colours[156] = ("156 DEFAULT ALLOY COLOR", 0xFF414347, null);
        tables.Combinations[new Hash32("adder").Value] = [[0, 0, 0, 156], [0, -1, 0, 156, 0, 0]];

        CarPaints adder = tables.PaintsFor("adder.yft")!;
        Assert.Equal(["Metallic Black", "Default Alloy"], adder.Colours.Select(colour => colour.Name));
        Assert.Equal([BlackMetallic, null], adder.Colours.Select(colour => colour.Metallic));
        Assert.Equal([0, 0, 0, 156], adder.Combinations[0]);
        // A slot carvariations leaves out is none.
        Assert.Equal([0, null, 0, 156, 0, 0], adder.Combinations[1]);
        // The high-detail model is the vehicle's; one carvariations doesn't list has the colours, no combinations.
        Assert.Equal(2, tables.PaintsFor("adder_hi.yft")!.Combinations.Count);
        Assert.Empty(tables.PaintsFor("mymod.yft")!.Combinations);
        Assert.Equal(2, tables.PaintsFor("mymod.yft")!.Colours.Count);
    }

    [Fact]
    public void The_colours_and_combinations_are_kept_in_the_cache()
    {
        string folder = Directory.CreateTempSubdirectory("hh-car-paint-").FullName;
        try
        {
            var tables = new GameTextures.Tables();
            tables.Colours[27] = ("27 Metallic Red", 0xFFC00E1A, Metallic);
            tables.Colours[156] = ("156 DEFAULT ALLOY COLOR", 0xFF414347, null);
            tables.Combinations[new Hash32("sultan").Value] = [[27, 27, 5, 156, 0, 0], [0, 0, 10, 156]];
            byte[] stamp = [1, 2, 3];
            string cache = Path.Combine(folder, "tables.bin");
            tables.Write(cache, stamp);
            GameTextures.Tables read = GameTextures.Tables.Read(cache, stamp)!;
            Assert.Equal(("27 Metallic Red", 0xFFC00E1AU, Metallic), read.Colours[27]);
            Assert.Equal(("156 DEFAULT ALLOY COLOR", 0xFF414347U, (CarMetallic?)null), read.Colours[156]);
            Assert.Equal([[27, 27, 5, 156, 0, 0], [0, 0, 10, 156]], read.Combinations[new Hash32("sultan").Value]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Views.ModelViewer Open(ModelFile file)
    {
        var viewer = new Views.ModelViewer(file);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        return viewer;
    }

    [AvaloniaFact]
    public void The_games_textures_found_while_its_paints_are_read_are_still_added()
    {
        // The car's texture is only in the game's dictionaries, whose tables are done once the drawable is read, while its
        // paints are still being looked up: the drawable was read without them, so it is read again with them.
        var mesh = new DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], [0, 1, 2], "surface");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 3)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0)]);
        var material = new DrawableMaterialInput { Name = "surface", Shader = "vehicle_paint1" };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("car_d");
        var builder = new DrawableBuilder { Name = "car" };
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        byte[] car = new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
        var tables = new TaskCompletionSource<IReadOnlyList<NearbyDictionary>>();
        var paints = new TaskCompletionSource<CarPaints?>();
        var viewer = new Views.ModelViewer(new ModelFile("car.ydr", car) { GameDictionaries = tables.Task, GamePaints = paints.Task });
        TestWait.Pump(300);
        byte[] dictionary = Textures.TextureTestKit.Bytes(Textures.TextureTestKit.Dictionary("car_d"));
        tables.SetResult([new NearbyDictionary("car_txd.ytd", dictionary.Length, () => dictionary)]);
        paints.SetResult(Paints);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        while (!viewer.GameTexturesAdded.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        Assert.True(viewer.GameTexturesAdded.IsCompletedSuccessfully);
        Assert.Equal("", viewer.Location);
    }

    [AvaloniaFact]
    public void A_vehicle_opened_while_the_games_tables_are_worked_out_is_shown_at_once_and_painted_when_they_are()
    {
        // The tables can take seconds to make afresh: the car doesn't wait for its paint.
        var paints = new TaskCompletionSource<CarPaints?>();
        Views.ModelViewer viewer = Open(new ModelFile("car.ydr", Car()) { GamePaints = paints.Task });
        Assert.True(viewer.Reading.IsCompletedSuccessfully);
        Assert.Equal(150 / 255f, viewer.Surfaces!.Materials[0].Uniforms[16], 4);
        Assert.False(viewer.PaintWell.IsVisible);
        Assert.False(viewer.PaintsAdded.IsCompleted);

        // Once they are done, it takes its first combination and its well, as it would have from the start.
        paints.SetResult(Paints);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.PaintsAdded.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            TestWait.Pump(10);
        Vector3 silver = Srgb(0xFF5A5E66);
        Assert.Equal([silver.X, silver.Y, silver.Z], viewer.Surfaces!.Materials[0].Uniforms[16..19]);
        Assert.Equal([4, 12, 111, 156, 0, 0], viewer.PaintIndices);
        Assert.True(viewer.PaintWell.IsVisible);
    }

    [AvaloniaFact]
    public void A_vehicle_from_a_game_opens_in_its_first_combination_and_its_paint_is_picked_in_the_footer()
    {
        Views.ModelViewer viewer = Open(new ModelFile("car.ydr", Car()) { GamePaints = Task.FromResult<CarPaints?>(Paints) });
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        TestWait.Pump(50);
        float[] Drawn() => viewer.Surfaces!.Materials[0].Uniforms[16..19];
        Vector3 silver = Srgb(0xFF5A5E66), red = Srgb(0xFFC00E1A), black = Srgb(0xFF080808);

        // Painted from the first frame in the first combination the game lists; the well beside the level says so.
        Assert.True(viewer.PaintWell.IsVisible);
        Assert.Equal([4, 12, 111, 156, 0, 0], viewer.PaintIndices);
        Assert.Equal([silver.X, silver.Y, silver.Z], Drawn());

        // Its dropdown: another combination, all six slots at once.
        viewer.TogglePaint();
        Views.PaintPanel panel = Assert.IsType<Views.PaintPanel>(viewer.PaintPanel);
        Assert.Equal(0, panel.Combination);
        panel.PickCombination(1);
        Assert.Equal([27, 0, 111, 156, null, null], viewer.PaintIndices);
        Assert.Equal([red.X, red.Y, red.Z], Drawn());
        Assert.Equal(1, panel.Combination);

        // A slot's own colour: the picker opens in the finish of the colour it has, and stays open on it once one is
        // picked, so another can be tried; Escape goes back to the slots.
        panel.ShowPicker(0);
        Assert.Equal(0, panel.PickingSlot);
        Assert.Equal("Metallic", panel.Finish);
        panel.PickColour(0, 0);
        Assert.Equal(0, panel.PickingSlot);
        Assert.Equal("Metallic", panel.Finish);
        Assert.Equal([black.X, black.Y, black.Z], Drawn());
        Assert.Null(panel.Combination);
        panel.PickColour(0, 4);
        Assert.Equal([silver.X, silver.Y, silver.Z], Drawn());
        panel.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
        Assert.Null(panel.PickingSlot);

        viewer.TogglePaint();
        TestWait.Pump(50);
        Assert.Null(viewer.PaintPanel);
        viewer.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_colour_or_combination_under_the_pointer_is_shown_on_the_vehicle_until_the_pointer_is_off_it()
    {
        Views.ModelViewer viewer = Open(new ModelFile("car.ydr", Car()) { GamePaints = Task.FromResult<CarPaints?>(Paints) });
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        TestWait.Pump(50);
        float[] Drawn() => viewer.Surfaces!.Materials[0].Uniforms[16..19];
        Vector3 silver = Srgb(0xFF5A5E66), red = Srgb(0xFFC00E1A);
        viewer.TogglePaint();
        Views.PaintPanel panel = viewer.PaintPanel!;
        var top = (Avalonia.Controls.TopLevel)Avalonia.Controls.TopLevel.GetTopLevel(panel)!;
        TestWait.Pump(50);
        Avalonia.Controls.Control Named(Func<string, bool> name) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel)
            .OfType<Avalonia.Controls.Control>().First(control => Avalonia.Automation.AutomationProperties.GetName(control) is { } named && name(named));
        void Over(Avalonia.Controls.Control control, Avalonia.Point at)
        {
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, Avalonia.VisualExtensions.TranslatePoint(control, at, top)!.Value);
            TestWait.Pump(20);
        }
        Avalonia.Point Middle(Avalonia.Controls.Control control) => new(control.Bounds.Width / 2, control.Bounds.Height / 2);

        // The pointer over the second combination: the vehicle shows all its slots, and its paint is still the first.
        Avalonia.Controls.Control tile = Named(name => name.StartsWith("Combination 2:", StringComparison.Ordinal));
        Over(tile, Middle(tile));
        Assert.Equal(1, panel.PreviewedCombination);
        Assert.Equal([red.X, red.Y, red.Z], Drawn());
        Assert.Equal([4, 12, 111, 156, 0, 0], viewer.PaintIndices);
        Over(panel, new Avalonia.Point(2, 2));
        Assert.Null(panel.PreviewedCombination);
        Assert.Equal([silver.X, silver.Y, silver.Z], Drawn());

        // The pointer over Metallic Red in the primary's colours: the vehicle shows it, its paint still silver.
        panel.ShowPicker(0);
        TestWait.Pump(50);
        Avalonia.Controls.Control cell = Named(name => name == Paints.Colour(27)!.Name);
        Over(cell, Middle(cell));
        Assert.Equal(27, panel.PreviewColour);
        Assert.Equal([red.X, red.Y, red.Z], Drawn());
        Assert.Equal(4, viewer.PaintIndices[0]);

        // Over the well where it has no colour, back to the silver; and a preview the dropdown closes on goes with it.
        var row = (Avalonia.Controls.Control)Avalonia.VisualTree.VisualExtensions.GetVisualParent(cell)!;
        Over(row, new Avalonia.Point(row.Bounds.Width - 6, row.Bounds.Height / 2));
        Assert.Null(panel.PreviewColour);
        Assert.Equal([silver.X, silver.Y, silver.Z], Drawn());
        panel.Preview(27);
        Assert.Equal([red.X, red.Y, red.Z], Drawn());
        viewer.TogglePaint();
        TestWait.Pump(50);
        Assert.Equal([silver.X, silver.Y, silver.Z], Drawn());
        Assert.Equal(4, viewer.PaintIndices[0]);
        viewer.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void The_colour_picker_keeps_its_size_from_one_finish_to_another()
    {
        // Thirty metallics, three rows of them, and one matte.
        CarPaints many = new(
            [.. Enumerable.Range(0, 30).Select(i => new CarColour(i, $"Metallic {i}", 0xFF000000u | (uint)(i * 8), "Metallic")),
             new CarColour(30, "Matte Black", 0xFF131313, "Matte")],
            []);
        var panel = new Views.PaintPanel(many, [0, 0, 0, 0, 0, 0]);
        var window = new Avalonia.Controls.Window { Width = 600, Height = 600, Content = panel };
        window.Show();
        panel.ShowPicker(0);
        TestWait.Pump(20);
        double metallic = panel.DesiredSize.Height;
        panel.ShowFinish("Matte");
        TestWait.Pump(20);
        Assert.Equal("Matte", panel.Finish);
        Assert.Equal(metallic, panel.DesiredSize.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Without_a_game_or_a_vehicle_there_is_no_paint()
    {
        // A vehicle opened outside a game: the game's grey, and no well.
        Views.ModelViewer loose = Open(new ModelFile("car.ydr", Car()));
        Assert.False(loose.PaintWell.IsVisible);
        Assert.Equal(150 / 255f, loose.Surfaces!.Materials[0].Uniforms[16], 4);
        loose.Close();
        // Something that isn't a vehicle, from a game: no well either.
        Views.ModelViewer prop = Open(new ModelFile("prop.ydr", Prop()) { GamePaints = Task.FromResult<CarPaints?>(Paints) });
        Assert.False(prop.PaintWell.IsVisible);
        prop.Close();
    }
}
