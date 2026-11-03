using System.Text;

namespace HikariHopper.Core;

/// <summary>How a text file is coloured, and whether it can be shown as a table.</summary>
public enum TextFormat
{
    Plain,
    Xml,
    Json,
    Csv,
    Ini,
    Lua,
}

/// <summary>
/// A text file read for viewing: its decoded text, its lines as they are shown (tabs expanded), how it is
/// stored (encoding, line endings) and what kind of text it is.
/// </summary>
public sealed class TextDocument
{
    /// <summary>Spaces a tab stands for when shown.</summary>
    public const int TabWidth = 4;

    /// <summary>The largest file the viewer opens; anything bigger is better read elsewhere.</summary>
    public const long MaxSize = 64L * 1024 * 1024;

    // What each name ends in, and how it reads. Anything else is not opened as text.
    private static readonly Dictionary<string, TextFormat> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = TextFormat.Plain,
        [".log"] = TextFormat.Plain,
        [".md"] = TextFormat.Plain,
        [".dat"] = TextFormat.Plain,
        [".ide"] = TextFormat.Plain,
        [".ipl"] = TextFormat.Plain,
        [".gxt2.txt"] = TextFormat.Plain,
        [".xml"] = TextFormat.Xml,
        [".meta"] = TextFormat.Xml,
        [".json"] = TextFormat.Json,
        [".csv"] = TextFormat.Csv,
        [".tsv"] = TextFormat.Csv,
        [".ini"] = TextFormat.Ini,
        [".cfg"] = TextFormat.Ini,
        [".toml"] = TextFormat.Ini,
        [".lua"] = TextFormat.Lua,
    };

    // The file's own lines where they differ from the shown ones (they had tabs), by line; null elsewhere.
    private readonly Dictionary<int, string> rawLines;

    private TextDocument(string name, string text, IReadOnlyList<string> lines, Dictionary<int, string> rawLines, string encoding,
        string lineEnding, TextFormat format, long size)
    {
        Name = name;
        Text = text;
        Lines = lines;
        this.rawLines = rawLines;
        Encoding = encoding;
        LineEnding = lineEnding;
        Format = format;
        Size = size;
    }

    public string Name { get; }

    /// <summary>The whole text as decoded, line endings and tabs as they are in the file.</summary>
    public string Text { get; }

    /// <summary>The lines as shown: without their endings, tabs expanded to <see cref="TabWidth"/>.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>"UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE" or "Windows-1252".</summary>
    public string Encoding { get; }

    /// <summary>"CRLF", "LF", "CR", "Mixed", or "" for a single line.</summary>
    public string LineEnding { get; }

    public TextFormat Format { get; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; }

    /// <summary>The format's short name, as the status bar shows it.</summary>
    public string FormatName => Format switch
    {
        TextFormat.Xml => "XML",
        TextFormat.Json => "JSON",
        TextFormat.Csv => "CSV",
        TextFormat.Ini => "INI",
        TextFormat.Lua => "LUA",
        _ => "TEXT",
    };

    /// <summary>
    /// The file's own characters for shown columns <paramref name="from"/> to <paramref name="to"/> of
    /// <paramref name="line"/>: tabs as tabs. A column inside a tab's spaces takes the whole tab.
    /// </summary>
    public string RawSlice(int line, int from, int to)
    {
        if (!rawLines.TryGetValue(line, out string? raw))
            return Lines[line][from..to];
        int start = RawIndex(raw, from, roundUp: false), end = RawIndex(raw, to, roundUp: true);
        return raw[start..Math.Max(start, end)];
    }

    /// <summary>Where shown <paramref name="column"/> falls in <paramref name="raw"/>, its tabs expanded.</summary>
    private static int RawIndex(string raw, int column, bool roundUp)
    {
        int shown = 0;
        for (int index = 0; index < raw.Length; index++)
        {
            int next = raw[index] == '\t' ? shown + TabWidth - shown % TabWidth : shown + 1;
            if (column < next)
                return column == shown || !roundUp ? index : index + 1;
            shown = next;
        }
        return raw.Length;
    }

    /// <summary>Whether <paramref name="name"/> is a kind of file the viewer opens.</summary>
    public static bool IsTextName(string name) => FormatOf(name) is not null;

    /// <summary>The format <paramref name="name"/>'s extension stands for, or null for a file that isn't text.</summary>
    public static TextFormat? FormatOf(string name)
    {
        // Longest first, so "strings.gxt2.txt" is read as the GXT text dump it is.
        foreach ((string extension, TextFormat format) in Extensions.OrderByDescending(pair => pair.Key.Length))
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return format;
        return null;
    }

    /// <summary>
    /// Whether <paramref name="data"/> reads as text: a byte-order mark, or no NUL bytes and hardly any other
    /// control bytes in its first 8 KB.
    /// </summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        if (Bom(data) is not null)
            return true;
        ReadOnlySpan<byte> head = data[..Math.Min(data.Length, 8192)];
        int control = 0;
        foreach (byte value in head)
        {
            if (value == 0)
                return false;
            if (value < 0x20 && value is not ((byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C or 0x1A))
                control++;
        }
        return head.Length == 0 || control * 100 <= head.Length;
    }

    /// <summary>Reads <paramref name="data"/>, the bytes of the file called <paramref name="name"/>.</summary>
    public static TextDocument Load(string name, byte[] data)
    {
        (string text, string encoding) = Decode(data);
        var lines = new List<string>();
        var rawLines = new Dictionary<int, string>();
        int crlf = 0, lf = 0, cr = 0;
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current is not ('\r' or '\n'))
                continue;
            AddLine(text, start, index - start, lines, rawLines);
            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                crlf++;
                index++;
            }
            else if (current == '\r')
            {
                cr++;
            }
            else
            {
                lf++;
            }
            start = index + 1;
        }
        AddLine(text, start, text.Length - start, lines, rawLines);
        string ending = (crlf, lf, cr) switch
        {
            (0, 0, 0) => "",
            (> 0, 0, 0) => "CRLF",
            (0, > 0, 0) => "LF",
            (0, 0, > 0) => "CR",
            _ => "Mixed",
        };
        TextFormat format = FormatOf(name) ?? TextFormat.Plain;
        if (format == TextFormat.Plain)
            format = Sniff(text);
        return new TextDocument(name, text, lines, rawLines, encoding, ending, format, data.LongLength);
    }

    /// <summary>A plain-named file that is really XML or JSON reads as that.</summary>
    private static TextFormat Sniff(string text)
    {
        ReadOnlySpan<char> start = text.AsSpan().TrimStart();
        if (start.StartsWith("<?xml") || (start.StartsWith("<") && start.Length > 1 && char.IsLetter(start[1])))
            return TextFormat.Xml;
        if ((start.StartsWith("{") || start.StartsWith("[")) && text.AsSpan().TrimEnd() is [.., '}' or ']'])
            return TextFormat.Json;
        return TextFormat.Plain;
    }

    private static string? Bom(ReadOnlySpan<byte> data) => data switch
    {
        [0xEF, 0xBB, 0xBF, ..] => "UTF-8 BOM",
        [0xFF, 0xFE, ..] => "UTF-16 LE",
        [0xFE, 0xFF, ..] => "UTF-16 BE",
        _ => null,
    };

    /// <summary>
    /// The text and how it was stored: by its byte-order mark, else UTF-8 when it is valid UTF-8, else
    /// Windows-1252, which every byte decodes in.
    /// </summary>
    private static (string Text, string Encoding) Decode(byte[] data)
    {
        switch (Bom(data))
        {
            case "UTF-8 BOM":
                return (new UTF8Encoding(false).GetString(data, 3, data.Length - 3), "UTF-8 BOM");
            case "UTF-16 LE":
                return (System.Text.Encoding.Unicode.GetString(data, 2, data.Length - 2), "UTF-16 LE");
            case "UTF-16 BE":
                return (System.Text.Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2), "UTF-16 BE");
        }
        try
        {
            return (new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Latin1Windows(data), "Windows-1252");
        }
    }

    // Windows-1252's 0x80-0x9F; everything else maps to the same code point (Latin-1).
    private static readonly char[] Cp1252High =
    [
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
    ];

    private static string Latin1Windows(byte[] data)
    {
        // Built in, so no code page provider is needed in the native build.
        return string.Create(data.Length, data, static (chars, bytes) =>
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                chars[index] = value is >= 0x80 and <= 0x9F ? Cp1252High[value - 0x80] : (char)value;
            }
        });
    }

    /// <summary>Adds a line as shown, and keeps the file's own when showing it changed it.</summary>
    private static void AddLine(string text, int start, int length, List<string> lines, Dictionary<int, string> rawLines)
    {
        string shown = ExpandTabs(text, start, length);
        if (shown.Length != length)
            rawLines[lines.Count] = text.Substring(start, length);
        lines.Add(shown);
    }

    private static string ExpandTabs(string text, int start, int length)
    {
        ReadOnlySpan<char> line = text.AsSpan(start, length);
        if (!line.Contains('\t'))
            return line.ToString();
        var builder = new StringBuilder(length + 16);
        foreach (char current in line)
        {
            if (current == '\t')
                builder.Append(' ', TabWidth - builder.Length % TabWidth);
            else
                builder.Append(current);
        }
        return builder.ToString();
    }
}
