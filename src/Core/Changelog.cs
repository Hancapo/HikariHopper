using System.Text.RegularExpressions;

namespace HikariHopper.Core;

/// <summary>One version's changes: its name ("0.0.2", or "Unreleased"), its date, and its groups.</summary>
public sealed record ChangelogRelease(string Version, string Date, IReadOnlyList<ChangelogGroup> Groups)
{
    /// <summary>The changes since the last release, not yet in one.</summary>
    public bool IsUnreleased => Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A group of changes ("Added", "Fixed"…) and its entries, as plain text.</summary>
public sealed record ChangelogGroup(string Name, IReadOnlyList<string> Entries);

/// <summary>
/// The app's <c>CHANGELOG.md</c> (Keep a Changelog), read for showing in the app: versions, their groups and
/// their entries, with the markdown taken out. Empty groups, and versions with nothing in them, are left out.
/// </summary>
public static partial class Changelog
{
    /// <summary>Reads <paramref name="markdown"/> into its versions, newest first, as the file lists them.</summary>
    public static IReadOnlyList<ChangelogRelease> Parse(string markdown)
    {
        var releases = new List<ChangelogRelease>();
        string? version = null, date = "";
        var groups = new List<ChangelogGroup>();
        string? group = null;
        var entries = new List<string>();

        void EndGroup()
        {
            if (group is not null && entries.Count > 0)
                groups.Add(new ChangelogGroup(group, [.. entries]));
            group = null;
            entries.Clear();
        }

        void EndRelease()
        {
            EndGroup();
            if (version is not null && groups.Count > 0)
                releases.Add(new ChangelogRelease(version, date ?? "", [.. groups]));
            version = null;
            groups.Clear();
        }

        foreach (string raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndRelease();
                Match heading = ReleaseHeading().Match(line);
                version = heading.Success ? heading.Groups["version"].Value : line[3..].Trim();
                date = heading.Success ? heading.Groups["date"].Value : "";
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal) && version is not null)
            {
                EndGroup();
                group = line[4..].Trim();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) && group is not null)
            {
                entries.Add(Plain(line[2..]));
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0 && entries.Count > 0)
            {
                // A wrapped entry carries on.
                entries[^1] = $"{entries[^1]} {Plain(line.Trim())}";
            }
        }
        EndRelease();
        return releases;
    }

    /// <summary>An entry without its markdown: no PR links at the end, links as their text, code as text.</summary>
    public static string Plain(string entry)
    {
        string text = TrailingLinks().Replace(entry.Trim(), "");
        text = Link().Replace(text, "${text}");
        return text.Replace("`", "").Trim();
    }

    [GeneratedRegex(@"^## \[(?<version>[^\]]+)\](?:\s*-\s*(?<date>\S+))?")]
    private static partial Regex ReleaseHeading();

    // The " ([#46](…), [#47](…))" every entry ends with.
    [GeneratedRegex(@"\s*\((?:\[#\d+\]\([^)]*\)(?:,\s*)?)+\)\s*$")]
    private static partial Regex TrailingLinks();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();
}
