namespace HikariHopper.Rendering;

/// <summary>A row of the parts panel: a drawable (or a group of them), one of its models, or one of their geometries.</summary>
/// <param name="Key">What the row is, the same at every level of detail where it exists: parts hidden by name stay hidden.</param>
/// <param name="Triangles">The triangles under it, at the level shown.</param>
/// <param name="Parts">The scene parts under it (<see cref="DrawableScene.Parts"/>), at the level shown.</param>
internal sealed record PartNode(string Key, string Name, int Depth, int Triangles, IReadOnlyList<int> Parts, IReadOnlyList<PartNode> Children);

/// <summary>
/// Which of a drawable scene's parts are shown: those at the level of detail picked (a drawable without it shows the
/// closest it has, coarser first), less any the parts panel hides. Hidden parts are kept by name, so they stay hidden
/// at the other levels that have them.
/// </summary>
internal sealed class PartVisibility
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

    /// <summary>
    /// The parts panel's rows at the level picked: each drawable, or its group (a vehicle's wheels), then its models
    /// when it has more than one, then their geometries by shader.
    /// </summary>
    public IReadOnlyList<PartNode> Tree()
    {
        int[] at = [.. AtLevel()];
        PartNode Geometry(int part, int depth) =>
            new(keys[part], scene.Materials[scene.Parts[part].Material].Shader, depth, scene.Parts[part].TriangleCount, [part], []);
        PartNode Drawable(int model, int depth)
        {
            int[] own = [.. at.Where(i => scene.Parts[i].Model == model)];
            var groups = own.GroupBy(i => scene.Parts[i].Group).ToList();
            IReadOnlyList<PartNode> children = groups.Count == 1 && groups[0].Key == ""
                ? [.. own.Select(i => Geometry(i, depth + 1))]
                : [.. groups.Select(g => Node($"{model}/{g.Key}", g.Key == "" ? "unbound" : g.Key, depth + 1, [.. g.Select(i => Geometry(i, depth + 2))]))];
            return Node($"{model}", scene.Models[model].Name, depth, children);
        }
        var rows = new List<PartNode>();
        foreach (var group in Enumerable.Range(0, scene.Models.Count).Where(m => at.Any(i => scene.Parts[i].Model == m)).GroupBy(m => scene.Models[m].Group))
        {
            if (group.Key is null)
                rows.AddRange(group.Select(m => Drawable(m, 0)));
            else
                rows.Add(Node($"group/{group.Key}", group.Key, 0, [.. group.Select(m => Drawable(m, 1))]));
        }
        return rows;
    }

    /// <summary>Whether a row's parts are drawn: all of them, none, or some (null).</summary>
    public bool? State(PartNode node)
    {
        int shown = node.Parts.Count(i => !hidden.Contains(keys[i]));
        return shown == node.Parts.Count ? true : shown == 0 ? false : null;
    }

    /// <summary>Shows or hides every part under a row.</summary>
    public void Show(PartNode node, bool shown)
    {
        foreach (int part in node.Parts)
            if (shown)
                hidden.Remove(keys[part]);
            else
                hidden.Add(keys[part]);
    }

    private IEnumerable<int> AtLevel()
    {
        int[] levels = [.. Enumerable.Range(0, scene.Models.Count).Select(LevelOf)];
        return Enumerable.Range(0, scene.Parts.Count).Where(i => scene.Parts[i].Model < levels.Length && scene.Parts[i].Lod == levels[scene.Parts[i].Model]
            && Active(scene.Parts[i].Model));
    }

    private static PartNode Node(string key, string name, int depth, IReadOnlyList<PartNode> children) =>
        new(key, name, depth, children.Sum(child => child.Triangles), [.. children.SelectMany(child => child.Parts)], children);
}
