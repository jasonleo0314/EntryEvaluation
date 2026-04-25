using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class CriteriaCatalogTests
{
    [Fact]
    public void SampleCsv_PassesValidation()
    {
        var set = TestFixture.LoadSample();
        CriteriaCatalog.Validate(set.Categories, set.SubCriteria);
    }

    [Fact]
    public void SampleCsv_TotalMaxIs100()
    {
        var set = TestFixture.LoadSample();
        Assert.Equal(100, set.Categories.Sum(c => c.MaxPoints), 6);
    }

    [Fact]
    public void EachCategory_NormalizedWeightSumIsOne()
    {
        var set = TestFixture.LoadSample();
        var weights = TestFixture.LoadSampleWeightSnapshot().NormalizedWeights;
        foreach (var cat in set.Categories)
        {
            var sum = set.SubCriteria.Where(s => s.CategoryId == cat.Id).Sum(s => weights[s.Id]);
            Assert.Equal(1.0, sum, 6);
        }
    }

    [Fact]
    public void SubCriteria_CanBeRecomposedIntoCategories()
    {
        var set = TestFixture.LoadSample();
        foreach (var sub in set.SubCriteria)
        {
            Assert.Contains(set.Categories, c => c.Id == sub.CategoryId);
        }
    }

    [Fact]
    public void Validate_RejectsMismatchedTotal()
    {
        var cats = new List<Category> { new("C1", "A", 50) };
        var subs = new List<SubCriterion>
        {
            new("S1", "C1", WorkflowPhase.ReadDocs, "t", "d")
        };
        Assert.Throws<InvalidOperationException>(() => CriteriaCatalog.Validate(cats, subs));
    }

    [Fact]
    public void Validate_RejectsDuplicateCategoryId()
    {
        var cats = new List<Category>
        {
            new("C1", "A", 50),
            new("C1", "B", 50)
        };
        var subs = new List<SubCriterion>
        {
            new("S1", "C1", WorkflowPhase.ReadDocs, "t", "d")
        };
        Assert.Throws<InvalidOperationException>(() => CriteriaCatalog.Validate(cats, subs));
    }
}
