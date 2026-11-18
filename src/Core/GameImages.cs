using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommonFury.Hashing;
using FiveFury.Assets;
using FiveFury.Authoring;

namespace HikariHopper.Core;

/// <summary>
/// The archives GTA V's level registers its streamed files from, in the order it registers them: the RPF_FILE items of
/// the data files its level descriptor (common:/data/levels/gta5/gta5.meta) includes, in the order it includes them.
/// The game registers each name once, from the first archive that has it, and refuses it from any later one. FiveFury
/// mounts every archive the game's folders hold, those the game never registers (indust_02's id2_props.rpf, say) among
/// them, so a name in two such archives at the same rank comes back ambiguous and unread; the game reads it from the
/// archive it registers.
/// </summary>
internal sealed partial class GameImages
{
    private static readonly ConditionalWeakTable<GameFileSystem, GameImages> Known = new();

    // Each registered archive's virtual path (platform:/levels/...rpf), lowercase, by when the game registers it.
    private readonly Dictionary<string, int> order = new(StringComparer.Ordinal);

    private GameImages(GameFileSystem files)
    {
        if (Text(files, "common:/data/levels/gta5/gta5.meta") is not { } level)
            return;
        List<string> lists = [.. Include().Matches(level).Select(match => match.Groups[1].Value.Replace("commoncrc:", "common:", StringComparison.OrdinalIgnoreCase))];
        foreach (string list in lists)
        {
            if (Text(files, list) is not { } text)
                continue;
            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }
            foreach (XElement item in document.Descendants("Item"))
                if (item.Element("fileType")?.Value.Trim() == "RPF_FILE" && item.Element("filename")?.Value.Trim() is { Length: > 0 } name)
                    order.TryAdd(name.ToLowerInvariant(), order.Count);
        }
    }

    /// <summary>The level's archives as the game registers them, read once for each mounted game.</summary>
    public static GameImages Of(GameFileSystem files) => Known.GetValue(files, mounted => new GameImages(mounted));

    /// <summary>How many archives the level registers.</summary>
    public int Count => order.Count;

    /// <summary>
    /// The file the game reads for a name: FiveFury's, or of the copies it finds ambiguous, the one in the archive the
    /// game registers first; none if the game registers none of them.
    /// </summary>
    public GameFile? Resolve(GameFileSystem files, Hash32 name, GameFileType type)
    {
        Resolution<GameFile> resolution = files.Resolve(name, type);
        if (resolution.Status != ResolutionStatus.Ambiguous)
            return resolution.Value;
        var sources = new HashSet<string>(resolution.Sources, StringComparer.OrdinalIgnoreCase);
        GameFile? best = null;
        int rank = int.MaxValue;
        foreach (GameFile candidate in files.GetCandidates(name))
            if (candidate.Type == type && sources.Contains(candidate.SourcePath) && Rank(candidate) is int at && at < rank)
                (best, rank) = (candidate, at);
        return best;
    }

    // When the game registers the archive a file is in: the innermost archive of its virtual path; null if never.
    private int? Rank(GameFile file)
    {
        string path = file.VirtualPath.ToLowerInvariant();
        int archive = path.LastIndexOf(".rpf/", StringComparison.Ordinal);
        return archive >= 0 && order.TryGetValue(path[..(archive + 4)], out int at) ? at : null;
    }

    // A data file's text, from its first tag on (some of the game's start with a blank line).
    private static string? Text(GameFileSystem files, string path)
    {
        if (files.Resolve(path).Value is not { } file)
            return null;
        string text = Encoding.UTF8.GetString(file.ReadStandalone());
        int start = text.IndexOf('<');
        return start >= 0 ? text[start..] : null;
    }

    [GeneratedRegex("<xi:include[^>]*href=\"([^\"]+)\"")]
    private static partial Regex Include();
}

/// <summary>Resolving a name as the game registers it (<see cref="GameImages"/>).</summary>
internal static class GameImagesResolve
{
    /// <summary>The file the game reads for a name, of the copies FiveFury finds at the same rank.</summary>
    public static GameFile? ResolveRegistered(this GameFileSystem files, Hash32 name, GameFileType type) =>
        GameImages.Of(files).Resolve(files, name, type);
}
