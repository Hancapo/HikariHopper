using Avalonia;
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
    /// Writes where a search result lives as the address bar does: folders dim between chevrons, RPFs in blue with
    /// their mark, and a DLC pack's own folder a little brighter, since it is what tells two results apart.
    /// </summary>
    public static void Location(InlineCollection inlines, string location, bool selected)
    {
        inlines.Clear();
        if (location.Length == 0)
        {
            inlines.Add(new Run("Home") { Foreground = selected ? Ui.Theme.SelectionInk : Ui.Theme.TextFaint });
            return;
        }
        string[] parts = location.Split('/');
        for (int index = 0; index < parts.Length; index++)
        {
            bool archive = parts[index].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            if (index > 0)
                inlines.Add(Icon("chevron", selected ? Ui.Theme.SelectionInk : Ui.Theme.TextFaint, 9, 2, new Thickness(3, 0)));
            if (archive)
                inlines.Add(Icon("package", selected ? Ui.Theme.SelectionText : Ui.Theme.Accent, 11, 1.6, new Thickness(0, 0, 3, 0)));
            bool pack = index > 0 && string.Equals(parts[index - 1], "dlcpacks", StringComparison.OrdinalIgnoreCase);
            IBrush ink = selected ? Ui.Theme.SelectionText : archive ? Ui.Theme.Accent : pack ? Ui.Theme.TextRow : Ui.Theme.TextDim;
            inlines.Add(new Run(parts[index]) { Foreground = ink });
        }
    }

    private static InlineUIContainer Icon(string kind, IBrush ink, double size, double thickness, Thickness margin) => new(new Controls.ChromeIcon
    {
        Kind = kind, Stroke = ink, Width = size, Height = size, Thickness = thickness, Margin = margin,
    })
    { BaselineAlignment = BaselineAlignment.Center };

    /// <summary>Fills <paramref name="inlines"/> with the name, marking each of <paramref name="spans"/> in <paramref name="ink"/>.</summary>
    public static void MarkSpans(InlineCollection inlines, string name, IReadOnlyList<(int Start, int Length)> spans, IBrush ink)
    {
        inlines.Clear();
        int at = 0;
        foreach ((int start, int length) in spans.OrderBy(span => span.Start))
        {
            if (start < at || start + length > name.Length)
                continue;
            if (start > at)
                inlines.Add(new Run(name[at..start]));
            inlines.Add(new Run(name.Substring(start, length)) { Foreground = ink, FontWeight = FontWeight.Bold });
            at = start + length;
        }
        if (at < name.Length)
            inlines.Add(new Run(name[at..]));
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
