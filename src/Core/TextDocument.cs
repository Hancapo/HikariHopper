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
/// A text file read for viewing and editing: its lines as the file has them and as they are shown (tabs expanded),
/// the break after each, how it is stored (encoding, line endings) and what kind of text it is. Edits replace a
/// stretch of the file's own text (<see cref="Replace"/>); <see cref="Encode"/> writes it back as it was stored.
/// </summary>
public sealed class TextDocument
{
    /// <summary>Spaces a tab stands for when shown.</summary>
    public const int TabWidth = 4;

    /// <summary>The largest file the viewer opens; anything bigger is better read elsewhere.</summary>
    public const long MaxSize = 64L * 1024 * 1024;

    private const string Crlf = "\r\n", Lf = "\n", Cr = "\r";

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

    // The file's own lines; the shown ones, which are the same strings where a line has no tab; and the break after
    // each line, "" after the last. A file ending in a break has an empty last line, so one that doesn't stays so.
    private readonly List<string> raw;
    private readonly List<string> shown;
    private readonly List<string> breaks;
    // How many breaks of each kind there are now: CRLF, LF, CR.
    private int crlfCount, lfCount, crCount;
    // The whole text, kept until the next edit.
    private string? text;

    private TextDocument(string name, string text, List<string> raw, List<string> shown, List<string> breaks, string encoding,
        TextFormat format, long size)
    {
        Name = name;
        this.text = text;
        this.raw = raw;
        this.shown = shown;
        this.breaks = breaks;
        foreach (string lineBreak in breaks)
            Count(lineBreak, 1);
        Encoding = encoding;
        Format = format;
        Size = size;
        NewLine = MostCommonBreak();
    }

    public string Name { get; }

    /// <summary>The whole text as it is now, line endings and tabs as they are in the file.</summary>
    public string Text => text ??= Join();

    /// <summary>The lines as shown: without their endings, tabs expanded to <see cref="TabWidth"/>.</summary>
    public IReadOnlyList<string> Lines => shown;

    /// <summary>"UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE" or "Windows-1252".</summary>
    public string Encoding { get; }

    /// <summary>"CRLF", "LF", "CR", "Mixed", or "" for a single line: what the breaks are now.</summary>
    public string LineEnding => (crlfCount, lfCount, crCount) switch
    {
        (0, 0, 0) => "",
        (> 0, 0, 0) => "CRLF",
        (0, > 0, 0) => "LF",
        (0, 0, > 0) => "CR",
        _ => "Mixed",
    };

    /// <summary>
    /// The break a new line gets: the file's own, or with mixed endings the one it had most of when read (CRLF for a
    /// file of one line, as Windows writes them). Lines already there keep the break they have.
    /// </summary>
    public string NewLine { get; }

    public TextFormat Format { get; }

    /// <summary>The file's size in bytes, as read or as last saved.</summary>
    public long Size { get; private set; }

    /// <summary>The text was edited: some lines were replaced by others.</summary>
    public event EventHandler<TextChange>? Changed;

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

    /// <summary>A line as the file has it: tabs as tabs.</summary>
    public string RawLine(int line) => raw[line];

    /// <summary>
    /// The file's own characters for shown columns <paramref name="from"/> to <paramref name="to"/> of
    /// <paramref name="line"/>: tabs as tabs. A column inside a tab's spaces takes the whole tab.
    /// </summary>
    public string RawSlice(int line, int from, int to)
    {
        string own = raw[line];
        if (ReferenceEquals(own, shown[line]))
            return own[from..to];
        int start = RawIndex(own, from, roundUp: false), end = RawIndex(own, to, roundUp: true);
        return own[start..Math.Max(start, end)];
    }

    /// <summary>
    /// The index in the file's own <paramref name="line"/> of the character shown at <paramref name="column"/>: a
    /// column inside a tab's spaces is the tab's, or the next character's when <paramref name="roundUp"/>.
    /// </summary>
    public int ToRaw(int line, int column, bool roundUp = false)
    {
        string own = raw[line];
        return ReferenceEquals(own, shown[line]) ? Math.Min(column, own.Length) : RawIndex(own, column, roundUp);
    }

    /// <summary>The shown column the file's own character <paramref name="index"/> of <paramref name="line"/> starts at.</summary>
    public int ToShown(int line, int index)
    {
        string own = raw[line];
        if (ReferenceEquals(own, shown[line]))
            return Math.Min(index, own.Length);
        int column = 0;
        for (int at = 0; at < index && at < own.Length; at++)
            column = own[at] == '\t' ? column + TabWidth - column % TabWidth : column + 1;
        return column;
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

    /// <summary>The file's own text from <paramref name="start"/> to <paramref name="end"/>, each break as the file has it.</summary>
    public TextRun RawText(RawPosition start, RawPosition end)
    {
        if (start.Line == end.Line)
            return new TextRun([raw[start.Line][start.Index..end.Index]], []);
        var lines = new List<string>(end.Line - start.Line + 1) { raw[start.Line][start.Index..] };
        for (int line = start.Line + 1; line < end.Line; line++)
            lines.Add(raw[line]);
        lines.Add(raw[end.Line][..end.Index]);
        return new TextRun(lines, breaks.GetRange(start.Line, end.Line - start.Line));
    }

    /// <summary>
    /// Replaces the file's own text from <paramref name="start"/> to <paramref name="end"/> with <paramref name="run"/>,
    /// whose breaks are kept as they are; the last line keeps the break it had. Only the lines touched are made again.
    /// Returns where what was put in ends.
    /// </summary>
    public RawPosition Replace(RawPosition start, RawPosition end, TextRun run)
    {
        string prefix = raw[start.Line][..start.Index], suffix = raw[end.Line][end.Index..];
        string tail = breaks[end.Line];
        int removed = end.Line - start.Line + 1, longest = 0;
        for (int line = start.Line; line <= end.Line; line++)
        {
            longest = Math.Max(longest, shown[line].Length);
            if (line < end.Line)
                Count(breaks[line], -1);
        }
        int inserted = run.Lines.Count;
        var newRaw = new string[inserted];
        var newBreaks = new string[inserted];
        for (int index = 0; index < inserted; index++)
        {
            string line = run.Lines[index];
            if (index == 0)
                line = prefix + line;
            if (index == inserted - 1)
                line += suffix;
            newRaw[index] = line;
            newBreaks[index] = index < inserted - 1 ? Intern(run.Breaks[index]) : tail;
            if (index < inserted - 1)
                Count(newBreaks[index], 1);
        }
        string[] newShown = [.. newRaw.Select(Expand)];
        if (removed == inserted)
        {
            for (int index = 0; index < inserted; index++)
            {
                raw[start.Line + index] = newRaw[index];
                shown[start.Line + index] = newShown[index];
                breaks[start.Line + index] = newBreaks[index];
            }
        }
        else
        {
            raw.RemoveRange(start.Line, removed);
            raw.InsertRange(start.Line, newRaw);
            shown.RemoveRange(start.Line, removed);
            shown.InsertRange(start.Line, newShown);
            breaks.RemoveRange(start.Line, removed);
            breaks.InsertRange(start.Line, newBreaks);
        }
        text = null;
        Changed?.Invoke(this, new TextChange(start.Line, removed, inserted, longest));
        return run.EndFrom(start);
    }

    /// <summary>
    /// The text as it is now, in the bytes of the encoding it was read in: a byte-order mark where it had one, every
    /// line with its own break. Throws <see cref="InvalidDataException"/> for a character Windows-1252 has no byte for,
    /// rather than writing a question mark in its place.
    /// </summary>
    public byte[] Encode()
    {
        string all = Text;
        try
        {
            return Encoding switch
            {
                "UTF-8 BOM" => [0xEF, 0xBB, 0xBF, .. Strict(new UTF8Encoding(false, true), all)],
                "UTF-16 LE" => [0xFF, 0xFE, .. Strict(new UnicodeEncoding(false, false, true), all)],
                "UTF-16 BE" => [0xFE, 0xFF, .. Strict(new UnicodeEncoding(true, false, true), all)],
                "Windows-1252" => Windows1252(),
                _ => Strict(new UTF8Encoding(false, true), all),
            };
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidDataException("the text has a broken character (half of a surrogate pair)");
        }

        static byte[] Strict(System.Text.Encoding encoding, string value) => encoding.GetBytes(value);
    }

    /// <summary>The text was saved, <paramref name="size"/> bytes long.</summary>
    public void MarkSaved(long size) => Size = size;

    private byte[] Windows1252()
    {
        string all = Text;
        var bytes = new byte[all.Length];
        for (int index = 0; index < all.Length; index++)
        {
            if (Windows1252Byte(all[index]) is not { } value)
                throw Unwritable();
            bytes[index] = value;
        }
        return bytes;
    }

    /// <summary>The byte Windows-1252 writes <paramref name="value"/> as, or null for a character it has none for.</summary>
    private static byte? Windows1252Byte(char value) =>
        value < 0x80 || value >= 0xA0 && value <= 0xFF ? (byte)value
        : Array.IndexOf(Cp1252High, value) is >= 0 and var high ? (byte)(0x80 + high)
        : null;

    /// <summary>Says which character Windows-1252 can't hold, and where the first one is.</summary>
    private InvalidDataException Unwritable()
    {
        for (int line = 0; line < raw.Count; line++)
        {
            string own = raw[line];
            for (int index = 0; index < own.Length; index++)
            {
                if (Windows1252Byte(own[index]) is not null)
                    continue;
                string character = char.IsHighSurrogate(own[index]) && index + 1 < own.Length ? own.Substring(index, 2) : own[index].ToString();
                return new InvalidDataException(
                    $"Windows-1252, the file's encoding, has no “{character}” (line {line + 1}, column {ToShown(line, index) + 1})");
            }
        }
        return new InvalidDataException("Windows-1252, the file's encoding, can't hold all of its text");
    }

    private string Join()
    {
        var builder = new StringBuilder();
        for (int line = 0; line < raw.Count; line++)
            builder.Append(raw[line]).Append(breaks[line]);
        return builder.ToString();
    }

    private void Count(string lineBreak, int delta)
    {
        switch (lineBreak)
        {
            case Crlf:
                crlfCount += delta;
                break;
            case Lf:
                lfCount += delta;
                break;
            case Cr:
                crCount += delta;
                break;
        }
    }

    private string MostCommonBreak() =>
        lfCount > crlfCount && lfCount >= crCount ? Lf : crCount > crlfCount && crCount > lfCount ? Cr : Crlf;

    // The same three strings for every break, so a file's million breaks are three strings.
    private static string Intern(string lineBreak) => lineBreak switch
    {
        Crlf => Crlf,
        Lf => Lf,
        Cr => Cr,
        _ => throw new ArgumentException($"Not a line break: {lineBreak}"),
    };

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
        var raw = new List<string>();
        var shown = new List<string>();
        var breaks = new List<string>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current is not ('\r' or '\n'))
                continue;
            raw.Add(text.Substring(start, index - start));
            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                breaks.Add(Crlf);
                index++;
            }
            else
            {
                breaks.Add(current == '\r' ? Cr : Lf);
            }
            start = index + 1;
        }
        raw.Add(text[start..]);
        breaks.Add("");
        foreach (string line in raw)
            shown.Add(Expand(line));
        TextFormat format = FormatOf(name) ?? TextFormat.Plain;
        if (format == TextFormat.Plain)
            format = Sniff(text);
        return new TextDocument(name, text, raw, shown, breaks, encoding, format, data.LongLength);
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

    /// <summary>A line as shown: tabs expanded, or the very same string when it has none.</summary>
    private static string Expand(string line)
    {
        if (!line.Contains('\t'))
            return line;
        var builder = new StringBuilder(line.Length + 16);
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
