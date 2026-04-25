namespace EntryEvaluation.Models;

/// <summary>
/// 评分大项。Id/Name/MaxPoints 全部由 CSV 提供，无业务硬编码。
/// </summary>
public sealed record Category(string Id, string Name, double MaxPoints);
