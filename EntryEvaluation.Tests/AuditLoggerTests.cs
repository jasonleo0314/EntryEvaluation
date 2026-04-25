using System.Text.Json;
using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Xunit;

namespace EntryEvaluation.Tests;

public class AuditLoggerTests
{
    [Fact]
    public void WritesJsonLines_ForEachStage()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"audit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.jsonl");

        var set = TestFixture.LoadSample();
        var weights = TestFixture.LoadSampleWeightSnapshot();
        var calc = new ScoreCalculator(set.Categories, set.SubCriteria, weights.NormalizedWeights);
        var result = calc.Compute(TestFixture.AllScores(set, 3));

        using (var logger = new AuditLogger(path))
        {
            logger.Log("Begin", new { Hello = "world" });
            logger.LogScore(
                set.SubCriteria[0],
                new ScoreEntry(set.SubCriteria[0].Id, 3, DateTimeOffset.Now, "ok"),
                weights.NormalizedWeights[set.SubCriteria[0].Id]);
            logger.LogResult(result);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("Time", out _));
            Assert.True(doc.RootElement.TryGetProperty("Stage", out _));
            Assert.True(doc.RootElement.TryGetProperty("Payload", out _));
        }

        Assert.Contains("Begin", lines[0]);
        Assert.Contains("Score", lines[1]);
        Assert.Contains("Result", lines[2]);
    }
}
