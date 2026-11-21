using CommonFury.Numerics;

namespace HikariHopper.Rendering;

/// <summary>
/// A flat mesh's marks as the parts panel's rows: a collision's materials, or a navmesh's kinds of ground, edges, links
/// and cover points, each under its section with the colour it is drawn in and how many things wear it, most first.
/// Hiding a row leaves its triangles and lines out of the mesh.
/// </summary>
internal sealed class MeshParts(ViewportMesh mesh) : IPartList
{
    private readonly IReadOnlyList<MeshMark> marks = mesh.Marks;

    /// <summary>The mesh whose marks these are.</summary>
    public ViewportMesh Mesh { get; } = mesh;

    /// <summary>Whether the mesh has anything to list.</summary>
    public bool Any => marks.Count > 0;

    public IReadOnlyList<PartSection> Sections() =>
        [.. marks.GroupBy(mark => mark.Section).Select(section => new PartSection(section.Key, PartSectionKind.Tree,
            [.. section.OrderByDescending(mark => mark.Count).ThenBy(mark => mark.Name, StringComparer.OrdinalIgnoreCase).Select(Node)]))];

    private static PartNode Node(MeshMark mark) =>
        new($"mark/{mark.Index}", mark.Name, 0, mark.Count, [mark.Index], [], PartKind.Mark, mark.Colour);

    public bool? State(PartNode node) => node.Parts.Count == 0 ? null
        : node.Parts.All(Mesh.Shown) ? true
        : node.Parts.Any(Mesh.Shown) ? null
        : false;

    public void Show(PartNode node, bool shown)
    {
        foreach (int mark in node.Parts)
            Mesh.Show(mark, shown);
    }

    public void ShowAll() => Mesh.ShowAll();

    public void ShowOnly(IEnumerable<PartNode> nodes)
    {
        HashSet<int> keep = [.. nodes.SelectMany(node => node.Parts)];
        foreach (MeshMark mark in marks)
            Mesh.Show(mark.Index, keep.Contains(mark.Index));
    }

    public (int Shown, int Total) Counts() => (marks.Count(mark => Mesh.Shown(mark.Index)), marks.Count);

    public Aabb3? Bounds(IEnumerable<PartNode> nodes) => Mesh.BoundsOf(nodes.SelectMany(node => node.Parts));
}
