using System.Text;

namespace HikariHopper.Core;

/// <summary>
/// A place in a <see cref="TextDocument"/>'s own text: a line, and an index into that line's characters as the file
/// has them (a tab is one character), both from 0.
/// </summary>
public readonly record struct RawPosition(int Line, int Index) : IComparable<RawPosition>
{
    public int CompareTo(RawPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Index.CompareTo(other.Index);
}

/// <summary>
/// A stretch of text as lines and the breaks between them, each break as written ("\r\n", "\n" or "\r"). Kept apart
/// rather than joined into one string, so a stretch taken out of a file goes back with exactly the breaks it had: a
/// lone "\r" followed by an empty "\n" line would read back as one "\r\n" from a string.
/// </summary>
public sealed class TextRun
{
    /// <summary>Nothing: one empty line and no breaks.</summary>
    public static readonly TextRun Empty = new([""], []);

    public TextRun(IReadOnlyList<string> lines, IReadOnlyList<string> breaks)
    {
        if (lines.Count == 0 || breaks.Count != lines.Count - 1)
            throw new ArgumentException("A run has one more line than it has breaks.");
        Lines = lines;
        Breaks = breaks;
    }

    /// <summary>The lines, without their breaks; at least one, which may be empty.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The break after each line but the last.</summary>
    public IReadOnlyList<string> Breaks { get; }

    public bool IsEmpty => Lines.Count == 1 && Lines[0].Length == 0;

    /// <summary>
    /// <paramref name="text"/> split at its line breaks, whichever kind each is, every one written as
    /// <paramref name="newline"/>: what is typed or pasted takes the file's own line ending.
    /// </summary>
    public static TextRun Of(string text, string newline)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0)
            return new TextRun([text], []);
        var lines = new List<string>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current is not ('\r' or '\n'))
                continue;
            lines.Add(text[start..index]);
            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            start = index + 1;
        }
        lines.Add(text[start..]);
        return new TextRun(lines, Enumerable.Repeat(newline, lines.Count - 1).ToArray());
    }

    /// <summary>This run with <paramref name="next"/> straight after it, the one's last line joining the other's first.</summary>
    public TextRun Then(TextRun next)
    {
        if (next.IsEmpty)
            return this;
        if (IsEmpty)
            return next;
        var lines = new List<string>(Lines.Count + next.Lines.Count - 1);
        lines.AddRange(Lines.Take(Lines.Count - 1));
        lines.Add(Lines[^1] + next.Lines[0]);
        lines.AddRange(next.Lines.Skip(1));
        return new TextRun(lines, [.. Breaks, .. next.Breaks]);
    }

    /// <summary>Where the run ends once put in at <paramref name="start"/>.</summary>
    public RawPosition EndFrom(RawPosition start) => Lines.Count == 1
        ? start with { Index = start.Index + Lines[0].Length }
        : new RawPosition(start.Line + Lines.Count - 1, Lines[^1].Length);

    /// <summary>The run as one string, with its own breaks.</summary>
    public override string ToString()
    {
        if (Lines.Count == 1)
            return Lines[0];
        var builder = new StringBuilder();
        for (int index = 0; index < Lines.Count; index++)
        {
            builder.Append(Lines[index]);
            if (index < Breaks.Count)
                builder.Append(Breaks[index]);
        }
        return builder.ToString();
    }
}

/// <summary>
/// What an edit changed, in the document's lines: from <see cref="Line"/>, <see cref="RemovedLines"/> lines were
/// replaced by <see cref="InsertedLines"/>; the longest shown line taken out was <see cref="RemovedLongest"/> long.
/// </summary>
public readonly record struct TextChange(int Line, int RemovedLines, int InsertedLines, int RemovedLongest);
