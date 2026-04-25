using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class IntegrationTests
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
    public void EndToEnd_DefaultWeights_ReviewsToolAndWritesResultCsv()
    {
        var tempDir = CreateTempDirectory();
        var criteriaSet = CriteriaCsvLoader.LoadFromText(TestFixture.SampleCriteriaCsv);
        var firstSubId = criteriaSet.SubCriteria
            .OrderBy(s => (int)s.Phase)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .First()
            .Id;
        var defaultWeights = WeightsCsvLoader.LoadFromText(TestFixture.SampleWeightsCsv);
        var Entry = EntriesCsvLoader.LoadFromText(TestFixture.SampleEntriesCsv)[0];
        var weightsPrompt = new StubPrompt([""]);
        var weights = new WeightsCollector(weightsPrompt, criteriaSet.Categories, criteriaSet.SubCriteria)
            .Collect(defaultWeights);
        var reviewPrompt = new StubPrompt(BuildReviewInputs(criteriaSet, "3", "子项表现良好", "大项整体良好", "项目总体良好"));
        var progressPath = Path.Combine(tempDir, "progress.json");
        var progressStore = new ProgressStore(progressPath);
        var completedReviews = new List<EntryReview>();
        var workflow = new ReviewWorkflow(reviewPrompt, criteriaSet.Categories, criteriaSet.SubCriteria, weights, Path.Combine(tempDir, "Audit"));

        var review = workflow.ReviewOne(
            Entry,
            resume: null,
            current => progressStore.Save(new ReviewProgress(completedReviews, current)));
        completedReviews.Add(review);
        progressStore.Save(new ReviewProgress(completedReviews, null));
        var resultsPath = Path.Combine(tempDir, "results.csv");
        ResultsCsvWriter.Write(resultsPath, criteriaSet.Categories, criteriaSet.SubCriteria, completedReviews);

        var rows = CsvParser.ParseFile(resultsPath);
        var savedProgress = progressStore.Load();
        Assert.Equal(2, rows.Count);
        Assert.Equal(Entry.Name, rows[1][0]);
        Assert.Equal("100.0", rows[1][^2]);
        Assert.Contains("项目总体良好", rows[1][^1]);
        Assert.Contains(criteriaSet.Categories[0].Name, rows[1][^1]);
        Assert.Contains(criteriaSet.SubCriteria[0].Title, rows[1][^1]);
        Assert.Contains($"[{firstSubId} ", rows[1][^1]);
        Assert.Contains(weightsPrompt.Output, line => line.Contains("已使用默认权重"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(tempDir, "Audit"), "*.jsonl"));
        Assert.Single(savedProgress.CompletedReviews);
        Assert.Null(savedProgress.CurrentReview);
    }

    [Fact]
    public void EndToEnd_SavedProgress_ResumesReviewAndClearsCurrentReview()
    {
        var tempDir = CreateTempDirectory();
        var criteriaSet = CriteriaCsvLoader.LoadFromText(TestFixture.SampleCriteriaCsv);
        var preservedCategoryId = criteriaSet.Categories[0].Id;
        var resumedCategoryId = criteriaSet.Categories
            .Select(c => c.Id)
            .First(id => !string.Equals(id, preservedCategoryId, StringComparison.Ordinal));
        var weights = TestFixture.LoadSampleWeightSnapshot();
        var Entry = new Entry("恢复测试工具");
        var c1Scores = criteriaSet.SubCriteria
            .Where(s => string.Equals(s.CategoryId, preservedCategoryId, StringComparison.Ordinal))
            .Select(s => new ScoreEntry(s.Id, 3, DateTimeOffset.Now, $"已保存{s.Title}"))
            .ToList();
        var current = new InProgressReview(
            Entry.Name,
            DateTimeOffset.Now,
            c1Scores,
            new Dictionary<string, string?> { [preservedCategoryId] = "已保存大项备注" },
            null,
            weights.RawWeights,
            weights.NormalizedWeights);
        var progressPath = Path.Combine(tempDir, "progress.json");
        var progressStore = new ProgressStore(progressPath);
        progressStore.Save(new ReviewProgress([], current));
        var loadedProgress = progressStore.Load();
        var completedReviews = loadedProgress.CompletedReviews.ToList();
        var resumePrompt = new StubPrompt(BuildResumeInputs(criteriaSet, preservedCategoryId, "2", "恢复后子项备注", "恢复后大项备注", "恢复后项目备注"));
        var workflow = new ReviewWorkflow(resumePrompt, criteriaSet.Categories, criteriaSet.SubCriteria, weights, Path.Combine(tempDir, "Audit"));

        var review = workflow.ReviewOne(
            Entry,
            loadedProgress.CurrentReview,
            currentReview => progressStore.Save(new ReviewProgress(completedReviews, currentReview)));
        completedReviews.Add(review);
        progressStore.Save(new ReviewProgress(completedReviews, null));

        var savedProgress = progressStore.Load();
        Assert.Equal(criteriaSet.SubCriteria.Count, review.Scores.Count);
        Assert.Equal("恢复后项目备注", review.ProjectComment);
        Assert.Equal("已保存大项备注", review.CategoryComments[preservedCategoryId]);
        Assert.Equal($"恢复后大项备注-{resumedCategoryId}", review.CategoryComments[resumedCategoryId]);
        Assert.Single(savedProgress.CompletedReviews);
        Assert.Null(savedProgress.CurrentReview);
        Assert.Contains(resumePrompt.Output, line => line.Contains("开始评审"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(tempDir, "Audit"), "*.jsonl"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trh_integration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static List<string> BuildReviewInputs(
        CriteriaSet set,
        string score,
        string subComment,
        string categoryComment,
        string projectComment)
    {
        var inputs = new List<string>();
        var completedScores = new HashSet<string>(StringComparer.Ordinal);
        var completedCategoryComments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in OrderedSubCriteria(set))
        {
            inputs.Add(score);
            inputs.Add($"{subComment}-{sub.Title}");
            completedScores.Add(sub.Id);

            if (CategoryComplete(set, sub.CategoryId, completedScores)
                && completedCategoryComments.Add(sub.CategoryId))
            {
                inputs.Add($"{categoryComment}-{sub.CategoryId}");
            }
        }

        inputs.Add(projectComment);
        return inputs;
    }

    private static List<string> BuildResumeInputs(
        CriteriaSet set,
        string preservedCategoryId,
        string score,
        string subComment,
        string categoryComment,
        string projectComment)
    {
        var inputs = new List<string>();
        var completedScores = set.SubCriteria
            .Where(s => string.Equals(s.CategoryId, preservedCategoryId, StringComparison.Ordinal))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);
        var completedCategoryComments = new HashSet<string>(StringComparer.Ordinal) { preservedCategoryId };
        foreach (var sub in OrderedSubCriteria(set).Where(s => !string.Equals(s.CategoryId, preservedCategoryId, StringComparison.Ordinal)))
        {
            inputs.Add(score);
            inputs.Add($"{subComment}-{sub.Title}");
            completedScores.Add(sub.Id);

            if (CategoryComplete(set, sub.CategoryId, completedScores)
                && completedCategoryComments.Add(sub.CategoryId))
            {
                inputs.Add($"{categoryComment}-{sub.CategoryId}");
            }
        }

        inputs.Add(projectComment);
        return inputs;
    }

    private static IEnumerable<SubCriterion> OrderedSubCriteria(CriteriaSet set) =>
        set.SubCriteria.OrderBy(s => (int)s.Phase).ThenBy(s => s.Id, StringComparer.Ordinal);

    private static bool CategoryComplete(CriteriaSet set, string categoryId, HashSet<string> completedScores) =>
        set.SubCriteria.Where(s => s.CategoryId == categoryId).Select(s => s.Id).All(completedScores.Contains);
}
