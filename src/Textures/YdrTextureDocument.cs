using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using FiveFury.Ydr;

namespace HikariHopper.Textures;

/// <summary>A drawable's embedded textures, edited without rebuilding the drawable.</summary>
/// <remarks>
/// Saving goes through FiveFury.NET's <see cref="Ydr.WriteTextureEdits"/>: every other byte of the drawable keeps its
/// address, unchanged pixel data stays where it was, and the old dictionary and anything nothing else points to are
/// cleared and reused before a section grows, so repeated saves don't accumulate copies. It never falls back to the
/// rebuilding <see cref="Ydr.Write"/>.
/// </remarks>
public sealed class YdrTextureDocument
{
    private YdrTextureDocument(byte[] original, GameTarget target, TextureSet dictionary, IReadOnlyList<string?> bindings)
    {
        Original = original;
        Target = target;
        Dictionary = dictionary;
        Bindings = bindings;
    }

    /// <summary>The complete source resource, returned unchanged by a no-op save.</summary>
    public byte[] Original { get; }

    public GameTarget Target { get; }

    /// <summary>The embedded textures as read.</summary>
    public TextureSet Dictionary { get; }

    /// <summary>
    /// Every material texture reference in model order: the embedded texture it is bound to, or null for an external one.
    /// </summary>
    public IReadOnlyList<string?> Bindings { get; }

    public static YdrTextureDocument Read(byte[] data)
    {
        Ydr ydr = Ydr.Read(data);
        TextureSet dictionary = new(ydr.Drawable.TextureDictionary?.Textures ?? [], ydr.Target);
        return new YdrTextureDocument(data.ToArray(), ydr.Target, dictionary, [.. References(ydr).Select(reference => reference.EmbeddedTexture?.Name)]);
    }

    /// <summary>Writes <paramref name="dictionary"/> into a copy of the drawable. <paramref name="renames"/> maps
    /// original embedded names to current names; <paramref name="deleted"/> lists removed original names.</summary>
    /// <remarks>Materials follow their texture through a rename. A material whose texture was deleted, or no longer
    /// exists under its name, refers to that name externally instead. External references are never rebound merely
    /// because an embedded texture has their name.</remarks>
    public byte[] Serialize(TextureSet dictionary, IReadOnlyDictionary<string, string> renames, IReadOnlySet<string>? deleted = null)
    {
        deleted ??= new HashSet<string>(StringComparer.Ordinal);
        if (dictionary.Target != Target)
            throw new ArgumentException("A drawable's textures cannot change edition");
        if (SameTextures(dictionary.Textures, Dictionary.Textures) && renames.Count == 0 && deleted.Count == 0)
            return Original;

        // A private copy of the drawable, so the textures the editor shares are never touched.
        Ydr ydr = Ydr.Read(Original, Target);
        List<Texture> source = ydr.Drawable.TextureDictionary?.Textures ?? [];
        // With two embedded textures of one name, which one a material meant can't be told apart.
        if (source.Select(texture => texture.Name ?? "").Distinct(StringComparer.Ordinal).Count() != source.Count)
            throw new InvalidDataException("YDR has ambiguous duplicate texture origins; rewriting is not supported");
        var originals = source.ToDictionary(texture => texture.Name ?? "", StringComparer.Ordinal);
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in originals.Keys.Where(name => !deleted.Contains(name)))
            origins[renames.GetValueOrDefault(name, name)] = name;

        var textures = new List<Texture>();
        foreach (Texture texture in dictionary.Textures)
        {
            // A texture whose pixels and metadata are unchanged keeps the source's object, so its payload stays put.
            if (origins.TryGetValue(texture.Name ?? "", out string? origin) && SameContentIgnoringName(originals[origin], texture))
            {
                originals[origin].Name = texture.Name;
                textures.Add(originals[origin]);
            }
            else
            {
                textures.Add(texture);
            }
        }
        var byName = textures.ToDictionary(texture => texture.Name ?? "", StringComparer.Ordinal);

        List<TextureReference> references = [.. References(ydr)];
        var expected = new List<string?>();
        for (int index = 0; index < references.Count; index++)
        {
            if (Bindings[index] is not { } embedded)
            {
                expected.Add(null);
                continue;
            }
            string name = renames.GetValueOrDefault(embedded, embedded);
            if (!deleted.Contains(embedded) && byName.TryGetValue(name, out Texture? texture))
            {
                references[index].Bind(texture);
                expected.Add(name);
            }
            else
            {
                references[index].ReferTo(name);
                expected.Add(null);
            }
        }

        // A drawable left without embedded textures has no dictionary at all.
        ydr.Drawable.TextureDictionary = textures.Count == 0 ? null : new TextureDictionary();
        ydr.Drawable.TextureDictionary?.Textures.AddRange(textures);
        byte[] result = ydr.WriteTextureEdits();
        Verify(result, dictionary, expected);
        return result;
    }

    /// <summary>Reads the written drawable back: the textures and every material binding must be what was asked for.</summary>
    private void Verify(byte[] result, TextureSet dictionary, IReadOnlyList<string?> bindings)
    {
        YdrTextureDocument reread = Read(result);
        Texture[] before = [.. dictionary.Textures.OrderBy(texture => texture.Name, StringComparer.Ordinal)];
        Texture[] after = [.. reread.Dictionary.Textures.OrderBy(texture => texture.Name, StringComparer.Ordinal)];
        if (before.Length != after.Length || before.Zip(after).Any(pair => !TextureComparison.SameContent(pair.First, pair.Second)))
            throw new InvalidDataException("YDR round-trip changed texture data or metadata");
        if (!reread.Bindings.SequenceEqual(bindings))
            throw new InvalidDataException("YDR round-trip changed material texture bindings");
    }

    private static IEnumerable<TextureReference> References(Ydr ydr) =>
        ydr.Drawable.Materials.Distinct(ReferenceEqualityComparer.Instance).Cast<FiveFury.Drawables.DrawableMaterial>()
            .SelectMany(material => material.EnumerateTextureReferences());

    private static bool SameContentIgnoringName(Texture original, Texture texture) =>
        TextureComparison.SameContent(original, texture)
        || original.Width == texture.Width && original.Height == texture.Height && original.Format == texture.Format
            && original.MipCount == texture.MipCount && original.Usage == texture.Usage && original.UsageFlags == texture.UsageFlags
            && original.Shape == texture.Shape && original.Data.SequenceEqual(texture.Data);

    private static bool SameTextures(IReadOnlyList<Texture> left, IReadOnlyList<Texture> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => TextureComparison.SameContent(pair.First, pair.Second));
}
