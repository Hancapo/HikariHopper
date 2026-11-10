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
internal sealed record ShaderValue(string Name, string Label, string Group, int Components, int Order);

/// <summary>
/// The shader values edited in a 3D viewer: each change goes into the shader entries read (so the next save writes it)
/// and onto an undo list; what each value was when first changed is kept, to reset it to. A drag is one change.
/// </summary>
internal sealed class ShaderEdits
{
    private sealed record Change(DrawableMaterial Material, string Name, float[] Before, float[] After);

    // The values as read (or last saved), by entry and name, from their first change: what a reset goes back to.
    private readonly Dictionary<DrawableMaterial, Dictionary<string, float[]>> originals = new(ReferenceEqualityComparer.Instance);
    // Steps to undo, each one or more changes (Reset all is one step); the last one's open while a drag goes on.
    private readonly List<List<Change>> steps = [];
    private bool open;

    /// <summary>Raised after an entry's values changed, by an edit, a reset or an undo.</summary>
    public event Action<DrawableMaterial>? Changed;

    /// <summary>Whether any value differs from the file's.</summary>
    public bool Dirty => originals.Any(entry => entry.Value.Any(value => !Same(value.Value, Current(entry.Key, value.Key))));

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => steps.Count > 0;

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
            (string label, string group, int order) = Labels.TryGetValue(legacy, out var known) ? known : (Words(legacy), Other, int.MaxValue);
            values.Add(new ShaderValue(name, label, group, components, order));
        }
        return [.. values.OrderBy(value => value.Order).ThenBy(value => value.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A value's numbers as the entry holds them now, all of them (a Legacy value is padded to whole vectors).</summary>
    public static float[] Current(DrawableMaterial material, string name) =>
        material.GetParameters().FirstOrDefault(value => value.Name == name)?.Values.ToArray() ?? [];

    /// <summary>A value's numbers as the file has them: as read, or as last saved.</summary>
    public float[] Original(DrawableMaterial material, string name) =>
        originals.TryGetValue(material, out var own) && own.TryGetValue(name, out float[]? first) ? first : Current(material, name);

    /// <summary>Whether a value differs from the file's.</summary>
    public bool IsChanged(DrawableMaterial material, string name) =>
        originals.TryGetValue(material, out var own) && own.TryGetValue(name, out float[]? first) && !Same(first, Current(material, name));

    /// <summary>
    /// Sets a value's numbers in its entry. While <paramref name="continuing"/> (a drag after its first step), the change
    /// joins the one before, so undo takes the whole drag back.
    /// </summary>
    public void Set(DrawableMaterial material, string name, float[] values, bool continuing = false)
    {
        float[] before = Current(material, name);
        if (Same(before, values))
            return;
        if (continuing && open && steps.Count > 0 && steps[^1] is [var last] && last.Material == material && last.Name == name)
            steps[^1] = [last with { After = values }];
        else
            steps.Add([new Change(material, name, before, values)]);
        // A drag's later steps join this one until it ends.
        open = true;
        Remember(material, name, before);
        Apply(material, name, values);
        Changed?.Invoke(material);
    }

    /// <summary>Ends a drag: the next change is a step of its own.</summary>
    public void Close() => open = false;

    /// <summary>Puts a value back as the file has it, as a step of its own.</summary>
    public void Reset(DrawableMaterial material, string name)
    {
        Close();
        Set(material, name, Original(material, name));
        Close();
    }

    /// <summary>Puts every changed value of an entry back as the file has it, as one step.</summary>
    public void ResetAll(DrawableMaterial material)
    {
        Close();
        if (!originals.TryGetValue(material, out var own))
            return;
        var step = new List<Change>();
        foreach ((string name, float[] first) in own)
        {
            float[] now = Current(material, name);
            if (Same(now, first))
                continue;
            step.Add(new Change(material, name, now, first));
            Apply(material, name, first);
        }
        if (step.Count == 0)
            return;
        steps.Add(step);
        Changed?.Invoke(material);
    }

    /// <summary>Takes the last step back; false when there is none.</summary>
    public bool Undo()
    {
        Close();
        if (steps.Count == 0)
            return false;
        List<Change> step = steps[^1];
        steps.RemoveAt(steps.Count - 1);
        for (int i = step.Count - 1; i >= 0; i--)
            Apply(step[i].Material, step[i].Name, step[i].Before);
        foreach (DrawableMaterial material in step.Select(change => change.Material).Distinct(ReferenceEqualityComparer.Instance).Cast<DrawableMaterial>())
            Changed?.Invoke(material);
        return true;
    }

    /// <summary>The values as they are now become the file's, once it is saved: what a reset goes back to.</summary>
    public void MarkSaved() => MarkSaved(Snapshot());

    /// <summary>The changed values as they are now, by entry and name: what a save writes, for <see cref="MarkSaved(IReadOnlyDictionary{DrawableMaterial, Dictionary{string, float[]}})"/>.</summary>
    public IReadOnlyDictionary<DrawableMaterial, Dictionary<string, float[]>> Snapshot()
    {
        Close();
        var now = new Dictionary<DrawableMaterial, Dictionary<string, float[]>>(ReferenceEqualityComparer.Instance);
        foreach ((DrawableMaterial material, Dictionary<string, float[]> own) in originals)
            now[material] = own.Keys.ToDictionary(name => name, name => Current(material, name), StringComparer.Ordinal);
        return now;
    }

    /// <summary>
    /// The values a save wrote (<see cref="Snapshot"/>, taken when it started) become the file's: changes made while it
    /// ran stay changed.
    /// </summary>
    public void MarkSaved(IReadOnlyDictionary<DrawableMaterial, Dictionary<string, float[]>> saved)
    {
        foreach ((DrawableMaterial material, Dictionary<string, float[]> values) in saved)
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
        for (int s = steps.Count - 1; s >= 0; s--)
        {
            List<Change> step = [.. steps[s].Where(change => map.ContainsKey(change.Material)).Select(change => change with { Material = map[change.Material] })];
            if (step.Count == 0)
                steps.RemoveAt(s);
            else
                steps[s] = step;
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

    // Each value values.json names: its label, its group, and where it comes, by group and then as listed.
    private static readonly Dictionary<string, (string Label, string Group, int Order)> Labels = ReadLabels();

    private static Dictionary<string, (string, string, int)> ReadLabels()
    {
        using JsonDocument document = JsonDocument.Parse(ViewportRenderer.Read("values.json"));
        var labels = new Dictionary<string, (string, string, int)>(StringComparer.OrdinalIgnoreCase);
        int order = 0;
        foreach (JsonElement group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            string name = group.GetProperty("name").GetString()!;
            foreach (JsonProperty value in group.GetProperty("values").EnumerateObject())
                labels.TryAdd(value.Name, (value.Value.GetString()!, name, order++));
        }
        return labels;
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
