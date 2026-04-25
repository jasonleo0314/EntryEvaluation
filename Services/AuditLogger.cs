using System.Text.Json;
using System.Text.Json.Serialization;
using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 审计日志：每个步骤（权重确认、单条打分、大项汇总、总分计算）追加为一条 JSON 记录。
/// 同一次评审写入同一文件 Audit/{参赛作品名}_{时间戳}.jsonl。
/// </summary>
public sealed class AuditLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly StreamWriter _writer;

    public string FilePath { get; }

    public AuditLogger(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public void Log(string stage, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(payload);

        var record = new AuditRecord(DateTimeOffset.Now, stage, payload);
        _writer.WriteLine(JsonSerializer.Serialize(record, JsonOpts));
    }

    public void LogScore(SubCriterion sub, ScoreEntry entry, double finalWeight)
    {
        Log("Score", new
        {
            sub.Id,
            sub.Title,
            sub.CategoryId,
            Phase = sub.Phase.ToString(),
            FinalWeight = finalWeight,
            entry.RawScore,
            entry.Comment,
            entry.RecordedAt
        });
    }

    public void LogWeights(
        IReadOnlyList<SubCriterion> subs,
        IReadOnlyDictionary<string, double> rawWeights,
        IReadOnlyDictionary<string, double> finalWeights)
    {
        Log("Weights", subs.Select(s => new
        {
            s.Id,
            s.CategoryId,
            Phase = s.Phase.ToString(),
            s.Title,
            RawWeight = rawWeights[s.Id],
            FinalWeight = finalWeights[s.Id]
        }));
    }

    public void LogCategoryComment(Category category, string? comment)
    {
        Log("CategoryComment", new
        {
            category.Id,
            category.Name,
            Comment = comment
        });
    }

    public void LogProjectComment(string? comment)
    {
        Log("ProjectComment", new { Comment = comment });
    }

    public void LogProgressRestored(InProgressReview review)
    {
        Log("ProgressRestored", new
        {
            review.EntryName,
            review.StartedAt,
            ScoreCount = review.Scores.Count,
            CategoryCommentCount = review.CategoryComments.Count
        });
    }

    public void LogResult(ScoringResult result)
    {
        Log("Result", new
        {
            result.CategoryScores,
            result.CategoryWeightedRates,
            result.TotalScore,
            result.InRequiredBand,
            result.RequiredMinimum,
            result.RequiredMaximum
        });
    }

    public void Dispose() => _writer.Dispose();

    private sealed record AuditRecord(DateTimeOffset Time, string Stage, object Payload);
}
