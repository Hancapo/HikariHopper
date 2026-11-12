using System.Globalization;
using System.Text;
using System.Text.Json;
using FiveFury.Drawables;

namespace HikariHopper.Rendering;

/// <summary>
/// One of a shader entry's numeric values, as the shader values card shows it: its parameter's name, what it is called
/// and grouped under, and how many numbers it holds (a colour three, a scalar one; an array four per row).
/// </summary>
/// <param name="Name">The parameter's name in the entry's edition, what <see cref="DrawableMaterial.SetParameter"/> takes.</param>
/// <param name="Order">Where it comes in the card: by group, then within it.</param>
/// <param name="Range">What its slider's fill spans, when values.json gives it; else 0 to 1, or to twice the file's value past that.</param>
/// <param name="Colour">Whether its first three numbers are a colour, shown with a swatch.</param>
/// <param name="Legacy">Its Legacy name, what values.json knows it by.</param>
internal sealed record ShaderValue(string Name, string Label, string Group, int Components, int Order,
    (float Min, float Max)? Range = null, bool Colour = false, string Legacy = "");

/// <summary>What a save wrote: the changed values as they were when it started, and the entries geometries had been given of their own.</summary>
internal sealed record ShaderSnapshot(IReadOnlyDictionary<DrawableMaterial, Dictionary<string, float[]>> Values, IReadOnlyList<DrawableMaterial> Splits);

/// <summary>
/// The shader values edited in a 3D viewer: each change goes into the shader entries read (so the next save writes it)
/// and onto an undo list; what each value was when first changed is kept, to reset it to. A drag is one change.
/// </summary>
internal sealed class ShaderEdits
{
    private sealed record Change(DrawableMaterial Material, string Name, float[] Before, float[] After);

    // A step to undo: its changes, and for a geometry moved to an entry of its own, how to move it back.
    private sealed class Step
    {
        public List<Change> Changes { get; init; } = [];
        public Action? Undo { get; init; }
        public Action? Redo { get; init; }
    }

    // The values as read (or last saved), by entry and name, from their first change: what a reset goes back to.
    private readonly Dictionary<DrawableMaterial, Dictionary<string, float[]>> originals = new(ReferenceEqualityComparer.Instance);
    // Steps to undo, each one or more changes (Reset all is one step); the last one's open while a drag goes on.
    private readonly List<Step> steps = [];
    // Steps undone, the last first to redo; a new change forgets them.
    private readonly List<Step> undone = [];
    private bool open;
    // Whether the next change joins the last step: the first change after a geometry was given an entry of its own.
    private bool joinNext;
    // The entries geometries were given of their own, now and as last saved: each adds an entry to the file. Which ones,
    // not how many: a split undone and another made is a change.
    private readonly List<DrawableMaterial> splits = [];
    private DrawableMaterial[] savedSplits = [];
    // What the last save writes (its snapshot), until it is marked saved: entries read again meanwhile take its values.
    private Dictionary<DrawableMaterial, Dictionary<string, float[]>>? writing;
    private List<DrawableMaterial>? writingSplits;

    /// <summary>Raised after an entry's values changed, by an edit, a reset or an undo.</summary>
    public event Action<DrawableMaterial>? Changed;

    /// <summary>Raised after an undo moved a geometry back to the entry it shared: what is drawn has to be built again.</summary>
    public event Action? StructureChanged;

    /// <summary>Whether any value differs from the file's, or a geometry was given an entry of its own since.</summary>
    public bool Dirty => !SameSplits(splits, savedSplits) || originals.Any(entry =>
    {
        Dictionary<string, float[]> now = CurrentAll(entry.Key);
        return entry.Value.Any(value => !Same(value.Value, now.GetValueOrDefault(value.Key) ?? []));
    });

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => steps.Count > 0;

    /// <summary>Whether there is an undone step to redo.</summary>
    public bool CanRedo => undone.Count > 0;

    /// <summary>A shader entry's numeric values that have a name, as the card lists them: by group, then as values.json orders them.</summary>
    public static IReadOnlyList<ShaderValue> ValuesOf(DrawableMaterial material)
    {
        bool enhanced = material.Gen9Parameters is not null;
        ShaderDefinition? definition = (enhanced ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy).Find(material.ShaderHash);
        var values = new List<ShaderValue>();
        foreach (MaterialParameterValue value in material.GetParameters())
        {
            if (value.Kind != Gen9ShaderParameterKind.ConstantBuffer || value.Name is not { } name || value.Values.Length == 0)
                continue;
            ShaderParameterDefinition? parameter = definition?.FindParameter(value.NameHash);
            // Labelled by the Legacy name, which Enhanced parameters carry as an alias.
            string legacy = (enhanced ? parameter?.LegacyName : null) ?? name;
            int components = parameter?.Kind switch
            {
                ShaderValueKind.Float => 1,
                ShaderValueKind.Float2 => 2,
                ShaderValueKind.Float3 => 3,
                ShaderValueKind.Float4 => 4,
                ShaderValueKind.Float4Array => 4 * Math.Max(1, parameter.Count),
                _ => value.Values.Length,
            };
            components = Math.Clamp(components, 1, value.Values.Length);
            if (Hidden.Contains(legacy))
                continue;
            values.Add(Labels.TryGetValue(legacy, out ShaderValue? known)
                ? known with { Name = name, Components = components, Legacy = legacy }
                : new ShaderValue(name, Words(legacy), Other, components, int.MaxValue, Legacy: legacy));
        }
        return [.. values.OrderBy(value => value.Order).ThenBy(value => value.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A value's numbers as the entry holds them now, all of them (a Legacy value is padded to whole vectors).</summary>
    public static float[] Current(DrawableMaterial material, string name) =>
        material.GetParameters().FirstOrDefault(value => value.Name == name)?.Values.ToArray() ?? [];

    /// <summary>Every named value's numbers as the entry holds them now, read once: for showing all of them.</summary>
    public static Dictionary<string, float[]> CurrentAll(DrawableMaterial material)
    {
        var all = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (MaterialParameterValue value in material.GetParameters())
            if (value.Name is { } name)
                all.TryAdd(name, value.Values.ToArray());
        return all;
    }

    /// <summary>A value's numbers as the file has them: as read, or as last saved.</summary>
    public float[] Original(DrawableMaterial material, string name) =>
        originals.TryGetValue(material, out var own) && own.TryGetValue(name, out float[]? first) ? first : Current(material, name);

    /// <summary>Whether a value differs from the file's.</summary>
    public bool IsChanged(DrawableMaterial material, string name) => IsChanged(material, name, Current(material, name));

    /// <summary>Whether a value whose numbers are <paramref name="now"/> differs from the file's.</summary>
    public bool IsChanged(DrawableMaterial material, string name, float[] now) =>
        originals.TryGetValue(material, out var own) && own.TryGetValue(name, out float[]? first) && !Same(first, now);

    /// <summary>
    /// Sets a value's numbers in its entry. While <paramref name="continuing"/> (a drag after its first step), the change
    /// joins the one before, so undo takes the whole drag back.
    /// </summary>
    public void Set(DrawableMaterial material, string name, float[] values, bool continuing = false)
    {
        float[] before = Current(material, name);
        if (Same(before, values))
            return;
        Step? last = steps.Count > 0 ? steps[^1] : null;
        if (continuing && open && last is { Changes.Count: > 0 } && last.Changes[^1] is var change && change.Material == material && change.Name == name)
            last.Changes[^1] = change with { After = values };
        else if (joinNext && last is not null)
            last.Changes.Add(new Change(material, name, before, values));
        else
            steps.Add(new Step { Changes = [new Change(material, name, before, values)] });
        joinNext = false;
        undone.Clear();
        // A drag's later steps join this one until it ends.
        open = true;
        Remember(material, name, before);
        Apply(material, name, values);
        Changed?.Invoke(material);
    }

    /// <summary>Ends a drag: the next change is a step of its own.</summary>
    public void Close() => open = false;

    /// <summary>
    /// Gives a geometry a copy of its shader entry, added to the entry's list, so changes to it change that geometry
    /// alone; its first change is one step with this. Undone, the geometry goes back to the entry it shared.
    /// </summary>
    public DrawableMaterial Split(DrawableGeometry geometry, List<DrawableMaterial> list, DrawableMaterial material)
    {
        Close();
        DrawableMaterial copy = material.Clone();
        ushort before = geometry.MaterialIndex;
        list.Add(copy);
        geometry.MaterialIndex = (ushort)(list.Count - 1);
        splits.Add(copy);
        undone.Clear();
        steps.Add(new Step
        {
            Undo = () =>
            {
                geometry.MaterialIndex = before;
                list.Remove(copy);
                originals.Remove(copy);
                splits.Remove(copy);
            },
            Redo = () =>
            {
                list.Add(copy);
                geometry.MaterialIndex = (ushort)(list.Count - 1);
                splits.Add(copy);
            },
        });
        joinNext = true;
        return copy;
    }

    /// <summary>
    /// Whether a geometry was given an entry of its own, or moved back to the one it shared, since the file was read or
    /// saved, or since the save under way began: the drawables read again from the file wouldn't have it.
    /// </summary>
    public bool HasUnsavedSplits => !SameSplits(splits, savedSplits) || writingSplits is { } written && !SameSplits(written, splits);

    private static bool SameSplits(IReadOnlyCollection<DrawableMaterial> a, IReadOnlyCollection<DrawableMaterial> b) =>
        a.Count == b.Count && a.All(copy => b.Contains(copy, ReferenceEqualityComparer.Instance));

    /// <summary>Puts a value back as the file has it, as a step of its own.</summary>
    public void Reset(DrawableMaterial material, string name)
    {
        Close();
        Set(material, name, Original(material, name));
        Close();
    }

    /// <summary>Puts every changed value of an entry back as the file has it, as one step.</summary>
    public void ResetAll(DrawableMaterial material) => ResetEntries([material]);

    /// <summary>Puts every changed value of every entry back as the file has it, as one step.</summary>
    public void ResetEverything() => ResetEntries([.. originals.Keys]);

    /// <summary>Whether any value of any entry differs from the file's: what Reset every value has to do.</summary>
    public bool AnyChanged => originals.Any(entry => entry.Value.Keys.Any(name => IsChanged(entry.Key, name)));

    private void ResetEntries(IReadOnlyList<DrawableMaterial> materials)
    {
        Close();
        var step = new List<Change>();
        var touched = new List<DrawableMaterial>();
        foreach (DrawableMaterial material in materials)
        {
            if (!originals.TryGetValue(material, out var own))
                continue;
            int before = step.Count;
            foreach ((string name, float[] first) in own)
            {
                float[] now = Current(material, name);
                if (Same(now, first))
                    continue;
                step.Add(new Change(material, name, now, first));
                Apply(material, name, first);
            }
            if (step.Count > before)
                touched.Add(material);
        }
        if (step.Count == 0)
            return;
        steps.Add(new Step { Changes = step });
        undone.Clear();
        foreach (DrawableMaterial material in touched)
            Changed?.Invoke(material);
    }

    /// <summary>Takes the last step back; false when there is none.</summary>
    public bool Undo()
    {
        Close();
        if (steps.Count == 0)
            return false;
        joinNext = false;
        Step step = steps[^1];
        steps.RemoveAt(steps.Count - 1);
        for (int i = step.Changes.Count - 1; i >= 0; i--)
            Apply(step.Changes[i].Material, step.Changes[i].Name, step.Changes[i].Before);
        if (step.Undo is { } undo)
        {
            undo();
            StructureChanged?.Invoke();
        }
        undone.Add(step);
        foreach (DrawableMaterial material in step.Changes.Select(change => change.Material).Distinct(ReferenceEqualityComparer.Instance).Cast<DrawableMaterial>())
            Changed?.Invoke(material);
        return true;
    }

    /// <summary>Does the last undone step again; false when there is none.</summary>
    public bool Redo()
    {
        Close();
        if (undone.Count == 0)
            return false;
        joinNext = false;
        Step step = undone[^1];
        undone.RemoveAt(undone.Count - 1);
        // A geometry given an entry of its own gets it back first, then the changes made to it.
        if (step.Redo is { } redo)
        {
            redo();
            StructureChanged?.Invoke();
        }
        foreach (Change change in step.Changes)
        {
            Remember(change.Material, change.Name, change.Before);
            Apply(change.Material, change.Name, change.After);
        }
        steps.Add(step);
        foreach (DrawableMaterial material in step.Changes.Select(change => change.Material).Distinct(ReferenceEqualityComparer.Instance).Cast<DrawableMaterial>())
            Changed?.Invoke(material);
        return true;
    }

    /// <summary>The values as they are now become the file's, once it is saved: what a reset goes back to.</summary>
    public void MarkSaved() => MarkSaved(Snapshot());

    /// <summary>The changed values as they are now, by entry and name: what a save writes, for <see cref="MarkSaved(ShaderSnapshot)"/>.</summary>
    public ShaderSnapshot Snapshot()
    {
        Close();
        var now = new Dictionary<DrawableMaterial, Dictionary<string, float[]>>(ReferenceEqualityComparer.Instance);
        foreach ((DrawableMaterial material, Dictionary<string, float[]> own) in originals)
            now[material] = own.Keys.ToDictionary(name => name, name => Current(material, name), StringComparer.Ordinal);
        writing = now;
        writingSplits = [.. splits];
        return new ShaderSnapshot(now, writingSplits);
    }

    /// <summary>
    /// The values a save wrote (<see cref="Snapshot"/>, taken when it started) become the file's: changes made while it
    /// ran stay changed. Drawables read again while it ran have its values under their new entries.
    /// </summary>
    public void MarkSaved(ShaderSnapshot saved)
    {
        savedSplits = [.. saved.Splits];
        if (ReferenceEquals(saved.Values, writing))
            (writing, writingSplits) = (null, null);
        foreach ((DrawableMaterial material, Dictionary<string, float[]> values) in saved.Values)
            if (originals.TryGetValue(material, out var own))
                foreach ((string name, float[] value) in values)
                    own[name] = value;
        Saved?.Invoke();
    }

    /// <summary>Raised once a save's values became the file's.</summary>
    public event Action? Saved;

    /// <summary>
    /// The same drawables read again (with more of their textures, say): each entry of <paramref name="before"/> is
    /// <paramref name="after"/>'s at the same place, and takes the values changed in it, its file's values and its
    /// undo steps. Returns the places whose entries took changed values.
    /// </summary>
    public IReadOnlyList<int> Carry(IReadOnlyList<DrawableMaterial> before, IReadOnlyList<DrawableMaterial> after)
    {
        Close();
        var map = new Dictionary<DrawableMaterial, DrawableMaterial>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < Math.Min(before.Count, after.Count); i++)
            map.TryAdd(before[i], after[i]);
        var carried = new List<int>();
        foreach ((DrawableMaterial old, Dictionary<string, float[]> own) in originals.ToList())
        {
            originals.Remove(old);
            if (!map.TryGetValue(old, out DrawableMaterial? now))
                continue;
            foreach (string name in own.Keys)
                Apply(now, name, Current(old, name));
            originals[now] = own;
            for (int i = 0; i < after.Count; i++)
                if (after[i] == now)
                    carried.Add(i);
        }
        // A save under way marks its values saved on the entries they now live in.
        if (writing is not null)
        {
            var moved = writing.Where(entry => map.ContainsKey(entry.Key)).Select(entry => (Now: map[entry.Key], entry.Value)).ToList();
            writing.Clear();
            foreach ((DrawableMaterial now, Dictionary<string, float[]> values) in moved)
                writing[now] = values;
        }
        // Read again only once the splits are saved, the drawables have the entries as their own: there are none to
        // save, and a split's step can't be undone there.
        splits.Clear();
        savedSplits = [];
        writingSplits?.Clear();
        // Undone steps are forgotten, as a change forgets them.
        undone.Clear();
        for (int s = steps.Count - 1; s >= 0; s--)
        {
            List<Change> step = [.. steps[s].Changes.Where(change => map.ContainsKey(change.Material)).Select(change => change with { Material = map[change.Material] })];
            if (step.Count == 0 || steps[s].Undo is not null)
                steps.RemoveAt(s);
            else
                steps[s] = new Step { Changes = step };
        }
        return carried;
    }

    private void Remember(DrawableMaterial material, string name, float[] before)
    {
        if (!originals.TryGetValue(material, out var own))
            originals[material] = own = new Dictionary<string, float[]>(StringComparer.Ordinal);
        own.TryAdd(name, before);
    }

    private static void Apply(DrawableMaterial material, string name, float[] values) =>
        material.SetParameter(name, values, material.Gen9Parameters is not null ? ShaderCatalog.Enhanced : ShaderCatalog.Legacy);

    private static bool Same(float[] a, float[] b) => a.AsSpan().SequenceEqual(b);

    /// <summary>The group of values values.json doesn't name.</summary>
    public const string Other = "Other";

    // What values.json says: each value it names (its label, group, place, range and whether it's a colour), the
    // groups that start folded, and the values not listed.
    private static readonly (Dictionary<string, ShaderValue> Labels, HashSet<string> Folded, HashSet<string> Hidden) Table = ReadTable();
    private static Dictionary<string, ShaderValue> Labels => Table.Labels;
    private static HashSet<string> Hidden => Table.Hidden;

    /// <summary>Whether a group starts folded on the card: the rarely used ones, and Other.</summary>
    public static bool StartsFolded(string group) => Table.Folded.Contains(group);

    private static (Dictionary<string, ShaderValue>, HashSet<string>, HashSet<string>) ReadTable()
    {
        using JsonDocument document = JsonDocument.Parse(ViewportRenderer.Read("values.json"));
        var labels = new Dictionary<string, ShaderValue>(StringComparer.OrdinalIgnoreCase);
        var folded = new HashSet<string>(StringComparer.Ordinal) { Other };
        var hidden = new HashSet<string>(document.RootElement.GetProperty("hidden").EnumerateArray().Select(name => name.GetString()!), StringComparer.OrdinalIgnoreCase);
        int order = 0;
        foreach (JsonElement group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            string name = group.GetProperty("name").GetString()!;
            if (group.TryGetProperty("folded", out JsonElement fold) && fold.GetBoolean())
                folded.Add(name);
            foreach (JsonProperty value in group.GetProperty("values").EnumerateObject())
            {
                JsonElement entry = value.Value;
                string label = entry.ValueKind == JsonValueKind.String ? entry.GetString()! : entry.GetProperty("label").GetString()!;
                (float, float)? range = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("range", out JsonElement span)
                    ? (span[0].GetSingle(), span[1].GetSingle()) : null;
                bool colour = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("colour", out JsonElement c) && c.GetBoolean();
                labels.TryAdd(value.Name, new ShaderValue(value.Name, label, name, 1, order++, range, colour, value.Name));
            }
        }
        return (labels, folded, hidden);
    }

    /// <summary>A parameter's name in words, for one values.json doesn't name: "envEffTexTileUV" is "Env eff tex tile UV".</summary>
    internal static string Words(string name)
    {
        var words = new StringBuilder();
        string trimmed = name.TrimStart('_').Replace('_', ' ');
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool upper = char.IsUpper(c) || char.IsDigit(c);
            bool startsWord = i > 0 && upper && trimmed[i - 1] != ' '
                && (!char.IsUpper(trimmed[i - 1]) && !char.IsDigit(trimmed[i - 1]) || i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1]));
            if (startsWord && !(char.IsDigit(c) && char.IsDigit(trimmed[i - 1])))
                words.Append(' ');
            words.Append(c);
        }
        // Lower-case each word but the first, unless it is an acronym (UV, RGB).
        string[] parts = words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            bool acronym = part.Length > 1 && part.All(ch => char.IsUpper(ch) || char.IsDigit(ch));
            parts[i] = acronym ? part : i == 0 ? char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..].ToLowerInvariant() : part.ToLowerInvariant();
        }
        return string.Join(' ', parts);
    }
}
