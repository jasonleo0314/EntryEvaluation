using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class WeightsCollectorTests
{
    private sealed class StubPrompt : IPrompt
    {
        private readonly Queue<string> _inputs;

        public StubPrompt(IEnumerable<string> inputs)
        {
            _inputs = new Queue<string>(inputs);
        }

        public List<string> Output { get; } = [];

        public void WriteLine(string text = "") => Output.Add(text);
        public void Write(string text) => Output.Add(text);
        public string? ReadLine() => _inputs.Count > 0 ? _inputs.Dequeue() : null;
    }

    [Fact]
    public void Collect_WhenJudgeDoesNotModify_UsesDefaultWeights()
    {
        var set = TestFixture.LoadSample();
        var defaults = TestFixture.LoadSampleWeights();
        var prompt = new StubPrompt([""]);
        var collector = new WeightsCollector(prompt, set.Categories, set.SubCriteria);

        var snapshot = collector.Collect(defaults);

        Assert.Equal(defaults["S1.1"], snapshot.RawWeights["S1.1"], 6);
        Assert.Equal(1.0, set.SubCriteria.Where(s => s.CategoryId == "C1").Sum(s => snapshot.NormalizedWeights[s.Id]), 6);
        Assert.Contains(prompt.Output, line => line.Contains("默认权重如下"));
    }

    [Fact]
    public void Normalize_ScalesCategoryWeightsToOne()
    {
        var set = TestFixture.LoadSample();
        var raw = TestFixture.LoadSampleWeights().ToDictionary();
        raw["S1.1"] = 3;
        raw["S1.2"] = 3;
        raw["S1.3"] = 6;

        var normalized = WeightsCollector.Normalize(set.Categories, set.SubCriteria, raw);

        Assert.Equal(0.25, normalized["S1.1"], 6);
        Assert.Equal(0.25, normalized["S1.2"], 6);
        Assert.Equal(0.5, normalized["S1.3"], 6);
    }

    [Fact]
    public void Normalize_AllZeroCategory_UsesEqualWeights()
    {
        var set = TestFixture.LoadSample();
        var raw = TestFixture.LoadSampleWeights().ToDictionary();
        raw["S1.1"] = 0;
        raw["S1.2"] = 0;
        raw["S1.3"] = 0;

        var normalized = WeightsCollector.Normalize(set.Categories, set.SubCriteria, raw);

        Assert.Equal(1d / 3d, normalized["S1.1"], 6);
        Assert.Equal(1d / 3d, normalized["S1.2"], 6);
        Assert.Equal(1d / 3d, normalized["S1.3"], 6);
    }

    [Fact]
    public void Normalize_MissingWeight_Throws()
    {
        var set = TestFixture.LoadSample();
        var raw = TestFixture.LoadSampleWeights().ToDictionary();
        raw.Remove(set.SubCriteria[0].Id);

        Assert.Throws<InvalidOperationException>(() => WeightsCollector.Normalize(set.Categories, set.SubCriteria, raw));
    }

    [Fact]
    public void WeightsCsvLoader_DuplicateWeight_Throws()
    {
        var csv = "SubId,Weight\nS1,0.4\nS1,0.6\n";

        Assert.Throws<InvalidDataException>(() => WeightsCsvLoader.LoadFromText(csv));
    }
}
