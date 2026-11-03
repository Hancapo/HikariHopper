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

    /// <summary>The separator <paramref name="line"/> uses: the one of , ; tab it has most of, outside quotes.</summary>
    public static char SeparatorOf(string line)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        bool quoted = false;
        foreach (char current in line)
        {
            if (current == '"')
                quoted = !quoted;
            else if (!quoted)
            {
                if (current == ',')
                    commas++;
                else if (current == ';')
                    semicolons++;
                else if (current == '\t')
                    tabs++;
            }
        }
        return tabs > commas && tabs >= semicolons ? '\t' : semicolons > commas ? ';' : ',';
    }

    /// <summary>Reads <paramref name="text"/>, the whole file, into rows.</summary>
    public static CsvTable Parse(string text)
    {
        int firstEnd = text.IndexOfAny(['\r', '\n']);
        char separator = SeparatorOf(firstEnd < 0 ? text : text[..firstEnd]);
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
