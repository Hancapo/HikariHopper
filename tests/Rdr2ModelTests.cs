using System.Numerics;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2's drawables in the 3D viewer's scene, read through RedFury from the installed game.</summary>
public sealed class Rdr2ModelTests
{
    // The archives searched, and one level into the archives nested in them, where RDR2 keeps its props.
    private static readonly string[] Archives = ["levels_0.rpf", "levels_1.rpf", "content_0.rpf", "update_1.rpf"];

    /// <summary>The first file of an extension the archives hold under a size, with its bytes; null when none does.</summary>
    internal static (string Name, byte[] Data)? Find(string extension, Func<string, byte[], bool>? wanted = null, long below = 6_000_000)
    {
        string game = TestGames.Rdr2();
        foreach (string archive in Archives)
        {
            string path = Path.Combine(game, archive);
            if (!File.Exists(path))
                continue;
            ArchiveNode root = ArchiveNode.OpenRoot(path, Rdr2Keys.Bundled);
            try
            {
                (string, byte[])? In(ArchiveNode node)
                {
                    foreach (ArchiveEntry entry in node.EnumerateEntries())
                        if (entry.IsResource && entry.Size < below && entry.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            byte[] data = entry.Read();
                            if (wanted?.Invoke(entry.Name, data) != false)
                                return (entry.Name, data);
                        }
                    return null;
                }
                if (In(root) is { } direct)
                    return direct;
                foreach (ArchiveEntry nested in root.EnumerateEntries().Where(entry => entry.IsBinaryFile
                    && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (In(root.OpenNested(nested)) is { } found)
                            return found;
                    }
                    catch (InvalidDataException)
                    {
                    }
                }
            }
            finally
            {
                root.Close();
            }
        }
        return null;
    }

    private static void AssertDrawn(DrawableScene scene)
    {
        Assert.NotEmpty(scene.Parts);
        Assert.Contains(scene.Parts, part => part.Primary);
        Assert.False(scene.Box.IsEmpty);
        Assert.All(scene.Vertices, vertex => Assert.True(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y)
            && float.IsFinite(vertex.Position.Z) && float.IsFinite(vertex.Normal.X)));
        Assert.All(scene.Parts, part =>
        {
            Assert.InRange(part.Material, 0, scene.Materials.Count - 1);
            Assert.True(part.FirstIndex + part.IndexCount <= scene.Indices.Length);
        });
        // Every index is inside its part's vertices.
        foreach (SurfacePart part in scene.Parts)
            for (uint i = part.FirstIndex; i < part.FirstIndex + part.IndexCount; i++)
                Assert.True(part.BaseVertex + scene.Indices[i] < scene.Vertices.Length);
        // Around its own middle, as the viewer frames it.
        Assert.True(Vector3.Distance(scene.Origin, scene.Box.Center) < 1e-3f);
    }

    [Fact]
    public void A_drawable_from_the_installed_game_is_drawn_with_the_textures_it_carries()
    {
        // One that carries its textures, so they're found without the game's dictionaries.
        (string name, byte[] data) = Find(".ydr", (_, data) => RedFury.Ydr.Ydr.Read(data).Drawable.EmbeddedTextures is { Textures.Count: > 0 })
            ?? throw new InvalidOperationException("None of the archives searched holds a drawable with textures of its own");
        ModelContent content = ModelContent.Read(name, data);
        DrawableScene scene = content.Surfaces!;
        AssertDrawn(scene);
        Assert.Contains(scene.Materials, material => material.Diffuse is not null);
        // What isn't drawn as RDR2 draws it yet is said.
        Assert.Contains(scene.Diagnostics, line => line.StartsWith("Drawn as a plain textured surface", StringComparison.Ordinal));
        // Nothing of GTA V's to edit or save.
        Assert.Empty(scene.Sources);
        Assert.Null(scene.Writer);
    }

    [Fact]
    public void A_drawable_dictionary_from_the_installed_game_is_drawn_one_model_a_drawable()
    {
        (string name, byte[] data) = Find(".ydd", (_, data) => RedFury.Ydd.Ydd.Read(data).Drawables.Count > 1)
            ?? throw new InvalidOperationException("None of the archives searched holds a drawable dictionary");
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        AssertDrawn(scene);
        Assert.Equal(RedFury.Ydd.Ydd.Read(data).Drawables.Count, scene.Models.Count);
    }

    [Fact]
    public void A_fragment_from_the_installed_game_is_drawn_with_its_main_drawable()
    {
        (string name, byte[] data) = Find(".yft", (_, data) => RedFury.Yft.Yft.Read(data).MainDrawable is not null)
            ?? throw new InvalidOperationException("None of the archives searched holds a fragment");
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        AssertDrawn(scene);
        Assert.Equal(Path.GetFileNameWithoutExtension(name), scene.Models[0].Name);
    }

    [Fact]
    public void Its_textures_are_found_in_the_dictionary_named_for_it()
    {
        // A drawable that carries none of its own: given the dictionary that holds them, they're found in it.
        (string name, byte[] data) = Find(".ydr", (_, data) => RedFury.Ydr.Ydr.Read(data).Drawable.EmbeddedTextures is null)
            ?? throw new InvalidOperationException("None of the archives searched holds a drawable without textures");
        DrawableScene bare = ModelContent.Read(name, data).Surfaces!;
        var drawable = RedFury.Ydr.Ydr.Read(data).Drawable;
        var references = drawable.TextureReferences.Where(reference => reference.Name.Length > 0).Take(3).ToList();
        if (references.Count == 0)
            return;
        var images = references.Select(reference => new RedFury.Textures.TextureImage(reference.Name, 4, 4,
            RedFury.Textures.TextureFormat.Bc1Unorm, 1, new byte[8]));
        byte[] dictionary = RedFury.Ytd.Ytd.Create(images).Write();
        DrawableScene textured = ModelContent.Read(name, data, [dictionary]).Surfaces!;
        int Missing(DrawableScene scene) => scene.Materials.Sum(material => material.MissingTextures.Count);
        Assert.True(Missing(textured) < Missing(bare));
    }
}
