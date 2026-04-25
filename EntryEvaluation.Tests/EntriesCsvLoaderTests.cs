using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class EntriesCsvLoaderTests
{
    [Fact]
    public void LoadFromText_ReadsAllEntries()
    {
        var Entries = TestFixture.LoadSampleEntries();
        Assert.Equal(3, Entries.Count);
        Assert.Contains("经侦万象", Entries[0].Name);
    }

    [Fact]
    public void LoadFromText_RejectsBadHeader()
    {
        Assert.Throws<InvalidDataException>(() =>
            EntriesCsvLoader.LoadFromText("Bad,Header\n1,2\n"));
    }

    [Fact]
    public void LoadFromText_RejectsEmptyFields()
    {
        // Whitespace-only rows are skipped, so this should produce zero Entries (no throw).
        var Entries = EntriesCsvLoader.LoadFromText("EntryName\n   \n");
        Assert.Empty(Entries);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip()
    {
        var original = TestFixture.LoadSampleEntries();
        var path = Path.Combine(Path.GetTempPath(), $"tools_{Guid.NewGuid():N}.csv");
        try
        {
            EntriesCsvLoader.Save(path, original);
            var loaded = EntriesCsvLoader.Load(path);
            Assert.Equal(original.Count, loaded.Count);
            for (var i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Name, loaded[i].Name);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DefaultToolsCsv_ShipsWithBuild_AndMatchesDataRowCount()
    {
        var path = CriteriaCsvLoaderTests.FindDefaultCsv("Entries.csv");
        Assert.True(File.Exists(path), $"未在主项目输出目录找到 Entries.csv：{path}");
        var Entries = EntriesCsvLoader.Load(path);

        using var sr = new StreamReader(path);
        var rows = CsvParser.Parse(sr);
        var expectedCount = rows.Skip(1).Count(r => r.Count > 0 && r.Any(v => !string.IsNullOrWhiteSpace(v)));

        Assert.Equal(expectedCount, Entries.Count);
    }
}
