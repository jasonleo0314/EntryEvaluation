namespace EntryEvaluation.Models;

/// <summary>
/// 单条子项打分记录（含填写时间，可追溯）。
/// </summary>
public sealed record ScoreEntry(
    string SubCriterionId,
    int RawScore,
    DateTimeOffset RecordedAt,
    string? Comment);
