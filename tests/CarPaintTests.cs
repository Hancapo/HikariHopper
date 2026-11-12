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
            ExplorerStateRenders.Pump(10);
        return viewer;
    }

    [AvaloniaFact]
    public void A_vehicle_from_a_game_opens_in_its_first_combination_and_its_paint_is_picked_in_the_footer()
    {
        Views.ModelViewer viewer = Open(new ModelFile("car.ydr", Car()) { GamePaints = Task.FromResult<CarPaints?>(Paints) });
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        ExplorerStateRenders.Pump(50);
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

        // A slot's own colour: the picker opens in the finish of the colour it has, and a colour picked shows the slots again.
        panel.ShowPicker(0);
        Assert.Equal(0, panel.PickingSlot);
        panel.PickColour(0, 0);
        Assert.Null(panel.PickingSlot);
        Assert.Equal([black.X, black.Y, black.Z], Drawn());
        Assert.Null(panel.Combination);

        viewer.TogglePaint();
        ExplorerStateRenders.Pump(50);
        Assert.Null(viewer.PaintPanel);
        viewer.Close();
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
