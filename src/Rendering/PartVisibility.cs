using System.Numerics;
using CommonFury.Numerics;

namespace HikariHopper.Rendering;

/// <summary>A row of the parts panel: a drawable (or a group of them), one of its models, or one of their geometries.</summary>
/// <param name="Key">What the row is, the same at every level of detail where it exists: parts hidden by name stay hidden.</param>
/// <param name="Triangles">The triangles under it, at the level shown.</param>
/// <param name="Parts">The scene parts under it (<see cref="DrawableScene.Parts"/>), at the level shown.</param>
/// <param name="Kind">What the row is: a group of drawables, a drawable, one of its bone-bound models, or a geometry.</param>
/// <param name="Colour">For a mark of a flat mesh (a collision's material), the colour it is drawn in; the row wears it.</param>
internal sealed record PartNode(string Key, string Name, int Depth, int Triangles, IReadOnlyList<int> Parts, IReadOnlyList<PartNode> Children,
    PartKind Kind = PartKind.Drawable, uint? Colour = null);

/// <summary>What the parts panel lists, and shows or hides: a drawable's parts, or a flat mesh's marks.</summary>
internal interface IPartList
{
    /// <summary>The panel's sections, each with its rows.</summary>
    IReadOnlyList<PartSection> Sections();

    /// <summary>Whether what is under a row is shown: all of it, none of it, or (null) part of it.</summary>
    bool? State(PartNode node);

    /// <summary>Shows or hides everything under a row.</summary>
    void Show(PartNode node, bool shown);

    /// <summary>Shows everything.</summary>
    void ShowAll();

    /// <summary>Shows what is under these rows only.</summary>
    void ShowOnly(IEnumerable<PartNode> nodes);

    /// <summary>How many of what the panel counts are shown, of how many there are.</summary>
    (int Shown, int Total) Counts();

    /// <summary>The world box around what is under these rows; none when there is nothing.</summary>
    Aabb3? Bounds(IEnumerable<PartNode> nodes);
}

/// <summary>What a section of the parts panel holds.</summary>
internal enum PartSectionKind
{
    /// <summary>Rows in a tree: a drawable's parts, or a group's drawables.</summary>
    Tree,
    /// <summary>A vehicle's extras, each a toggle numbered as the game numbers it.</summary>
    Extras,
    /// <summary>A drawable dictionary's drawables of one kind (a ped's heads, say): variants of each other.</summary>
    Variants,
    /// <summary>A fragment's intact and damaged states, each with its parts: one of them shows.</summary>
    States,
}

/// <summary>A section of the parts panel.</summary>
/// <param name="Title">Its heading; none when it's the only one, a single drawable's.</param>
/// <param name="Nodes">Its rows; for extras, one per extra, by number.</param>
internal sealed record PartSection(string Title, PartSectionKind Kind, IReadOnlyList<PartNode> Nodes);

/// <summary>What a parts panel row is.</summary>
internal enum PartKind
{
    /// <summary>Drawables gathered under one name: a vehicle's wheels.</summary>
    Group,
    /// <summary>A drawable.</summary>
    Drawable,
    /// <summary>One of a drawable's models, bound to a bone: a fragment's door, say.</summary>
    Child,
    /// <summary>A geometry, named by its shader.</summary>
    Geometry,
    /// <summary>What a flat mesh's triangles are marked as, by colour: a collision's material, a navmesh's kind of ground.</summary>
    Mark,
    /// <summary>A fragment intact or damaged, with the parts drawn only in that state.</summary>
    State,
}

/// <summary>
/// Which of a drawable scene's parts are shown: those at the level of detail picked (a drawable without it shows the
/// closest it has, coarser first), less any the parts panel hides. Hidden parts are kept by name, so they stay hidden
/// at the other levels that have them.
/// </summary>
internal sealed class PartVisibility : IPartList
{
    private readonly DrawableScene scene;
    private readonly string[] keys;
    private readonly HashSet<string> hidden = [];

    public PartVisibility(DrawableScene scene)
    {
        this.scene = scene;
        // A geometry's key: its model, its drawable model, and which of that model's geometries it is.
        keys = new string[scene.Parts.Count];
        var counts = new Dictionary<(int, int, string), int>();
        for (int i = 0; i < keys.Length; i++)
        {
            SurfacePart part = scene.Parts[i];
            int ordinal = counts.GetValueOrDefault((part.Model, part.Lod, part.Group));
            counts[(part.Model, part.Lod, part.Group)] = ordinal + 1;
            keys[i] = $"{part.Model}/{part.Group}/{ordinal}";
        }
        Levels = [.. scene.Parts.Select(part => part.Lod).Distinct().Order()];
        Lod = Levels.Count > 0 ? Levels[0] : 0;
    }

    /// <summary>The levels of detail any drawable has: 0 high, 1 medium, 2 low, 3 very low.</summary>
    public IReadOnlyList<int> Levels { get; }

    /// <summary>The level of detail picked.</summary>
    public int Lod { get; set; }

    /// <summary>
    /// Takes over what <paramref name="before"/> shows (its level, its damaged state, the parts it hides) for the same
    /// drawables read again: parts are kept by name, so they match.
    /// </summary>
    public void KeepFrom(PartVisibility before)
    {
        Lod = Levels.Contains(before.Lod) ? before.Lod : Lod;
        Damaged = before.Damaged;
        hidden.Clear();
        hidden.UnionWith(before.hidden);
    }

    /// <summary>Whether a fragment shows damaged: its damaged drawables in place of their intact ones.</summary>
    public bool Damaged { get; set; }

    /// <summary>Whether a model is drawn in the state shown: always, or only intact or damaged.</summary>
    public bool Active(int model) => (model < scene.Variants.Count ? scene.Variants[model] : ModelVariant.Always) switch
    {
        ModelVariant.Intact => !Damaged,
        ModelVariant.Damaged => Damaged,
        _ => true,
    };

    /// <summary>A level of detail as its segment names it: High, Med, Low, VLow.</summary>
    public static string ShortLevelName(int lod) => lod switch { 0 => "High", 1 => "Med", 2 => "Low", _ => "VLow" };

    /// <summary>A level of detail's name.</summary>
    public static string LevelName(int lod) => lod switch { 0 => "High", 1 => "Medium", 2 => "Low", _ => "Very low" };

    /// <summary>The level a model shows for the level picked: that one, else the closest it has, coarser first.</summary>
    public int LevelOf(int model)
    {
        var own = scene.Parts.Where(part => part.Model == model).Select(part => part.Lod).Distinct().ToList();
        if (own.Contains(Lod) || own.Count == 0)
            return Lod;
        return own.Where(level => level > Lod).DefaultIfEmpty(own.Max()).Min();
    }

    /// <summary>Whether a scene part is drawn.</summary>
    public bool Shown(int part) => scene.Parts[part].Lod == LevelOf(scene.Parts[part].Model) && Active(scene.Parts[part].Model) && !hidden.Contains(keys[part]);

    /// <summary>A flag per scene part, for the renderer.</summary>
    public bool[] Mask()
    {
        int[] levels = [.. Enumerable.Range(0, scene.Models.Count).Select(LevelOf)];
        return [.. scene.Parts.Select((part, i) => part.Lod == (part.Model < levels.Length ? levels[part.Model] : Lod) && Active(part.Model) && !hidden.Contains(keys[i]))];
    }

    /// <summary>What is drawn: its triangles, vertices and distinct edges.</summary>
    public (int Triangles, int Vertices, int Edges) Stats()
    {
        bool[] mask = Mask();
        IEnumerable<SurfacePart> shown = scene.Parts.Where((_, i) => mask[i]);
        return (shown.Sum(part => part.TriangleCount), shown.Sum(part => part.VertexCount), shown.Sum(part => part.EdgeCount));
    }

    /// <summary>How many of the level's geometries are drawn, of how many there are.</summary>
    public (int Shown, int Total) Counts()
    {
        int[] at = [.. AtLevel()];
        return (at.Count(i => !hidden.Contains(keys[i])), at.Length);
    }

    /// <summary>The intact state's row of a fragment that has a damaged one.</summary>
    public const string IntactKey = "state/intact";

    /// <summary>The damaged state's row.</summary>
    public const string DamagedKey = "state/damaged";

    /// <summary>
    /// The parts panel's rows at the level picked: each drawable, or its group (a vehicle's wheels), then its models
    /// when it has more than one, then their geometries by shader. A fragment with a damaged state has a row for each
    /// state first, Intact and Damaged, with what is drawn only in it under it: its main drawable's parts, and the
    /// children that break.
    /// </summary>
    public IReadOnlyList<PartNode> Tree()
    {
        int[] at = [.. AtLevel(everyState: true)];
        PartNode Geometry(int part, int depth) =>
            new(keys[part], scene.Materials[scene.Parts[part].Material].Shader, depth, scene.Parts[part].TriangleCount, [part], [], PartKind.Geometry);
        PartNode Drawable(int model, int depth)
        {
            int[] own = [.. at.Where(i => scene.Parts[i].Model == model)];
            var groups = own.GroupBy(i => scene.Parts[i].Group).ToList();
            IReadOnlyList<PartNode> children = groups.Count == 1 && groups[0].Key == ""
                ? [.. own.Select(i => Geometry(i, depth + 1))]
                : [.. groups.Select(g => Node($"{model}/{g.Key}", g.Key == "" ? "unbound" : g.Key, depth + 1, [.. g.Select(i => Geometry(i, depth + 2))], PartKind.Child))];
            return Node($"{model}", scene.Models[model].Name, depth, children, PartKind.Drawable);
        }
        var rows = new List<PartNode>();
        foreach (var group in Enumerable.Range(0, scene.Models.Count).Where(m => at.Any(i => scene.Parts[i].Model == m)).GroupBy(m => scene.Models[m].Group))
        {
            if (group.Key is null)
                rows.AddRange(group.Select(m => Drawable(m, 0)));
            else
                rows.Add(Node($"group/{group.Key}", group.Key, 0, [.. group.Select(m => Drawable(m, 1))], PartKind.Group));
        }
        return scene.HasDamaged ? WithStates(rows) : rows;
    }

    // The rows with what is drawn only intact or only damaged taken out, into a row for each state.
    private IReadOnlyList<PartNode> WithStates(List<PartNode> rows)
    {
        ModelVariant VariantOf(PartNode node) =>
            node.Kind == PartKind.Drawable && int.TryParse(node.Key, out int model) && model < scene.Variants.Count ? scene.Variants[model] : ModelVariant.Always;
        var intact = new List<PartNode>();
        var damaged = new List<PartNode>();
        var kept = new List<PartNode>();
        foreach (PartNode row in rows)
        {
            if (row.Kind == PartKind.Group)
            {
                var stays = new List<PartNode>();
                foreach (PartNode child in row.Children)
                    (VariantOf(child) switch { ModelVariant.Intact => intact, ModelVariant.Damaged => damaged, _ => stays }).Add(child);
                if (stays.Count > 0)
                    kept.Add(stays.Count == row.Children.Count ? row : Node(row.Key, row.Name, row.Depth, stays, PartKind.Group));
                continue;
            }
            switch (VariantOf(row))
            {
                case ModelVariant.Intact:
                    intact.Add(Shifted(row, 1));
                    break;
                case ModelVariant.Damaged:
                    damaged.Add(Shifted(row, 1));
                    break;
                default:
                    kept.Add(row);
                    break;
            }
        }
        // A state that is one drawable lists its parts under it, without a row for the drawable.
        PartNode State(string key, string name, List<PartNode> nodes) => Node(key, name, 0,
            nodes is [{ Kind: PartKind.Drawable, Children.Count: > 0 } only] ? [.. only.Children.Select(child => Shifted(child, -1))] : nodes, PartKind.State);
        return [State(IntactKey, "Intact", intact), State(DamagedKey, "Damaged", damaged), .. kept];
    }

    // A row and everything under it moved down (or up) the tree.
    private static PartNode Shifted(PartNode node, int by) =>
        node with { Depth = Math.Max(0, node.Depth + by), Children = [.. node.Children.Select(child => Shifted(child, by))] };

    /// <summary>
    /// The parts panel's sections at the level picked. A drawable dictionary's drawables go by what their names say they
    /// are (a ped's head_000_r and head_001_r are both Head), as variants of each other. A fragment's are its body, then
    /// each group of drawables (its wheels, its children), then its extras, taken out of where they're bound; a single
    /// drawable is one section, untitled.
    /// </summary>
    public IReadOnlyList<PartSection> Sections() => SectionsOf(Tree(), node => node.Key != (Damaged ? IntactKey : DamagedKey));

    /// <summary>
    /// The sections of these rows, as <see cref="Sections"/> makes them. A fragment's states come first, as a section of
    /// their own; the extras listed are those of the state <paramref name="shown"/> says is drawn, so each is there once.
    /// </summary>
    internal static IReadOnlyList<PartSection> SectionsOf(IReadOnlyList<PartNode> tree, Func<PartNode, bool>? shown = null)
    {
        PartNode[] states = [.. tree.Where(node => node.Kind == PartKind.State)];
        if (states.Length > 0)
        {
            var extrasOf = new List<PartNode>();
            var listed = new List<PartNode>();
            foreach (PartNode state in states)
            {
                // A state is taken as one drawable, its extras out of it.
                IReadOnlyList<PartSection> own = SectionsOf([state with { Kind = PartKind.Drawable }]);
                listed.Add(own.First(section => section.Kind == PartSectionKind.Tree).Nodes.Single() with { Kind = PartKind.State });
                if (shown?.Invoke(state) != false)
                    extrasOf.AddRange(own.Where(section => section.Kind == PartSectionKind.Extras).SelectMany(section => section.Nodes));
            }
            IReadOnlyList<PartSection> rest = SectionsOf([.. tree.Where(node => node.Kind != PartKind.State)]);
            var all = new List<PartSection> { new("State", PartSectionKind.States, listed) };
            foreach (PartSection section in rest)
                all.Add(section.Kind == PartSectionKind.Tree && section.Title == "" ? section with { Title = "Body" } : section);
            if (extrasOf.Count > 0)
            {
                PartNode[] merged = [.. (all.FirstOrDefault(section => section.Kind == PartSectionKind.Extras)?.Nodes ?? []).Concat(extrasOf)
                    .OrderBy(extra => ExtraNumber(extra.Name)).ThenBy(extra => extra.Name, StringComparer.Ordinal)];
                all.RemoveAll(section => section.Kind == PartSectionKind.Extras);
                all.Add(new PartSection("Extras", PartSectionKind.Extras, merged));
            }
            return all;
        }
        if (tree.Count > 1 && tree.All(node => node.Kind == PartKind.Drawable))
            // Drawables whose names don't say what they are aren't variants of each other: all of them show.
            // A ped's are in the order of its body, head first, then its props, each kind's variants by their number;
            // other names keep the file's order, after them.
            return [.. tree.GroupBy(node => VariantPrefix(node.Name)).OrderBy(group => VariantOrder(group.Key)).Select(group =>
                new PartSection(VariantTitle(group.Key), group.Key.Length > 0 ? PartSectionKind.Variants : PartSectionKind.Tree,
                    group.Key.Length > 0 ? [.. group.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)] : [.. group]))];
        var extras = new List<PartNode>();
        PartNode WithoutExtras(PartNode node)
        {
            if (node.Children.Count == 0)
                return node;
            var kept = new List<PartNode>();
            foreach (PartNode child in node.Children)
                if (ExtraNumber(child.Name) is not null)
                    extras.Add(child);
                else
                    kept.Add(WithoutExtras(child));
            return kept.Count == node.Children.Count && kept.SequenceEqual(node.Children)
                ? node
                : node with { Children = kept, Parts = [.. kept.SelectMany(child => child.Parts)], Triangles = kept.Sum(child => child.Triangles) };
        }
        var body = new List<PartNode>();
        var groups = new List<PartSection>();
        foreach (PartNode node in tree)
        {
            if (node.Kind != PartKind.Group)
            {
                body.Add(WithoutExtras(node));
                continue;
            }
            var rows = new List<PartNode>();
            foreach (PartNode child in node.Children)
                if (ExtraNumber(child.Name) is not null)
                    extras.Add(child);
                else
                    rows.Add(Lifted(WithoutExtras(child)));
            if (rows.Count > 0)
                groups.Add(new PartSection(node.Name, PartSectionKind.Tree, rows));
        }
        var sections = new List<PartSection>();
        bool alone = groups.Count == 0 && extras.Count == 0;
        if (body.Count > 0)
            sections.Add(new PartSection(alone ? "" : "Body", PartSectionKind.Tree, body));
        sections.AddRange(groups);
        if (extras.Count > 0)
            sections.Add(new PartSection("Extras", PartSectionKind.Extras, [.. extras.OrderBy(extra => ExtraNumber(extra.Name)).ThenBy(extra => extra.Name, StringComparer.Ordinal)]));
        return sections;
    }

    private static readonly string[] NumberWords = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>A vehicle extra's number, as the game numbers it: extra_1 is 1, extra_ten 10; null for anything else.</summary>
    public static int? ExtraNumber(string name)
    {
        if (!name.StartsWith("extra_", StringComparison.OrdinalIgnoreCase))
            return null;
        string number = name[6..];
        if (int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value))
            return value;
        int word = Array.FindIndex(NumberWords, each => each.Equals(number, StringComparison.OrdinalIgnoreCase));
        return word >= 0 ? word : null;
    }

    /// <summary>What a drawable dictionary's drawable's name says it is: head for head_000_r, p_head for p_head_002; "" when it doesn't say.</summary>
    public static string VariantPrefix(string name)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)_\d{3}(?:_.*)?$");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
    }

    private static readonly string[] Components = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly string[] Props = ["head", "eyes", "ears", "mouth", "lhand", "rhand", "lwrist", "rwrist", "hip", "lfoot", "rfoot"];

    /// <summary>
    /// Where a variant section goes: a ped's components in the game's order (head, mask, hair, torso, legs…), then its
    /// props in theirs, then everything else, which shares one place so it keeps the file's order: any other name, and
    /// the drawables whose names say nothing.
    /// </summary>
    internal static int VariantOrder(string prefix)
    {
        bool prop = prefix.StartsWith("p_", StringComparison.Ordinal);
        int index = Array.IndexOf(prop ? Props : Components, prop ? prefix[2..] : prefix);
        return index < 0 ? int.MaxValue : prop ? Components.Length + index : index;
    }

    /// <summary>A variant section's heading: a ped component's name for its prefix, the prefix itself otherwise.</summary>
    public static string VariantTitle(string prefix)
    {
        bool prop = prefix.StartsWith("p_", StringComparison.Ordinal);
        string component = prop ? prefix[2..] : prefix;
        string? name = component switch
        {
            "head" => "Head",
            "berd" => "Mask",
            "hair" => "Hair",
            "uppr" => "Torso",
            "lowr" => "Legs",
            "hand" => "Hands",
            "feet" => "Feet",
            "teef" => "Neck",
            "accs" => "Accessories",
            "task" => "Armour",
            "decl" => "Decals",
            "jbib" => "Tops",
            "eyes" => "Eyes",
            "ears" => "Ears",
            "mouth" => "Mouth",
            "lhand" => "Left hand",
            "rhand" => "Right hand",
            "lwrist" => "Left wrist",
            "rwrist" => "Right wrist",
            "hip" => "Hip",
            "lfoot" => "Left foot",
            "rfoot" => "Right foot",
            _ => null,
        };
        if (prefix.Length == 0)
            return "Drawables";
        if (name is null)
            return prefix;
        return prop ? $"{name} prop" : name;
    }

    // A group's row brought up a level, with everything under it, now that its section stands for the group.
    private static PartNode Lifted(PartNode node) =>
        node with { Depth = Math.Max(0, node.Depth - 1), Children = [.. node.Children.Select(Lifted)] };

    /// <summary>Whether a row's parts are drawn: all of them, none, or some (null).</summary>
    public bool? State(PartNode node)
    {
        // What the other state draws isn't drawn, whatever the panel hides.
        int shown = node.Parts.Count(i => Active(scene.Parts[i].Model) && !hidden.Contains(keys[i]));
        return shown == node.Parts.Count ? true : shown == 0 ? false : null;
    }

    /// <summary>Shows every part, at every level.</summary>
    public void ShowAll() => hidden.Clear();

    /// <summary>Shows the parts under these rows and hides the rest at the level shown.</summary>
    public void ShowOnly(IEnumerable<PartNode> nodes)
    {
        nodes = [.. nodes];
        // Only a state's parts, or parts of it: that state, and nothing else of it.
        ModelVariant[] states = [.. nodes.SelectMany(node => node.Parts).Select(part => scene.Parts[part].Model)
            .Select(model => model < scene.Variants.Count ? scene.Variants[model] : ModelVariant.Always).Where(variant => variant != ModelVariant.Always).Distinct()];
        if (nodes.FirstOrDefault(node => node.Kind == PartKind.State) is { } state)
            Damaged = state.Key == DamagedKey;
        else if (states is [var only])
            Damaged = only == ModelVariant.Damaged;
        var keep = new HashSet<int>(nodes.SelectMany(node => node.Parts));
        foreach (int part in AtLevel())
            if (keep.Contains(part))
                hidden.Remove(keys[part]);
            else
                hidden.Add(keys[part]);
    }

    /// <summary>The box around the parts under these rows, in the world; none when they have no triangles.</summary>
    public Aabb3? Bounds(IEnumerable<PartNode> nodes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (int index in nodes.SelectMany(node => node.Parts).Distinct())
        {
            SurfacePart part = scene.Parts[index];
            for (uint i = part.FirstIndex; i < part.FirstIndex + part.IndexCount && i < scene.Indices.Length; i++)
            {
                long vertex = part.BaseVertex + scene.Indices[i];
                if (vertex < 0 || vertex >= scene.Vertices.Length)
                    continue;
                Vector3 position = scene.Vertices[vertex].Position;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
                any = true;
            }
        }
        return any ? new Aabb3(min + scene.Origin, max + scene.Origin) : null;
    }

    /// <summary>Shows or hides every part under a row. A state's row shows that state, or the other one.</summary>
    public void Show(PartNode node, bool shown)
    {
        if (node.Kind == PartKind.State)
        {
            Damaged = (node.Key == DamagedKey) == shown;
            return;
        }
        foreach (int part in node.Parts)
            if (shown)
                hidden.Remove(keys[part]);
            else
                hidden.Add(keys[part]);
    }

    // The parts at the level shown, of the state shown or of both.
    private IEnumerable<int> AtLevel(bool everyState = false)
    {
        int[] levels = [.. Enumerable.Range(0, scene.Models.Count).Select(LevelOf)];
        return Enumerable.Range(0, scene.Parts.Count).Where(i => scene.Parts[i].Model < levels.Length && scene.Parts[i].Lod == levels[scene.Parts[i].Model]
            && (everyState || Active(scene.Parts[i].Model)));
    }

    private static PartNode Node(string key, string name, int depth, IReadOnlyList<PartNode> children, PartKind kind) =>
        new(key, name, depth, children.Sum(child => child.Triangles), [.. children.SelectMany(child => child.Parts)], children, kind);
}
