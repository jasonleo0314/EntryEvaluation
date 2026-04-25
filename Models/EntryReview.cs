namespace EntryEvaluation.Models;

/// <summary>
/// 单个参赛作品的整次评审快照。
/// </summary>
public sealed record EntryReview(
    string EntryName,
    DateTimeOffset StartedAt,
    IReadOnlyList<ScoreEntry> Scores,
    IReadOnlyDictionary<string, double> CategoryScores,
    double TotalScore,
    IReadOnlyDictionary<string, string?> CategoryComments,
    string? ProjectComment,
    IReadOnlyDictionary<string, double> FinalWeights);
