namespace EntryEvaluation.Models;

/// <summary>
/// 一次评审使用的全部目录定义（4 大项 + 子项），通常由 CSV 加载得到。
/// </summary>
public sealed record CriteriaSet(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<SubCriterion> SubCriteria);
