using System.Text;
using HikariHopper.Core;

namespace HikariHopper.Tests;

public sealed class TextDocumentTests
{
    private static TextDocument Load(string name, string text) => TextDocument.Load(name, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void KnowsWhichNamesAreTextAndRefusesBinary()
    {
        Assert.Equal(TextFormat.Xml, TextDocument.FormatOf("handling.meta"));
        Assert.Equal(TextFormat.Xml, TextDocument.FormatOf("WATER.XML"));
        Assert.Equal(TextFormat.Json, TextDocument.FormatOf("manifest.json"));
        Assert.Equal(TextFormat.Csv, TextDocument.FormatOf("vehicles.csv"));
        Assert.Equal(TextFormat.Ini, TextDocument.FormatOf("settings.ini"));
        Assert.Equal(TextFormat.Plain, TextDocument.FormatOf("americans.gxt2.txt"));
        Assert.Equal(TextFormat.Plain, TextDocument.FormatOf("gta.dat"));
        Assert.Null(TextDocument.FormatOf("textures.ytd"));
        Assert.Null(TextDocument.FormatOf("x64a.rpf"));
        Assert.False(TextDocument.IsTextName("strings.gxt2"));

        Assert.True(TextDocument.LooksLikeText("<CHandlingDataMgr>\r\n\t<Item />"u8));
        Assert.True(TextDocument.LooksLikeText([]));
        Assert.True(TextDocument.LooksLikeText([0xFF, 0xFE, (byte)'a', 0]));  // UTF-16, NULs and all.
        Assert.False(TextDocument.LooksLikeText([(byte)'R', (byte)'S', (byte)'C', 0x07, 0, 0, 0, 1]));
    }

    [Fact]
    public void ReadsTheEncodingAndTheLineEndings()
    {
        TextDocument crlf = Load("a.txt", "one\r\ntwo\r\nthree");
        Assert.Equal(["one", "two", "three"], crlf.Lines);
        Assert.Equal(("UTF-8", "CRLF"), (crlf.Encoding, crlf.LineEnding));
        Assert.Equal("LF", Load("a.txt", "one\ntwo\n").LineEnding);
        Assert.Equal(["one", "two", ""], Load("a.txt", "one\ntwo\n").Lines);
        Assert.Equal("Mixed", Load("a.txt", "one\r\ntwo\nthree").LineEnding);
        Assert.Equal("", Load("a.txt", "single").LineEnding);

        TextDocument bom = TextDocument.Load("a.xml", [0xEF, 0xBB, 0xBF, .. "<a/>"u8]);
        Assert.Equal(("UTF-8 BOM", "<a/>"), (bom.Encoding, bom.Text));
        TextDocument utf16 = TextDocument.Load("a.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("héllo")]);
        Assert.Equal(("UTF-16 LE", "héllo"), (utf16.Encoding, utf16.Text));
        // Not valid UTF-8: Windows-1252, where 0xE9 is é and 0x80 is €.
        TextDocument legacy = TextDocument.Load("a.txt", [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)' ', 0x80]);
        Assert.Equal(("Windows-1252", "café €"), (legacy.Encoding, legacy.Text));
    }

    [Fact]
    public void ShowsTabsAsSpacesButKeepsTheTextAsItIs()
    {
        TextDocument document = Load("a.meta", "<a>\r\n\t<b>\tx</b>\r\n</a>");
        Assert.Equal("    <b> x</b>", document.Lines[1]);
        Assert.Equal("<a>\r\n\t<b>\tx</b>\r\n</a>", document.Text);
        Assert.Equal(TextFormat.Xml, document.Format);
        Assert.Equal("XML", document.FormatName);
        Assert.Equal(document.Text.Length, document.Size);
    }

    [Fact]
    public void APlainNamedFileThatIsXmlOrJsonReadsAsThat()
    {
        Assert.Equal(TextFormat.Xml, Load("visualsettings.dat", "<?xml version=\"1.0\"?>\n<a/>").Format);
        Assert.Equal(TextFormat.Json, Load("notes.txt", "  {\"a\": 1}\n").Format);
        Assert.Equal(TextFormat.Plain, Load("gta.dat", "# comment\nIMG platform:/x").Format);
    }

    private static List<(string Text, TextKind Kind)> Kinds(TextFormat format, string line, byte state = TextHighlighter.Start) =>
        [.. TextHighlighter.Spans(format, line, state, out _).Select(span => (line.Substring(span.Start, span.Length), span.Kind))];

    [Fact]
    public void ColoursXml()
    {
        Assert.Equal(
            [("<", TextKind.Punctuation), ("fMass", TextKind.Tag), ("value", TextKind.Attribute), ("=", TextKind.Punctuation),
             ("\"1800.0\"", TextKind.String), ("/>", TextKind.Punctuation)],
            Kinds(TextFormat.Xml, "  <fMass value=\"1800.0\" />"));
        Assert.Equal(
            [("<", TextKind.Punctuation), ("handlingName", TextKind.Tag), (">", TextKind.Punctuation), ("ADDER", TextKind.Plain),
             ("</", TextKind.Punctuation), ("handlingName", TextKind.Tag), (">", TextKind.Punctuation)],
            Kinds(TextFormat.Xml, "<handlingName>ADDER</handlingName>"));
        Assert.Contains(("440010", TextKind.Number), Kinds(TextFormat.Xml, "<strModelFlags>440010</strModelFlags>"));

        // A comment and a tag can run over several lines; the state carries them.
        string[] lines = ["<!-- one", "two -->", "<Item", "  type=\"CHandlingData\">", "<a/>"];
        byte[] states = TextHighlighter.States(TextFormat.Xml, lines);
        Assert.Equal([("two -->", TextKind.Comment)], Kinds(TextFormat.Xml, lines[1], states[1]));
        Assert.Equal([("type", TextKind.Attribute), ("=", TextKind.Punctuation), ("\"CHandlingData\"", TextKind.String), (">", TextKind.Punctuation)],
            Kinds(TextFormat.Xml, lines[3], states[3]));
        Assert.Equal(TextHighlighter.Start, states[4]);
    }

    [Fact]
    public void ColoursJsonKeysApartFromStrings()
    {
        Assert.Equal(
            [("\"name\"", TextKind.Key), (":", TextKind.Punctuation), ("\"Lights\"", TextKind.String), (",", TextKind.Punctuation)],
            Kinds(TextFormat.Json, "  \"name\": \"Lights\","));
        Assert.Contains(("40", TextKind.Number), Kinds(TextFormat.Json, "\"priority\": 40,"));
        Assert.Contains(("true", TextKind.Literal), Kinds(TextFormat.Json, "\"on\": true"));
        Assert.Contains(("\"a \\\"b\\\"\"", TextKind.String), Kinds(TextFormat.Json, "[\"a \\\"b\\\"\"]"));
    }

    [Fact]
    public void ColoursIniAndLua()
    {
        Assert.Equal([("[Graphics]", TextKind.Tag)], Kinds(TextFormat.Ini, "[Graphics]"));
        Assert.Equal([("Width", TextKind.Attribute), ("=", TextKind.Punctuation), ("1920", TextKind.Number)], Kinds(TextFormat.Ini, "Width=1920"));
        Assert.Equal([("; note", TextKind.Comment)], Kinds(TextFormat.Ini, "; note"));
        Assert.Contains(("local", TextKind.Literal), Kinds(TextFormat.Lua, "local x = 'a' -- note"));
        Assert.Contains(("-- note", TextKind.Comment), Kinds(TextFormat.Lua, "local x = 'a' -- note"));
    }

    [Fact]
    public void ParsesCsvWithQuotesAndOtherSeparators()
    {
        CsvTable table = CsvTable.Parse("model,make,notes\r\nadder,\"Truffade, FR\",\"said \"\"fast\"\"\"\r\nblista,Dinka,\"two\nlines\"\r\nzentorno,Pegassi\r\n");
        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(3, table.Columns);
        Assert.Equal(["adder", "Truffade, FR", "said \"fast\""], table.Rows[1]);
        Assert.Equal("two\nlines", table.Rows[2][2]);
        Assert.Equal(["zentorno", "Pegassi"], table.Rows[3]);
        Assert.Equal([0, 1, 2, 4], table.LineOfRow);

        Assert.Equal(';', CsvTable.SeparatorOf("a;b;c"));
        Assert.Equal('\t', CsvTable.SeparatorOf("a\tb\tc"));
        Assert.Equal(',', CsvTable.SeparatorOf("\"a;b\",c"));
        Assert.Equal(["1", "2"], CsvTable.Parse("a;b\n1;2").Rows[1]);
    }
}
