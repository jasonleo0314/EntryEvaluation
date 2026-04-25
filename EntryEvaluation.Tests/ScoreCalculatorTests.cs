using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class ScoreCalculatorTests
{
    private static ScoreCalculator NewCalc(out CriteriaSet set)
    {
        set = TestFixture.LoadSample();
        var weights = TestFixture.LoadSampleWeightSnapshot();
        return new ScoreCalculator(set.Categories, set.SubCriteria, weights.NormalizedWeights);
    }

    [Fact]
    public void AllZero_TotalEqualsZero()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 0));
        Assert.Equal(0.0, r.TotalScore, 1);
        Assert.True(r.InRequiredBand);
    }

    [Fact]
    public void AllThree_TotalEquals100()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 3));
        Assert.Equal(100.0, r.TotalScore, 1);
        Assert.True(r.InRequiredBand);
    }

    [Fact]
    public void AllTwo_TotalApprox93Point3()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 2));
        Assert.Equal(66.7, r.TotalScore, 1);
    }

    [Fact]
    public void AllOne_TotalApprox86Point7()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 1));
        Assert.Equal(33.3, r.TotalScore, 1);
    }

    [Fact]
    public void RandomInputs_AlwaysWithinZeroTo100()
    {
        var calc = NewCalc(out var set);
        var rng = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            var dict = set.SubCriteria.ToDictionary(s => s.Id, _ => rng.Next(0, 4));
            var r = calc.Compute(dict);
            Assert.InRange(r.TotalScore, 0.0, 100.0);
            Assert.True(r.InRequiredBand);
        }
    }

    [Fact]
    public void CategoryScores_StayWithinTheirBands()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 3));
        foreach (var cat in set.Categories)
        {
            var s = r.CategoryScores[cat.Id];
            Assert.InRange(s, 0.0, cat.MaxPoints + 1e-6);
        }
    }

    [Fact]
    public void OutOfRangeScore_Throws()
    {
        var calc = NewCalc(out var set);
        var dict = TestFixture.AllScores(set, 0);
        dict[set.SubCriteria[0].Id] = 4;
        Assert.Throws<ArgumentOutOfRangeException>(() => calc.Compute(dict));
    }

    [Fact]
    public void MissingScore_Throws()
    {
        var calc = NewCalc(out var set);
        var dict = TestFixture.AllScores(set, 0);
        dict.Remove(set.SubCriteria[0].Id);
        Assert.Throws<InvalidOperationException>(() => calc.Compute(dict));
    }

    [Fact]
    public void MissingWeight_Throws()
    {
        var set = TestFixture.LoadSample();
        var weights = TestFixture.LoadSampleWeightSnapshot().NormalizedWeights.ToDictionary();
        weights.Remove(set.SubCriteria[0].Id);

        Assert.Throws<InvalidOperationException>(() => new ScoreCalculator(set.Categories, set.SubCriteria, weights));
    }

    [Fact]
    public void TotalScore_RoundsToOneDecimal()
    {
        var calc = NewCalc(out var set);
        var r = calc.Compute(TestFixture.AllScores(set, 2));
        Assert.Equal(r.TotalScore, Math.Round(r.TotalScore, 1, MidpointRounding.AwayFromZero));
    }
}
