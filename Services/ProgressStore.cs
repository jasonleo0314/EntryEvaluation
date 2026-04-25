using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 将已完成评审和当前未完成评审写入本地 JSON 文件，支持程序重启后恢复。
/// </summary>
public sealed class ProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }

    public ProgressStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public ReviewProgress Load()
    {
        if (!File.Exists(FilePath))
        {
            return ReviewProgress.Empty;
        }

        using var stream = File.OpenRead(FilePath);
        return JsonSerializer.Deserialize<ReviewProgress>(stream, JsonOptions)
            ?? ReviewProgress.Empty;
    }

    public void Save(ReviewProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(FilePath);
        JsonSerializer.Serialize(stream, progress, JsonOptions);
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}

public sealed record ReviewProgress(
    IReadOnlyList<EntryReview> CompletedReviews,
    InProgressReview? CurrentReview)
{
    public static ReviewProgress Empty { get; } = new([], null);
}

public sealed record InProgressReview(
    string EntryName,
    DateTimeOffset StartedAt,
    IReadOnlyList<ScoreEntry> Scores,
    IReadOnlyDictionary<string, string?> CategoryComments,
    string? ProjectComment,
    IReadOnlyDictionary<string, double> RawWeights,
    IReadOnlyDictionary<string, double> FinalWeights);
