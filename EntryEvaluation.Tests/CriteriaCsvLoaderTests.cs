using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class CriteriaCsvLoaderTests
{
    [Fact]
    public void LoadFromText_BuildsValidCriteriaSet()
    {
        var set = TestFixture.LoadSample();
        using var sr = new StringReader(TestFixture.SampleCriteriaCsv);
        var rows = CsvParser.Parse(sr);
        var dataRows = rows.Skip(1).Where(r => r.Count > 0 && r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        var expectedCategoryCount = dataRows.Select(r => r[0].Trim()).Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(expectedCategoryCount, set.Categories.Count);
        Assert.Equal(dataRows.Count, set.SubCriteria.Count);
        // 不抛异常即代表通过 CriteriaCatalog.Validate
    }

    [Fact]
    public void LoadFromText_RejectsBadHeader()
    {
        var bad = "X,Y\n1,2\n";
        Assert.Throws<InvalidDataException>(() => CriteriaCsvLoader.LoadFromText(bad));
    }

    [Fact]
    public void LoadFromText_RejectsInconsistentCategoryName()
    {
        var bad =
            "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
            "C1,运行条件,10,S1,ReadDocs,a,d\n" +
            "C1,改名,10,S2,ReadDocs,b,d\n";
        Assert.Throws<InvalidDataException>(() => CriteriaCsvLoader.LoadFromText(bad));
    }

    [Fact]
    public void LoadFromText_RejectsInvalidPhase()
    {
        var bad =
            "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
            "C1,运行条件,10,S1,NotAPhase,a,d\n";
        Assert.Throws<InvalidDataException>(() => CriteriaCsvLoader.LoadFromText(bad));
    }

    [Fact]
    public void LoadFromText_DoesNotRequireWeights()
    {
        var csv =
            "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
            "C1,A,100,S1,ReadDocs,t,d\n";

        var set = CriteriaCsvLoader.LoadFromText(csv);

        Assert.Single(set.SubCriteria);
    }

    [Fact]
    public void LoadFromText_RejectsBrokenCategoryTotal()
    {
        var bad =
            "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
            "C1,A,50,S1,ReadDocs,t,d\n"; // 总分仅 50
        Assert.Throws<InvalidOperationException>(() => CriteriaCsvLoader.LoadFromText(bad));
    }

    [Fact]
    public void LoadFromText_RejectsDuplicateSubId()
    {
        var bad =
            "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
            "C1,A,100,S1,ReadDocs,t,d\n" +
            "C1,A,100,S1,ReadDocs,t,d\n";
        Assert.Throws<InvalidDataException>(() => CriteriaCsvLoader.LoadFromText(bad));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripPreservesData()
    {
        var original = TestFixture.LoadSample();
        var path = Path.Combine(Path.GetTempPath(), $"crit_{Guid.NewGuid():N}.csv");
        try
        {
            CriteriaCsvLoader.Save(path, original);
            var loaded = CriteriaCsvLoader.Load(path);

            Assert.Equal(original.Categories.Count, loaded.Categories.Count);
            Assert.Equal(original.SubCriteria.Count, loaded.SubCriteria.Count);

            foreach (var s in original.SubCriteria)
            {
                var l = loaded.SubCriteria.Single(x => x.Id == s.Id);
                Assert.Equal(s.CategoryId, l.CategoryId);
                Assert.Equal(s.Phase, l.Phase);
                Assert.Equal(s.Title, l.Title);
                Assert.Equal(s.Description, l.Description);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DefaultCriteriaCsv_ShipsWithBuild_AndIsValid()
    {
        // CSV 通过 csproj <Content CopyToOutputDirectory> 复制到主项目输出目录
        var path = FindDefaultCsv("Criteria.csv");
        Assert.True(File.Exists(path), $"未在主项目输出目录找到 Criteria.csv：{path}");
        var set = CriteriaCsvLoader.Load(path);

        var rows = CsvParser.ParseFile(path);
        var dataRows = rows.Skip(1).Where(r => r.Count > 0 && r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        var expectedCategoryCount = dataRows.Select(r => r[0].Trim()).Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(expectedCategoryCount, set.Categories.Count);
        Assert.Equal(dataRows.Count, set.SubCriteria.Count);
        Assert.Equal(100, set.Categories.Sum(c => c.MaxPoints), 6);
    }

    [Fact]
    public void DefaultWeightsCsv_ShipsWithBuild_AndIsValid()
    {
        var set = TestFixture.LoadSample();
        var path = FindDefaultCsv("Weights.csv");
        Assert.True(File.Exists(path), $"未在主项目输出目录找到 Weights.csv：{path}");
        var weights = WeightsCsvLoader.Load(path);
        var normalized = WeightsCollector.Normalize(set.Categories, set.SubCriteria, weights);
        foreach (var cat in set.Categories)
        {
            var sum = set.SubCriteria.Where(s => s.CategoryId == cat.Id).Sum(s => normalized[s.Id]);
            Assert.Equal(1.0, sum, 6);
        }
    }

    internal static string FindDefaultCsv(string name)
    {
        // 在测试输出目录找不到时，回溯到主项目输出目录。
        var here = AppContext.BaseDirectory;
        var p = Path.Combine(here, name);
        if (File.Exists(p)) return p;

        var dir = new DirectoryInfo(here);
        // 上溯 4 层，找到与测试 bin 同级的主项目 bin
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "bin", "Debug", "net10.0", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return p;
    }
}
