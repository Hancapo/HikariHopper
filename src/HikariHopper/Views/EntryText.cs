using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>
/// Port of <c>entry_text.js</c>: an entry name with the first case-insensitive match of the search
/// query underlined, bold and tinted (Qt StyledText <c>&lt;u&gt;&lt;font color&gt;&lt;b&gt;</c>).
/// </summary>
public static class EntryText
{
    /// <summary>Where the query matches in <paramref name="name"/>: start and length, or (-1, 0).</summary>
    public static (int At, int Length) Match(string name, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return (-1, 0);
        // JavaScript toLowerCase().indexOf(): an ordinal search over lower-cased UTF-16 text.
        int at = name.ToLowerInvariant().IndexOf(query.ToLowerInvariant(), StringComparison.Ordinal);
        return at < 0 ? (-1, 0) : (at, Math.Min(query.Length, name.Length - at));
    }

    /// <summary>
    /// Fills <paramref name="inlines"/> with the name, marking the match in <paramref name="ink"/>.
    /// The unmatched runs inherit the TextBlock's foreground, as the StyledText did.
    /// </summary>
    public static void MarkMatch(InlineCollection inlines, string name, string? query, IBrush ink)
    {
        inlines.Clear();
        (int at, int length) = Match(name, query);
        if (at < 0)
        {
            inlines.Add(new Run(name));
            return;
        }
        if (at > 0)
            inlines.Add(new Run(name[..at]));
        inlines.Add(new Run(name.Substring(at, length))
        {
            Foreground = ink,
            FontWeight = FontWeight.Bold,
            TextDecorations = TextDecorations.Underline,
        });
        if (at + length < name.Length)
            inlines.Add(new Run(name[(at + length)..]));
    }
}
