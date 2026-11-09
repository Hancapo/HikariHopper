using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>The parts panel's sections: a fragment's body, groups and extras; a drawable dictionary's kinds of drawable.</summary>
public sealed class PartSectionsTests
{
    private static int next;

    private static PartNode Leaf(string name, int depth, PartKind kind = PartKind.Geometry, int triangles = 10) =>
        new($"k{next++}", name, depth, triangles, [next], [], kind);

    private static PartNode Branch(string name, int depth, PartKind kind, params PartNode[] children) =>
        new($"k{next++}", name, depth, children.Sum(child => child.Triangles), [.. children.SelectMany(child => child.Parts)], children, kind);

    [Theory]
    [InlineData("extra_1", 1)]
    [InlineData("EXTRA_12", 12)]
    [InlineData("extra_ten", 10)]
    [InlineData("extra_Two", 2)]
    [InlineData("extra_", null)]
    [InlineData("extra_x", null)]
    [InlineData("door_dside_f", null)]
    public void An_extra_is_numbered_as_the_game_numbers_it(string name, int? number) => Assert.Equal(number, PartVisibility.ExtraNumber(name));

    [Fact]
    public void A_fragment_is_its_body_its_groups_and_its_extras_by_number()
    {
        PartNode body = Branch("adder", 0, PartKind.Drawable,
            Branch("door_dside_f", 1, PartKind.Child, Leaf("vehicle_paint1", 2)),
            Branch("extra_ten", 1, PartKind.Child, Leaf("vehicle_paint1", 2)),
            Branch("extra_1", 1, PartKind.Child, Leaf("vehicle_paint1", 2)));
        PartNode wheels = Branch("Wheels", 0, PartKind.Group,
            Branch("wheel_lf", 1, PartKind.Drawable, Leaf("vehicle_tire", 2)),
            Branch("wheel_rf", 1, PartKind.Drawable, Leaf("vehicle_tire", 2)));
        IReadOnlyList<PartSection> sections = PartVisibility.SectionsOf([body, wheels]);

        Assert.Equal(["Body", "Wheels", "Extras"], sections.Select(section => section.Title));
        Assert.Equal([PartSectionKind.Tree, PartSectionKind.Tree, PartSectionKind.Extras], sections.Select(section => section.Kind));
        // The body without its extras, and counting without them; the extras by number.
        PartNode adder = Assert.Single(sections[0].Nodes);
        Assert.Equal(["door_dside_f"], adder.Children.Select(child => child.Name));
        Assert.Equal(10, adder.Triangles);
        Assert.Equal(body.Key, adder.Key);
        Assert.Equal(["extra_1", "extra_ten"], sections[2].Nodes.Select(node => node.Name));
        // The group's drawables stand at the top of their section, the levels under them with them.
        Assert.Equal(["wheel_lf", "wheel_rf"], sections[1].Nodes.Select(node => node.Name));
        Assert.All(sections[1].Nodes, node => Assert.Equal(0, node.Depth));
        Assert.All(sections[1].Nodes, node => Assert.Equal(1, Assert.Single(node.Children).Depth));
    }

    [Fact]
    public void A_single_drawable_is_one_untitled_section()
    {
        PartNode sign = Branch("sign", 0, PartKind.Drawable, Leaf("default", 1));
        PartSection section = Assert.Single(PartVisibility.SectionsOf([sign]));
        Assert.Equal(("", PartSectionKind.Tree), (section.Title, section.Kind));
        Assert.Same(sign, Assert.Single(section.Nodes));
    }

    [Fact]
    public void A_drawable_dictionarys_drawables_go_by_what_their_names_say_they_are()
    {
        PartNode[] drawables = [.. new[] { "head_000_r", "uppr_000_u", "head_001_r", "p_head_002", "lamp" }.Select(name => Branch(name, 0, PartKind.Drawable, Leaf("ped", 1)))];
        IReadOnlyList<PartSection> sections = PartVisibility.SectionsOf(drawables);
        Assert.Equal(["Head", "Torso", "Head prop", "Drawables"], sections.Select(section => section.Title));
        // Those whose names don't say are a plain section: all of them show.
        Assert.Equal([PartSectionKind.Variants, PartSectionKind.Variants, PartSectionKind.Variants, PartSectionKind.Tree], sections.Select(section => section.Kind));
        Assert.Equal(["head_000_r", "head_001_r"], sections[0].Nodes.Select(node => node.Name));
        Assert.Equal(["lamp"], sections[3].Nodes.Select(node => node.Name));
        Assert.Equal(("head", "p_head", "", "lowr"), (PartVisibility.VariantPrefix("head_000_r"), PartVisibility.VariantPrefix("p_head_002"),
            PartVisibility.VariantPrefix("lamp"), PartVisibility.VariantPrefix("LOWR_015_U")));
        Assert.Equal("feetx", PartVisibility.VariantTitle("feetx"));
    }

    [Fact]
    public void A_peds_sections_go_head_to_toe_then_its_props_each_kinds_variants_by_number()
    {
        // The file's order, as a ped's dictionary has it: by hash, which says nothing.
        string[] names = ["lowr_001_u", "lowr_000_u", "accs_000_u", "p_eyes_000", "hair_000_r", "hair_002_u", "hair_001_u", "uppr_000_r", "p_head_001", "head_000_r", "crate_000"];
        PartNode[] drawables = [.. names.Select(name => Branch(name, 0, PartKind.Drawable, Leaf("ped", 1)))];
        IReadOnlyList<PartSection> sections = PartVisibility.SectionsOf(drawables);
        // Components in the game's order, then props in theirs, then names that aren't a ped's.
        Assert.Equal(["Head", "Hair", "Torso", "Legs", "Accessories", "Head prop", "Eyes prop", "crate"], sections.Select(section => section.Title));
        Assert.Equal(["hair_000_r", "hair_001_u", "hair_002_u"], sections[1].Nodes.Select(node => node.Name));
        Assert.Equal(["lowr_000_u", "lowr_001_u"], sections[3].Nodes.Select(node => node.Name));
    }

    [Fact]
    public void A_peds_drawables_named_only_for_their_dictionary_are_named_by_their_hash()
    {
        Assert.Equal("head_000_r", ModelContent.EntryName(new CommonFury.Hashing.Hash32("head_000_r").Value, "a_m_y_hipster_01.#dd"));
        Assert.Equal("p_head_002", ModelContent.EntryName(new CommonFury.Hashing.Hash32("p_head_002").Value, null));
        Assert.Equal("uppr_015_u", ModelContent.EntryName(new CommonFury.Hashing.Hash32("uppr_015_u").Value, ""));
        // A name of its own wins; an unknown hash shows as one.
        Assert.Equal("lamp", ModelContent.EntryName(new CommonFury.Hashing.Hash32("head_000_r").Value, "lamp.#dr"));
        Assert.Equal("0x00000ABC", ModelContent.EntryName(0xABC, "x.#dd"));
    }
}
