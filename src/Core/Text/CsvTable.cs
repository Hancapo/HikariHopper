namespace HikariHopper.Core;

/// <summary>
/// A CSV or TSV file read as a table: its rows of fields, quoted fields unquoted. The separator is
/// whichever of comma, semicolon or tab the first line uses most.
/// </summary>
public sealed class CsvTable
{
    private CsvTable(IReadOnlyList<string[]> rows, int columns, int[] lineOfRow, char separator)
    {
        Rows = rows;
        Columns = columns;
        LineOfRow = lineOfRow;
        Separator = separator;
    }

    /// <summary>The separator the file was split on: comma, semicolon or tab.</summary>
    public char Separator { get; }

    /// <summary>Every row, the first being the header. Short rows are not padded.</summary>
    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>The widest row's field count.</summary>
    public int Columns { get; }

    /// <summary>The line (from 0) each row starts on; a quoted field can run over several.</summary>
    public int[] LineOfRow { get; }

    /// <summary>
    /// The separator <paramref name="line"/> uses: the one of , ; tab it has most of, outside quoted fields.
    /// A quote opens a quoted field only where a field starts, as <see cref="Parse"/> reads it; anywhere
    /// else (5" for inches) it is just a character.
    /// </summary>
    public static char SeparatorOf(string line)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        Scan(line, stopAtLineBreak: false, separator =>
        {
            if (separator == ',')
                commas++;
            else if (separator == ';')
                semicolons++;
            else
                tabs++;
        });
        return tabs > commas && tabs >= semicolons ? '\t' : semicolons > commas ? ';' : ',';
    }

    /// <summary>The first record of <paramref name="text"/>: up to its first line break outside a quoted field.</summary>
    private static string FirstRecord(string text) => text[..Scan(text, stopAtLineBreak: true, _ => { })];

    /// <summary>
    /// Walks <paramref name="text"/> the way the parser does, telling each separator candidate (, ; tab) met
    /// outside a quoted field; returns where it stopped.
    /// </summary>
    private static int Scan(string text, bool stopAtLineBreak, Action<char> separator)
    {
        bool quoted = false, fieldStart = true;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quoted)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                        index++;
                    else
                        quoted = false;
                }
                continue;
            }
            if (current == '"' && fieldStart)
            {
                quoted = true;
                fieldStart = false;
            }
            else if (current is ',' or ';' or '\t')
            {
                separator(current);
                fieldStart = true;
            }
            else if (current is '\r' or '\n')
            {
                if (stopAtLineBreak)
                    return index;
                fieldStart = true;
            }
            else
            {
                fieldStart = false;
            }
        }
        return text.Length;
    }

    /// <summary>Reads <paramref name="text"/>, the whole file, into rows.</summary>
    public static CsvTable Parse(string text)
    {
        char separator = SeparatorOf(FirstRecord(text));
        var rows = new List<string[]>();
        var lines = new List<int>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        int line = 0, rowLine = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quoted)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (current == '\n' || (current == '\r' && !(index + 1 < text.Length && text[index + 1] == '\n')))
                        line++;
                    field.Append(current);
                }
                continue;
            }
            if (current == '"' && field.Length == 0)
            {
                quoted = true;
                any = true;
            }
            else if (current == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
                any = true;
            }
            else if (current is '\r' or '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                if (any || field.Length > 0)
                {
                    fields.Add(field.ToString());
                    rows.Add([.. fields]);
                    lines.Add(rowLine);
                }
                fields.Clear();
                field.Clear();
                any = false;
                line++;
                rowLine = line;
            }
            else
            {
                field.Append(current);
                any = true;
            }
        }
        if (any || field.Length > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
            lines.Add(rowLine);
        }
        return new CsvTable(rows, rows.Count == 0 ? 0 : rows.Max(row => row.Length), [.. lines], separator);
    }
}
