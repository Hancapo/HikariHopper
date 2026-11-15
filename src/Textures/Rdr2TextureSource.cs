using CommonFury;
using CommonFury.Hashing;
using FiveFury.Textures;
using RedYtd = RedFury.Ytd.Ytd;
using RedFormat = RedFury.Textures.TextureFormat;
using RedTexture = RedFury.Textures.Texture;
using TextureImage = RedFury.Textures.TextureImage;

namespace HikariHopper.Textures;

/// <summary>
/// A Red Dead Redemption 2 texture dictionary in the editor: the one it was read from, through RedFury. The editor works
/// on its textures as it does on GTA V's (FiveFury's texture model: name, size, DXGI format, mips, a linear mip chain),
/// which they convert to as they are; the original keeps what that model has no room for, so a save can keep it.
/// </summary>
public sealed record Rdr2TextureSource(RedYtd Original)
{
    // The stored texture each texture the editor was given came from: edits make new texture objects, so one of these
    // is a texture as the game stored it, and one the editor renamed or changed keeps its original's fields.
    private readonly Dictionary<Texture, RedTexture> stored = new(ReferenceEqualityComparer.Instance);

    /// <summary>The first four bytes of a standalone RSC8 resource, as RDR2's dictionaries are.</summary>
    public static bool IsRdr2(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual("RSC8"u8);

    /// <summary>
    /// Reads a standalone RDR2 dictionary; its textures in their stored order. A volume or array texture, which the
    /// editor's model doesn't hold, is refused rather than shown wrong.
    /// </summary>
    public static TextureSet Read(ReadOnlySpan<byte> data)
    {
        RedYtd original = RedYtd.Read(data);
        var source = new Rdr2TextureSource(original);
        var textures = new List<Texture>();
        foreach (RedTexture texture in original.Textures)
        {
            if (texture.Depth != 1)
                throw new NotSupportedException($"{texture.Name} is a volume or array texture, which the editor doesn't open yet");
            var converted = new Texture(texture.Name, texture.Width, texture.Height,
                (CommonFury.Textures.TextureFormat)(uint)texture.Format, texture.Mips.Count, texture.Data.Span);
            source.stored[converted] = texture;
            textures.Add(converted);
        }
        return new TextureSet(textures, FiveFury.GameTarget.Legacy) { Rdr2 = source };
    }

    /// <summary>
    /// Writes <paramref name="dictionary"/>, one of this source's, as a standalone RDR2 YTD, through RedFury: a texture as
    /// the game stored it, exactly as it was (every descriptor field and byte); one changed under its own name with its
    /// original's usage and tile mode; a renamed or new one with RedFury's defaults, which every retail texture uses. A
    /// dictionary whose textures RedFury can't write back as they were (a CPU-side texture, a render target, the particle
    /// atlas) is refused, saying which, rather than changed. Nothing is returned that doesn't read back as the editor has it.
    /// </summary>
    public byte[] Write(TextureSet dictionary)
    {
        var images = new List<TextureImage>();
        foreach (Texture texture in dictionary.Textures)
        {
            RedTexture? original = stored.GetValueOrDefault(texture) ?? Original.Find(texture.Name ?? "");
            if (original is not null)
            {
                TextureImage kept;
                try
                {
                    kept = TextureImage.FromTexture(original);
                }
                catch (NotSupportedException exception)
                {
                    throw new NotSupportedException($"{original.Name} can't be saved back as the game stores it yet: {exception.Message}", exception);
                }
                if (stored.TryGetValue(texture, out RedTexture? same) && ReferenceEquals(same, original))
                {
                    images.Add(kept);
                    continue;
                }
                // A 2D surface, as every texture the editor opens is: its original's usage and tile mode kept.
                images.Add(new TextureImage(texture.Name ?? "", texture.Width, texture.Height, (RedFormat)(byte)(uint)texture.Format,
                    texture.MipCount, texture.Data) { Usage = kept.Usage, TileMode = kept.TileMode });
                continue;
            }
            images.Add(new TextureImage(texture.Name ?? "", texture.Width, texture.Height, (RedFormat)(byte)(uint)texture.Format,
                texture.MipCount, texture.Data));
        }
        byte[] result = RedYtd.Create(images).Write();
        // Read back: the same textures, as the editor has them.
        RedYtd reread = RedYtd.Read(result);
        if (reread.Textures.Count != dictionary.Textures.Length)
            throw new InvalidDataException("The written dictionary doesn't hold the same textures");
        foreach (Texture texture in dictionary.Textures)
        {
            RedTexture? written = reread.Find(texture.Name ?? "");
            if (written is null || written.Width != texture.Width || written.Height != texture.Height || written.Mips.Count != texture.MipCount
                || (uint)written.Format != (uint)texture.Format || !written.Data.Span.SequenceEqual(texture.Data))
                throw new InvalidDataException($"The written dictionary changed {texture.Name}");
        }
        return result;
    }

    /// <summary>
    /// What RDR2 needs of a dictionary's textures: a name each, names whose lookup hashes don't repeat (RDR2 finds a
    /// texture by its hash), and formats RDR2 stores.
    /// </summary>
    public static ValidationReport Validate(IEnumerable<Texture> textures)
    {
        var issues = new List<Diagnostic>();
        var hashes = new Dictionary<uint, string>();
        foreach (Texture texture in textures)
        {
            string name = texture.Name ?? "";
            if (name.Length == 0)
            {
                issues.Add(Issue("A texture has no name"));
                continue;
            }
            if (!hashes.TryAdd(Jenkins.Hash(name), name))
                issues.Add(Issue($"{name} has the same lookup hash as {hashes[Jenkins.Hash(name)]}"));
            if (!Enum.IsDefined((RedFormat)(byte)(uint)texture.Format) || (uint)texture.Format > byte.MaxValue)
                issues.Add(Issue($"{name}'s format, {texture.Format}, isn't one RDR2 stores"));
        }
        return new ValidationReport(issues);
    }

    private static Diagnostic Issue(string message) => new("rdr2.texture", message);
}
