using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class ResultsCsvWriterTests
{
    private static EntryReview MakeReview(
        CriteriaSet set, string name, int allScore, params (string subId, string comment)[] commented)
    {
        var weights = TestFixture.LoadSampleWeightSnapshot();
        var calc = new ScoreCalculator(set.Categories, set.SubCriteria, weights.NormalizedWeights);
        var raw = TestFixture.AllScores(set, allScore);
        var result = calc.Compute(raw);
        var entries = set.SubCriteria
            .Select(s =>
            {
                var c = commented.FirstOrDefault(x => x.subId == s.Id).comment;
                return new ScoreEntry(s.Id, raw[s.Id], DateTimeOffset.Now, c);
            })
            .ToList();
        var categoryComments = set.Categories.ToDictionary(c => c.Id, c => c.Id == "C1" ? (string?)"大项备注" : null);
        return new EntryReview(
            name,
            DateTimeOffset.Now,
            entries,
            result.CategoryScores,
            result.TotalScore,
            categoryComments,
            "项目备注",
            weights.NormalizedWeights);
    }

    [Fact]
    public void Smoke_BuildCsvText_HasExpectedHeaderAndRow()
    {
        var set = TestFixture.LoadSample();
        var review = MakeReview(set, "工具A", 3, (set.SubCriteria[0].Id, "很好"));

        var text = ResultsCsvWriter.BuildCsvText(set.Categories, set.SubCriteria, [review]);
        var rows = CsvParser.Parse(new StringReader(text));

        // 表头：参赛作品名称, [大项分…], 总分, 备注汇总
        Assert.Equal(1 + set.Categories.Count + 2, rows[0].Count);
        Assert.Equal("参赛作品名称", rows[0][0]);
        for (var i = 0; i < set.Categories.Count; i++)
        {
            Assert.Equal(set.Categories[i].Name, rows[0][1 + i]);
        }
        Assert.Equal("总分", rows[0][^2]);
        Assert.Equal("备注汇总", rows[0][^1]);

        // 数据行
        Assert.Equal(2, rows.Count);
        Assert.Equal("工具A", rows[1][0]);
        Assert.Equal("100.0", rows[1][^2]);
        Assert.Contains("项目备注", rows[1][^1]);
        Assert.Contains("大项备注", rows[1][^1]);
        Assert.Contains("很好", rows[1][^1]);
        Assert.Contains(set.SubCriteria[0].Title, rows[1][^1]);
        Assert.Contains(set.SubCriteria[0].Id, rows[1][^1]);
    }

    [Fact]
    public void Smoke_Write_FileRoundTripParsesCleanly()
    {
        var set = TestFixture.LoadSample();
        var r1 = MakeReview(set, "工具A", 3);
        var r2 = MakeReview(set, "工具B,带,逗号", 0, (set.SubCriteria[1].Id, "含\"引号\"备注"));

        var path = Path.Combine(Path.GetTempPath(), $"results_{Guid.NewGuid():N}.csv");
        try
        {
            ResultsCsvWriter.Write(path, set.Categories, set.SubCriteria, [r1, r2]);
            Assert.True(File.Exists(path));

            var rows = CsvParser.ParseFile(path);
            Assert.Equal(3, rows.Count); // 1 表头 + 2 行
            Assert.Equal("工具B,带,逗号", rows[2][0]);
            Assert.Equal("0.0", rows[2][^2]);
            Assert.Contains("含\"引号\"备注", rows[2][^1]);
            Assert.Contains(set.SubCriteria[1].Title, rows[2][^1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Smoke_EmptyReviews_WritesHeaderOnly()
    {
        var set = TestFixture.LoadSample();
        var text = ResultsCsvWriter.BuildCsvText(set.Categories, set.SubCriteria, []);
        var rows = CsvParser.Parse(new StringReader(text));
        Assert.Single(rows);
    }

    [Fact]
    public void BuildCsvText_WithStandardizedReviews_WritesStandardizedCategoryScoresAndTotal()
    {
        var set = TestFixture.LoadSample();
        var rawReviews = new[]
        {
            MakeReview(set, "工具A", 0),
            MakeReview(set, "工具B", 3)
        };

        var standardizedReviews = ScoreStandardizer.Standardize(set.Categories, rawReviews);

        var text = ResultsCsvWriter.BuildCsvText(set.Categories, set.SubCriteria, standardizedReviews);
        var rows = CsvParser.Parse(new StringReader(text));

        Assert.Equal(3, rows.Count);

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var review = standardizedReviews[rowIndex - 1];
            Assert.Equal(review.EntryName, rows[rowIndex][0]);

            for (var categoryIndex = 0; categoryIndex < set.Categories.Count; categoryIndex++)
            {
                var category = set.Categories[categoryIndex];
                Assert.Equal(review.CategoryScores[category.Id].ToString("F1"), rows[rowIndex][1 + categoryIndex]);
            }

            Assert.Equal(review.TotalScore.ToString("F1"), rows[rowIndex][^2]);
        }
    }
}
