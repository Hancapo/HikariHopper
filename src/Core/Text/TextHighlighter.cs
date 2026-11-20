namespace HikariHopper.Core;

/// <summary>What a stretch of text is, which decides its ink.</summary>
public enum TextKind : byte
{
    Plain,
    Tag,
    Attribute,
    Key,
    String,
    Number,
    Literal,
    Punctuation,
    Comment,
}

/// <summary>A stretch of one line: where it starts, how long it is and what it is.</summary>
public readonly record struct TextSpan(int Start, int Length, TextKind Kind);

/// <summary>
/// Colours text a line at a time, for <see cref="TextFormat"/>s the viewer knows. What carries from one
/// line to the next (an open XML comment, a tag spread over lines, a Lua block comment) is a small state,
/// so any line can be coloured once the state it starts in is known; <see cref="States"/> works those out
/// for a whole document in one pass.
/// </summary>
public static class TextHighlighter
{
    // Line states. Plain text, JSON, CSV and INI carry nothing between lines.
    public const byte Start = 0;
    private const byte XmlComment = 1;
    private const byte XmlInTag = 2;
    private const byte XmlCData = 3;
    private const byte LuaComment = 4;

    /// <summary>The state each line starts in: <c>[0]</c> is <see cref="Start"/>.</summary>
    public static byte[] States(TextFormat format, IReadOnlyList<string> lines)
    {
        var states = new byte[lines.Count];
        byte state = Start;
        for (int index = 0; index < lines.Count; index++)
        {
            states[index] = state;
            state = Advance(format, lines[index], state);
        }
        return states;
    }

    /// <summary>The state <paramref name="line"/> leaves off in, read without keeping any stretches.</summary>
    public static byte Advance(TextFormat format, string line, byte state) => format switch
    {
        TextFormat.Xml => Xml(line, state, null),
        TextFormat.Lua => Lua(line, state, null),
        _ => Start,
    };

    /// <summary>The coloured stretches of <paramref name="line"/>, starting in <paramref name="state"/>.</summary>
    /// <summary>
    /// Lines longer than this are not coloured, as code editors do: a minified file's one huge line would
    /// cost seconds and millions of spans, mostly off screen. They carry their state on unchanged.
    /// </summary>
    public const int MaxColouredLength = 20_000;

    public static List<TextSpan> Spans(TextFormat format, string line, byte state, out byte next)
    {
        var spans = new List<TextSpan>();
        next = Start;
        if (line.Length > MaxColouredLength)
        {
            // Not coloured, but read for where it leaves off: a comment or a tag may end in it.
            next = Advance(format, line, state);
            return spans;
        }
        switch (format)
        {
            case TextFormat.Xml:
                next = Xml(line, state, spans);
                break;
            case TextFormat.Json:
                Json(line, spans);
                break;
            case TextFormat.Csv:
                Csv(line, spans);
                break;
            case TextFormat.Ini:
                Ini(line, spans);
                break;
            case TextFormat.Lua:
                next = Lua(line, state, spans);
                break;
        }
        return spans;
    }

    private static byte Xml(string line, byte state, List<TextSpan>? spans)
    {
        int index = 0;
        while (index < line.Length)
        {
            if (state == XmlComment)
            {
                int end = line.IndexOf("-->", index, StringComparison.Ordinal);
                int stop = end < 0 ? line.Length : end + 3;
                spans?.Add(new TextSpan(index, stop - index, TextKind.Comment));
                index = stop;
                if (end >= 0)
                    state = Start;
                continue;
            }
            if (state == XmlCData)
            {
                int end = line.IndexOf("]]>", index, StringComparison.Ordinal);
                int stop = end < 0 ? line.Length : end + 3;
                spans?.Add(new TextSpan(index, stop - index, TextKind.String));
                index = stop;
                if (end >= 0)
                    state = Start;
                continue;
            }
            if (state == XmlInTag)
            {
                index = XmlTagBody(line, index, spans, out bool closed);
                if (closed)
                    state = Start;
                continue;
            }
            // Between tags: text, until the next "<".
            if (line[index] != '<')
            {
                int end = line.IndexOf('<', index);
                int stop = end < 0 ? line.Length : end;
                XmlText(line, index, stop, spans);
                index = stop;
                continue;
            }
            if (string.CompareOrdinal(line, index, "<!--", 0, 4) == 0)
            {
                state = XmlComment;
                continue;
            }
            if (string.CompareOrdinal(line, index, "<![CDATA[", 0, 9) == 0)
            {
                spans?.Add(new TextSpan(index, 9, TextKind.Punctuation));
                index += 9;
                state = XmlCData;
                continue;
            }
            // "<", "</", "<?" or "<!", then the name.
            int open = index + 1;
            if (open < line.Length && line[open] is '/' or '?' or '!')
                open++;
            spans?.Add(new TextSpan(index, open - index, TextKind.Punctuation));
            int name = open;
            while (name < line.Length && IsNameChar(line[name]))
                name++;
            if (name > open)
                spans?.Add(new TextSpan(open, name - open, TextKind.Tag));
            index = XmlTagBody(line, name, spans, out bool done);
            state = done ? Start : XmlInTag;
        }
        return state;
    }

    /// <summary>Inside a tag: attributes and their values, up to the closing "&gt;", "/&gt;" or "?&gt;".</summary>
    private static int XmlTagBody(string line, int index, List<TextSpan>? spans, out bool closed)
    {
        closed = false;
        while (index < line.Length)
        {
            char current = line[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '>' || ((current is '/' or '?') && index + 1 < line.Length && line[index + 1] == '>'))
            {
                int length = current == '>' ? 1 : 2;
                spans?.Add(new TextSpan(index, length, TextKind.Punctuation));
                closed = true;
                return index + length;
            }
            if (current is '"' or '\'')
            {
                int end = line.IndexOf(current, index + 1);
                int stop = end < 0 ? line.Length : end + 1;
                spans?.Add(new TextSpan(index, stop - index, TextKind.String));
                index = stop;
                continue;
            }
            if (current == '=')
            {
                spans?.Add(new TextSpan(index, 1, TextKind.Punctuation));
                index++;
                continue;
            }
            int start = index;
            while (index < line.Length && IsNameChar(line[index]))
                index++;
            if (index == start)
                index++;
            spans?.Add(new TextSpan(start, index - start, TextKind.Attribute));
        }
        return index;
    }

    /// <summary>Element text: a number reads as one; anything else is plain.</summary>
    private static void XmlText(string line, int start, int stop, List<TextSpan>? spans)
    {
        ReadOnlySpan<char> text = line.AsSpan(start, stop - start);
        ReadOnlySpan<char> trimmed = text.Trim();
        if (trimmed.Length > 0 && IsNumber(trimmed))
        {
            int at = start + text.IndexOf(trimmed);
            spans?.Add(new TextSpan(at, trimmed.Length, TextKind.Number));
        }
        else if (trimmed.Length > 0)
        {
            spans?.Add(new TextSpan(start, stop - start, TextKind.Plain));
        }
    }

    private static void Json(string line, List<TextSpan>? spans)
    {
        int index = 0;
        while (index < line.Length)
        {
            char current = line[index];
            if (current == '"')
            {
                int end = index + 1;
                while (end < line.Length && line[end] != '"')
                    end += line[end] == '\\' ? 2 : 1;
                int stop = Math.Min(line.Length, end + 1);
                // A string followed by a colon is a key.
                int after = stop;
                while (after < line.Length && char.IsWhiteSpace(line[after]))
                    after++;
                spans?.Add(new TextSpan(index, stop - index, after < line.Length && line[after] == ':' ? TextKind.Key : TextKind.String));
                index = stop;
            }
            else if (current is '{' or '}' or '[' or ']' or ',' or ':')
            {
                spans?.Add(new TextSpan(index, 1, TextKind.Punctuation));
                index++;
            }
            else if (current == '-' || char.IsAsciiDigit(current))
            {
                int end = index + 1;
                while (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] is '.' or 'e' or 'E' or '+' or '-'))
                    end++;
                spans?.Add(new TextSpan(index, end - index, TextKind.Number));
                index = end;
            }
            else if (char.IsAsciiLetter(current))
            {
                int end = index;
                while (end < line.Length && char.IsAsciiLetter(line[end]))
                    end++;
                string word = line[index..end];
                spans?.Add(new TextSpan(index, end - index, word is "true" or "false" or "null" ? TextKind.Literal : TextKind.Plain));
                index = end;
            }
            else
            {
                index++;
            }
        }
    }

    private static void Csv(string line, List<TextSpan>? spans)
    {
        char separator = CsvTable.SeparatorOf(line);
        int index = 0;
        while (index <= line.Length)
        {
            int end = index;
            bool quoted = end < line.Length && line[end] == '"';
            if (quoted)
            {
                end++;
                while (end < line.Length && !(line[end] == '"' && (end + 1 >= line.Length || line[end + 1] != '"')))
                    end += line[end] == '"' ? 2 : 1;
                end = Math.Min(line.Length, end + 1);
            }
            else
            {
                while (end < line.Length && line[end] != separator)
                    end++;
            }
            if (end > index)
            {
                ReadOnlySpan<char> field = line.AsSpan(index, end - index).Trim();
                spans?.Add(new TextSpan(index, end - index, quoted ? TextKind.String : IsNumber(field) ? TextKind.Number : TextKind.Plain));
            }
            if (end < line.Length)
                spans?.Add(new TextSpan(end, 1, TextKind.Punctuation));
            index = end + 1;
        }
    }

    private static void Ini(string line, List<TextSpan>? spans)
    {
        ReadOnlySpan<char> text = line.AsSpan();
        int first = 0;
        while (first < text.Length && char.IsWhiteSpace(text[first]))
            first++;
        if (first >= text.Length)
            return;
        if (text[first] is ';' or '#')
        {
            spans?.Add(new TextSpan(first, text.Length - first, TextKind.Comment));
            return;
        }
        if (text[first] == '[')
        {
            spans?.Add(new TextSpan(first, text.Length - first, TextKind.Tag));
            return;
        }
        int equals = line.IndexOfAny(['=', ':'], first);
        if (equals < 0)
        {
            spans?.Add(new TextSpan(first, text.Length - first, TextKind.Plain));
            return;
        }
        spans?.Add(new TextSpan(first, equals - first, TextKind.Attribute));
        spans?.Add(new TextSpan(equals, 1, TextKind.Punctuation));
        ReadOnlySpan<char> value = text[(equals + 1)..];
        ReadOnlySpan<char> trimmed = value.Trim();
        if (trimmed.Length == 0)
            return;
        int at = equals + 1 + value.IndexOf(trimmed);
        TextKind kind = IsNumber(trimmed) ? TextKind.Number
            : trimmed is "true" or "false" or "on" or "off" or "yes" or "no" ? TextKind.Literal
            : trimmed[0] is '"' or '\'' ? TextKind.String
            : TextKind.Plain;
        spans?.Add(new TextSpan(at, trimmed.Length, kind));
    }

    private static readonly HashSet<string> LuaWords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    ];

    private static byte Lua(string line, byte state, List<TextSpan>? spans)
    {
        int index = 0;
        if (state == LuaComment)
        {
            int end = line.IndexOf("]]", StringComparison.Ordinal);
            int stop = end < 0 ? line.Length : end + 2;
            spans?.Add(new TextSpan(0, stop, TextKind.Comment));
            if (end < 0)
                return LuaComment;
            index = stop;
        }
        while (index < line.Length)
        {
            char current = line[index];
            if (current == '-' && index + 1 < line.Length && line[index + 1] == '-')
            {
                if (string.CompareOrdinal(line, index + 2, "[[", 0, 2) == 0)
                {
                    int end = line.IndexOf("]]", index + 4, StringComparison.Ordinal);
                    int stop = end < 0 ? line.Length : end + 2;
                    spans?.Add(new TextSpan(index, stop - index, TextKind.Comment));
                    if (end < 0)
                        return LuaComment;
                    index = stop;
                    continue;
                }
                spans?.Add(new TextSpan(index, line.Length - index, TextKind.Comment));
                return Start;
            }
            if (current is '"' or '\'')
            {
                int end = index + 1;
                while (end < line.Length && line[end] != current)
                    end += line[end] == '\\' ? 2 : 1;
                int stop = Math.Min(line.Length, end + 1);
                spans?.Add(new TextSpan(index, stop - index, TextKind.String));
                index = stop;
            }
            else if (char.IsAsciiDigit(current))
            {
                int end = index + 1;
                while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '.'))
                    end++;
                spans?.Add(new TextSpan(index, end - index, TextKind.Number));
                index = end;
            }
            else if (char.IsAsciiLetter(current) || current == '_')
            {
                int end = index + 1;
                while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;
                string word = line[index..end];
                spans?.Add(new TextSpan(index, end - index, LuaWords.Contains(word) ? TextKind.Literal : TextKind.Plain));
                index = end;
            }
            else
            {
                if (!char.IsWhiteSpace(current))
                    spans?.Add(new TextSpan(index, 1, TextKind.Punctuation));
                index++;
            }
        }
        return Start;
    }

    private static bool IsNameChar(char value) => char.IsLetterOrDigit(value) || value is '_' or '-' or '.' or ':';

    private static bool IsNumber(ReadOnlySpan<char> text) =>
        text.Length > 0 && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
        && (char.IsAsciiDigit(text[0]) || text[0] is '-' or '+' or '.');
}
