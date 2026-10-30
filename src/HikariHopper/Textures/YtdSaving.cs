using CommonFury.Hashing;
using FiveFury.Textures;
using FiveFury.Ytd;

namespace HikariHopper.Textures;

/// <summary>Reads and writes standalone YTDs for the editor (replaces <c>ytd_saving.py</c> and <c>ytd_empty_ad_hoc.py</c>).</summary>
/// <remarks>FiveFury.NET reads, validates and writes empty dictionaries natively for both editions, and its resource
/// layout places every allocation (dictionary, descriptors, names, payloads and the pages-info table) within one page,
/// so neither Python workaround is needed. The round-trip verification of <c>serialize_ytd</c> is kept: nothing reaches
/// a destination unless the written bytes read back strictly, validate and reproduce the model.</remarks>
public static class YtdSaving
{
    public static TextureSet Read(ReadOnlySpan<byte> data)
    {
        Ytd ytd = Ytd.Read(data);
        return new TextureSet(ytd.Textures, ytd.Target);
    }

    /// <summary>Builds and verifies YTD bytes before any destination is touched.</summary>
    public static byte[] Serialize(TextureSet dictionary)
    {
        Ytd model = dictionary.ToYtd();
        if (dictionary.Textures.IsEmpty)
            return model.Write(allowEmpty: true);
        byte[] result = model.Write();
        Ytd reread = Ytd.Read(result);
        reread.Validate().ThrowIfInvalid();
        if (reread.Target != dictionary.Target || reread.Textures.Count != dictionary.Textures.Length)
            throw new InvalidDataException("YTD round-trip changed the target or texture count");
        Texture[] ordered = [.. dictionary.Textures.OrderBy(texture => Jenkins.Hash(texture.Name ?? ""))];
        for (int index = 0; index < ordered.Length; index++)
            if (!TextureComparison.SameContent(ordered[index], reread.Textures[index]))
                throw new InvalidDataException($"YTD round-trip changed texture data: {ordered[index].Name}");
        return result;
    }
}

/// <summary>Value comparison of the texture fields the editor round-trips.</summary>
public static class TextureComparison
{
    public static bool SameContent(Texture before, Texture after) =>
        ReferenceEquals(before, after)
        || before.Name == after.Name && before.Width == after.Width && before.Height == after.Height
            && before.Format == after.Format && before.MipCount == after.MipCount && before.Usage == after.Usage
            && before.UsageFlags == after.UsageFlags && before.Shape == after.Shape && before.Data.SequenceEqual(after.Data);
}
