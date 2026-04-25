using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class ReviewWorkflowTests
{
    private sealed class StubPrompt : IPrompt
    {
        private readonly Queue<string> _inputs;
        public List<string> Output { get; } = [];

        public StubPrompt(IEnumerable<string> inputs)
        {
            _inputs = new Queue<string>(inputs);
        }

        public void WriteLine(string text = "") => Output.Add(text);
        public void Write(string text) => Output.Add(text);
        public string? ReadLine() => _inputs.Count > 0 ? _inputs.Dequeue() : null;
    }

    private static (StubPrompt prompt, string auditDir) NewEnv(IEnumerable<string> inputs)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (new StubPrompt(inputs), dir);
    }

    private static List<string> BuildReviewInputs(CriteriaSet set, string score)
    {
        var inputs = new List<string>();
        var completedScores = new HashSet<string>(StringComparer.Ordinal);
        var completedCategoryComments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in set.SubCriteria.OrderBy(s => (int)s.Phase).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            inputs.Add(score);
            inputs.Add("");
            completedScores.Add(sub.Id);

            var categorySubIds = set.SubCriteria
                .Where(s => s.CategoryId == sub.CategoryId)
                .Select(s => s.Id);
            if (categorySubIds.All(completedScores.Contains)
                && completedCategoryComments.Add(sub.CategoryId))
            {
                inputs.Add("");
            }
        }

        inputs.Add("");
        return inputs;
    }

    [Fact]
    public void ReviewOne_AllThrees_ProducesPerfectScore_AndAuditFile()
    {
        var set = TestFixture.LoadSample();
        var inputs = BuildReviewInputs(set, "3");
        var (prompt, dir) = NewEnv(inputs);

        var wf = new ReviewWorkflow(prompt, set.Categories, set.SubCriteria, TestFixture.LoadSampleWeightSnapshot(), dir);
        var review = wf.ReviewOne(new Entry("测试作品"));

        Assert.Equal(100.0, review.TotalScore, 1);
        Assert.Equal(set.Categories.Count, review.CategoryComments.Count);
        Assert.NotEmpty(Directory.GetFiles(dir, "*.jsonl"));
    }

    [Fact]
    public void ReviewOne_RejectsOutOfRangeInput_ThenAcceptsValid()
    {
        var set = TestFixture.LoadSample();
        var inputs = new List<string> { "abc", "9", "0", "" };
        var completedScores = new HashSet<string>(StringComparer.Ordinal) { set.SubCriteria.OrderBy(s => (int)s.Phase).ThenBy(s => s.Id, StringComparer.Ordinal).First().Id };
        var completedCategoryComments = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < set.SubCriteria.Count; i++)
        {
            var sub = set.SubCriteria.OrderBy(s => (int)s.Phase).ThenBy(s => s.Id, StringComparer.Ordinal).ElementAt(i);
            inputs.Add("0");
            inputs.Add("");
            completedScores.Add(sub.Id);
            var categorySubIds = set.SubCriteria
                .Where(s => s.CategoryId == sub.CategoryId)
                .Select(s => s.Id);
            if (categorySubIds.All(completedScores.Contains)
                && completedCategoryComments.Add(sub.CategoryId))
            {
                inputs.Add("");
            }
        }
        inputs.Add("");
        var (prompt, dir) = NewEnv(inputs);

        var wf = new ReviewWorkflow(prompt, set.Categories, set.SubCriteria, TestFixture.LoadSampleWeightSnapshot(), dir);
        var review = wf.ReviewOne(new Entry("测试作品"));

        Assert.Equal(0.0, review.TotalScore, 1);
        Assert.Contains(prompt.Output, line => line.Contains("输入无效"));
    }
}
