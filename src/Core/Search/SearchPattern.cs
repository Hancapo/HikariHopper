using System.Text.RegularExpressions;

namespace HikariHopper.Core.Search;

/// <summary>
/// What a search matches a file name against: plain text found anywhere in the name, a wildcard pattern that must
/// match the whole name (<c>*.ydr</c>, <c>prop_*_01?</c>), or a regular expression. Wildcards turn on by themselves
/// when the text has <c>*</c> or <c>?</c>, unless they are switched off.
/// </summary>
public sealed class SearchPattern
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private readonly string text;
    private readonly StringComparison comparison;
    private readonly Regex? regex;
    private readonly bool spansAreGroups;

    private SearchPattern(string text, bool matchCase, Regex? regex, bool spansAreGroups, SearchMode mode)
    {
        this.text = text;
        comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        this.regex = regex;
        this.spansAreGroups = spansAreGroups;
        Mode = mode;
    }

    /// <summary>How <see cref="Match"/> reads the text.</summary>
    public SearchMode Mode { get; }

    /// <summary>Whether the text, as written, would turn wildcards on by itself.</summary>
    public static bool LooksLikeWildcards(string text) => text.Contains('*') || text.Contains('?');

    /// <summary>
    /// Reads <paramref name="text"/> as a pattern. <paramref name="wildcards"/> null means "when the text has * or ?".
    /// Null, with <paramref name="error"/> saying why, when a regular expression doesn't parse; null with no error
    /// for blank text.
    /// </summary>
    public static SearchPattern? Parse(string text, bool matchCase, bool? wildcards, bool regex, out string error)
    {
        error = "";
        if (text.Length == 0)
            return null;
        RegexOptions options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        if (regex)
        {
            try
            {
                return new SearchPattern(text, matchCase, new Regex(text, options, Timeout), false, SearchMode.Regex);
            }
            catch (RegexParseException failure)
            {
                error = Describe(failure);
                return null;
            }
        }
        if (wildcards ?? LooksLikeWildcards(text))
        {
            // The whole name must match; each literal run is a group, so it can be marked.
            var pattern = new System.Text.StringBuilder("^");
            var literal = new System.Text.StringBuilder();
            void Flush()
            {
                if (literal.Length == 0)
                    return;
                pattern.Append('(').Append(Regex.Escape(literal.ToString())).Append(')');
                literal.Clear();
            }
            foreach (char c in text)
            {
                if (c is '*' or '?')
                {
                    Flush();
                    pattern.Append(c == '*' ? ".*" : ".");
                }
                else
                {
                    literal.Append(c);
                }
            }
            Flush();
            pattern.Append('$');
            return new SearchPattern(text, matchCase, new Regex(pattern.ToString(), options, Timeout), true, SearchMode.Wildcards);
        }
        return new SearchPattern(text, matchCase, null, false, SearchMode.Text);
    }

    /// <summary>The spans of <paramref name="name"/> that matched, to mark them; null when it doesn't match.</summary>
    public IReadOnlyList<(int Start, int Length)>? Match(string name)
    {
        if (regex is null)
        {
            int at = name.IndexOf(text, comparison);
            return at < 0 ? null : [(at, text.Length)];
        }
        System.Text.RegularExpressions.Match match;
        try
        {
            match = regex.Match(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
        if (!match.Success)
            return null;
        if (!spansAreGroups)
            return [(match.Index, match.Length)];
        var spans = new List<(int, int)>();
        for (int group = 1; group < match.Groups.Count; group++)
        {
            if (match.Groups[group].Length > 0)
                spans.Add((match.Groups[group].Index, match.Groups[group].Length));
        }
        return spans;
    }

    /// <summary>A regular expression's parse error in plain words, with where it was found.</summary>
    private static string Describe(RegexParseException failure)
    {
        string what = failure.Error switch
        {
            RegexParseError.InsufficientClosingParentheses => "Missing ) to close a group",
            RegexParseError.InsufficientOpeningParentheses => "A ) closes no group",
            RegexParseError.UnterminatedBracket => "Missing ] to close a set",
            RegexParseError.QuantifierAfterNothing => "Nothing before the quantifier",
            RegexParseError.NestedQuantifiersNotParenthesized => "Two quantifiers in a row",
            RegexParseError.UnescapedEndingBackslash => "A \\ at the end escapes nothing",
            RegexParseError.ReversedCharacterRange => "A range runs backwards",
            RegexParseError.ReversedQuantifierRange => "A {n,m} runs backwards",
            RegexParseError.UnrecognizedEscape => "Unknown escape",
            _ => "Not a valid regular expression",
        };
        return failure.Offset > 0 ? $"{what} at {failure.Offset}" : what;
    }
}

/// <summary>How a search's text is read.</summary>
public enum SearchMode
{
    Text,
    Wildcards,
    Regex,
}
