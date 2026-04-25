using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parses_SimpleRows()
    {
        using var sr = new StringReader("a,b,c\n1,2,3\n");
        var rows = CsvParser.Parse(sr);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void HandlesQuotedFields_WithEmbeddedCommaAndEscapedQuote()
    {
        using var sr = new StringReader("h1,h2\n\"a,b\",\"He said \"\"hi\"\"\"\n");
        var rows = CsvParser.Parse(sr);
        Assert.Equal(2, rows[1].Count);
        Assert.Equal("a,b", rows[1][0]);
        Assert.Equal("He said \"hi\"", rows[1][1]);
    }

    [Fact]
    public void StripsBom_OnFirstField()
    {
        using var sr = new StringReader("\uFEFFa,b\n1,2\n");
        var rows = CsvParser.Parse(sr);
        Assert.Equal("a", rows[0][0]);
    }

    [Fact]
    public void HandlesCrlfLineEndings()
    {
        using var sr = new StringReader("a,b\r\n1,2\r\n");
        var rows = CsvParser.Parse(sr);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void HandlesTrailingLineWithoutNewline()
    {
        using var sr = new StringReader("a,b\n1,2");
        var rows = CsvParser.Parse(sr);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void EscapeField_AddsQuotes_WhenNeeded()
    {
        Assert.Equal("plain", CsvParser.EscapeField("plain"));
        Assert.Equal("\"a,b\"", CsvParser.EscapeField("a,b"));
        Assert.Equal("\"a\"\"b\"", CsvParser.EscapeField("a\"b"));
    }

    [Fact]
    public void RoundTrip_PreservesAwkwardFields()
    {
        var rows = new[]
        {
            new[] { "h1", "h2", "h3" },
            new[] { "中文", "with,comma", "with\"quote\"" }
        };
        var sb = new System.IO.StringWriter();
        CsvParser.Write(sb, rows);
        using var sr = new StringReader(sb.ToString());
        var parsed = CsvParser.Parse(sr);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(["中文", "with,comma", "with\"quote\""], parsed[1]);
    }
}
