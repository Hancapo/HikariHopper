using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>Red Dead Redemption 2's fragments in the 3D viewer: their parts by physics group, and their damaged state.</summary>
public sealed class Rdr2FragmentTests
{
    // Where RDR2 keeps many of its breakable props.
    private static readonly string[] Archives = ["levels_3.rpf", "levels_2.rpf", "levels_4.rpf"];

    private static (string Name, byte[] Data, List<Rdr2Scene.Placed> Placed) FindFragment(Func<List<Rdr2Scene.Placed>, bool> wanted)
    {
        List<Rdr2Scene.Placed> placed = [];
        var found = Rdr2ModelTests.Find(".yft", (name, data) =>
        {
            try
            {
                placed = Rdr2Scene.Fragment(RedFury.Yft.Yft.Read(data), Path.GetFileNameWithoutExtension(name));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
            {
                return false;
            }
            return wanted(placed);
        }, 12_000_000, Archives) ?? throw new InvalidOperationException("None of the archives searched holds such a fragment");
        return (found.Name, found.Data, placed);
    }

    [Fact]
    public void A_fragment_damaged_in_part_swaps_only_the_groups_its_damaged_pieces_belong_to()
    {
        (string name, byte[] data, var placed) = FindFragment(placed => placed.Any(model => model.Except is { Count: > 0 }));
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        IEnumerable<string> GroupsOf(ModelVariant variant) =>
            scene.Parts.Where(part => scene.Variants[part.Model] == variant).Select(part => part.Group).Distinct();
        string[] replaced = [.. placed.Single(model => model.Except is not null).Except!];
        // Intact, the replaced groups are drawn from the main drawable; damaged, from the damaged one; the rest always.
        Assert.Equal(replaced.Order(), GroupsOf(ModelVariant.Intact).Order());
        Assert.All(GroupsOf(ModelVariant.Damaged), group => Assert.Contains(group, replaced));
        Assert.NotEmpty(GroupsOf(ModelVariant.Always));
        Assert.DoesNotContain(GroupsOf(ModelVariant.Always), group => replaced.Contains(group));
        Assert.True(scene.HasDamaged);
    }

    [Fact]
    public void A_skinned_fragments_triangles_are_split_into_its_physics_groups()
    {
        (string name, byte[] data, var placed) = FindFragment(placed => placed.Count > 0 && placed[0].BoneGroups is { } groups
            && groups.Values.Distinct().Count() > 2 && placed[0].Drawable.Models().Any(model => model.Binding.IsSkinned));
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        IReadOnlyDictionary<int, string> groups = placed[0].BoneGroups!;
        string[] main = [.. scene.Parts.Where(part => part.Model == 0 && part.Primary).Select(part => part.Group).Distinct()];
        Assert.True(main.Length > 2, $"{name}: {string.Join(", ", main)}");
        Assert.All(main, group => Assert.Contains(group, groups.Values));
        // Every triangle is still drawn, once.
        Assert.Equal(placed[0].Drawable.Geometries(placed[0].Drawable.PrimaryLod).Sum(geometry => geometry.Indices.IndexCount / 3),
            scene.Parts.Where(part => part.Model == 0 && part.Primary).Sum(part => part.TriangleCount));
    }
}
