using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class ScoreStandardizerTests
{
    [Fact]
    public void Standardize_TotalScores_FollowZScoreFormula()
    {
        var set = TestFixture.LoadSample();
        var rawTotals = new[] { 0.0, 50.0, 100.0 };
        var reviews = rawTotals.Select((t, i) => MakeProportionalReview(set, $"作品{i + 1}", t)).ToArray();

        var standardization = new StandardizationSettings();
        var expected = ExpectedStandardizedTotals(rawTotals, standardization);

        var standardized = ScoreStandardizer.Standardize(set.Categories, reviews, standardization: standardization);

        for (var i = 0; i < expected.Length; i++)
        {
            // 大项独立按 RoundingDigits 取整后再求和会引入轻微累积误差，
            // 因此用 0 位小数的容差校验总分仍贴合 z-score 公式。
            Assert.Equal(expected[i], standardized[i].TotalScore, 0);
        }
    }

    [Fact]
    public void Standardize_WhenScoresAreSame_ReturnsTargetMean()
    {
        var set = TestFixture.LoadSample();
        var standardization = new StandardizationSettings();
        var reviews = new[]
        {
            MakeProportionalReview(set, "作品A", 60.0),
            MakeProportionalReview(set, "作品B", 60.0)
        };

        var standardized = ScoreStandardizer.Standardize(set.Categories, reviews, standardization: standardization);

        Assert.All(standardized, review => Assert.Equal(standardization.TargetMean, review.TotalScore, 1));
    }

    [Fact]
    public void Standardize_CategoryScores_NeverExceedMaxPointsAndSumToTotal()
    {
        var set = TestFixture.LoadSample();
        var rawTotals = new[] { 10.0, 55.0, 95.0 };
        var reviews = rawTotals.Select((t, i) => MakeProportionalReview(set, $"作品{i + 1}", t)).ToArray();

        var standardized = ScoreStandardizer.Standardize(set.Categories, reviews);

        foreach (var review in standardized)
        {
            foreach (var cat in set.Categories)
            {
                var score = review.CategoryScores[cat.Id];
                Assert.InRange(score, 0.0, cat.MaxPoints);
            }

            var sum = set.Categories.Sum(c => review.CategoryScores[c.Id]);
            Assert.Equal(review.TotalScore, sum, 1);
        }
    }

    [Fact]
    public void Standardize_RedistributesOverflow_WhenProportionalShareExceedsCap()
    {
        var set = TestFixture.LoadSample();

        // 一个原始得分高度集中在小满分大项的作品，按比例分配后会触发瀑布式重分配。
        var smallest = set.Categories.OrderBy(c => c.MaxPoints).First();
        var biggest = set.Categories.OrderByDescending(c => c.MaxPoints).First();
        var rawScores = set.Categories.ToDictionary(
            c => c.Id,
            c => string.Equals(c.Id, smallest.Id, StringComparison.Ordinal) ? c.MaxPoints : 0.0,
            StringComparer.Ordinal);
        var rawTotal = rawScores.Values.Sum();

        // 第二个作品提供方差，使总分被 z-score 拉到接近 TargetMean，必然 > smallest.MaxPoints。
        var reviews = new[]
        {
            MakeReviewFromCategoryScores(set, "高分聚集", rawScores, rawTotal),
            MakeProportionalReview(set, "对照", 0.0)
        };

        var standardized = ScoreStandardizer.Standardize(set.Categories, reviews);

        var top = standardized[0];
        Assert.Equal(smallest.MaxPoints, top.CategoryScores[smallest.Id], 1);
        Assert.True(top.CategoryScores[biggest.Id] > 0,
            "溢出应被分配给其它未封顶大项。");
        Assert.True(top.TotalScore > smallest.MaxPoints,
            "重分配后总分应保留更多分值，而不是被单一大项的封顶吃掉。");

        foreach (var cat in set.Categories)
        {
            Assert.InRange(top.CategoryScores[cat.Id], 0.0, cat.MaxPoints);
        }
    }

    private static EntryReview MakeProportionalReview(CriteriaSet set, string name, double totalScore)
    {
        var totalCap = set.Categories.Sum(c => c.MaxPoints);
        var ratio = totalCap > 0 ? totalScore / totalCap : 0.0;
        var categoryScores = set.Categories.ToDictionary(
            c => c.Id,
            c => Math.Min(c.MaxPoints, c.MaxPoints * ratio),
            StringComparer.Ordinal);
        return new EntryReview(
            name,
            DateTimeOffset.Now,
            [],
            categoryScores,
            totalScore,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            null,
            new Dictionary<string, double>(StringComparer.Ordinal));
    }

    private static EntryReview MakeReviewFromCategoryScores(
        CriteriaSet set,
        string name,
        IReadOnlyDictionary<string, double> categoryScores,
        double totalScore) =>
        new(
            name,
            DateTimeOffset.Now,
            [],
            set.Categories.ToDictionary(c => c.Id, c => categoryScores[c.Id], StringComparer.Ordinal),
            totalScore,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            null,
            new Dictionary<string, double>(StringComparer.Ordinal));

    private static double[] ExpectedStandardizedTotals(
        IReadOnlyList<double> values,
        StandardizationSettings standardization)
    {
        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / values.Count);
        if (sd < standardization.Epsilon)
        {
            return values.Select(_ => standardization.TargetMean).ToArray();
        }

        var scoring = new ScoringSettings();
        var scale = standardization.TargetThreeStandardDeviations / 3.0;
        return values
            .Select(v => Math.Clamp(
                standardization.TargetMean + ((v - mean) / sd) * scale,
                scoring.RequiredMinimum,
                scoring.RequiredMaximum))
            .ToArray();
    }
}

