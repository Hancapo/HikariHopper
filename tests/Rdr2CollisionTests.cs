using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2's collisions and navmeshes in the 3D viewer, read through RedFury from the installed game.</summary>
public sealed class Rdr2CollisionTests
{
    [Fact]
    public void A_collision_from_the_installed_game_is_drawn_by_material_named_from_the_games_materials()
    {
        string game = TestGames.Rdr2();
        RedFury.Collisions.CollisionMaterialCatalog? materials = Rdr2CollisionMaterials.ForGame(game, Rdr2Keys.Bundled.Crypto).GetAwaiter().GetResult();
        Assert.NotNull(materials);
        Assert.NotEmpty(materials.Materials);
        (string name, byte[] data) = Rdr2ModelTests.Find(".ybn", (_, data) => data.Length > 20_000)
            ?? throw new InvalidOperationException("None of the archives searched holds a collision");
        ModelContent content = ModelContent.Read(name, data, rdr2Materials: materials);
        Assert.Null(content.Surfaces);
        Assert.False(content.Box.IsEmpty);
        Assert.True(content.Mesh.TriangleCount > 0);
        // Each material in the parts panel, its shapes counted, named from materials.meta.
        Assert.NotEmpty(content.Mesh.Legend);
        Assert.All(content.Mesh.Legend, entry => Assert.True(entry.Count > 0));
        Assert.Contains(content.Mesh.Legend, entry => !entry.Name.StartsWith("Material ", StringComparison.Ordinal));
        Assert.All(content.Mesh.Marks, mark => Assert.Equal(CollisionMesh.Section, mark.Section));
    }

    [Fact]
    public void A_navmesh_from_the_installed_game_is_drawn_at_its_map_coordinates()
    {
        (string name, byte[] data) = Rdr2ModelTests.Find(".ynv", (_, data) => data.Length > 4_000)
            ?? throw new InvalidOperationException("None of the archives searched holds a navmesh");
        ModelContent content = ModelContent.Read(name, data);
        Assert.False(content.Box.IsEmpty);
        Assert.True(content.Mesh.TriangleCount > 0);
        // Around its own middle, out in the world.
        Assert.Equal(content.Box.Center, content.Mesh.Origin);
        Assert.Equal("Navigable ground", Assert.Single(content.Mesh.Legend).Name);
    }
}
