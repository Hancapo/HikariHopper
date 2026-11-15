using CommonFury;
using CommonFury.Hashing;
using FiveFury.Textures;
using RedYtd = RedFury.Ytd.Ytd;
using RedFormat = RedFury.Textures.TextureFormat;

namespace HikariHopper.Textures;

/// <summary>
/// A Red Dead Redemption 2 texture dictionary in the editor: the one it was read from, through RedFury. The editor works
/// on its textures as it does on GTA V's (FiveFury's texture model: name, size, DXGI format, mips, a linear mip chain),
/// which they convert to as they are; the original keeps what that model has no room for, so a save can keep it.
/// </summary>
public sealed record Rdr2TextureSource(RedYtd Original)
{
    /// <summary>The first four bytes of a standalone RSC8 resource, as RDR2's dictionaries are.</summary>
    public static bool IsRdr2(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual("RSC8"u8);

    /// <summary>
    /// Reads a standalone RDR2 dictionary; its textures in their stored order. A volume or array texture, which the
    /// editor's model doesn't hold, is refused rather than shown wrong.
    /// </summary>
    public static TextureSet Read(ReadOnlySpan<byte> data)
    {
        RedYtd original = RedYtd.Read(data);
        var textures = new List<Texture>();
        foreach (RedFury.Textures.Texture texture in original.Textures)
        {
            if (texture.Depth != 1)
                throw new NotSupportedException($"{texture.Name} is a volume or array texture, which the editor doesn't open yet");
            textures.Add(new Texture(texture.Name, texture.Width, texture.Height,
                (CommonFury.Textures.TextureFormat)(uint)texture.Format, texture.Mips.Count, texture.Data.Span));
        }
        return new TextureSet(textures, FiveFury.GameTarget.Legacy) { Rdr2 = new Rdr2TextureSource(original) };
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
