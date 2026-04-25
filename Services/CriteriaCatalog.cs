using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 评分目录一致性校验工具。业务数据（大项/子项/权重/工具清单）一律由 CSV 提供，不在代码中硬编码。
/// </summary>
public static class CriteriaCatalog
{
    /// <summary>
    /// 校验目录的内部一致性：大项分值合计 = 100；Id 唯一；每个大项至少 1 个子项；子项归属合法。
    /// 权重不在此处校验（由 WeightsCollector 负责归一化）。
    /// </summary>
    public static void Validate(
        IEnumerable<Category> categories,
        IEnumerable<SubCriterion> subCriteria,
        ScoringSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        settings ??= new ScoringSettings();
        settings.Validate();

        var cats = categories.ToList();
        var subs = subCriteria.ToList();

        if (cats.Count == 0)
        {
            throw new InvalidOperationException("评分目录至少需要 1 个大项。");
        }

        var dupCat = cats.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupCat is not null)
        {
            throw new InvalidOperationException($"大项 Id 重复: {dupCat.Key}。");
        }

        var totalMax = cats.Sum(c => c.MaxPoints);
        if (Math.Abs(totalMax - settings.CategoryTotalPoints) > settings.Tolerance)
        {
            throw new InvalidOperationException(
                $"大项分值合计应为 {settings.CategoryTotalPoints}，当前为 {totalMax}。");
        }

        foreach (var cat in cats)
        {
            var items = subs.Where(s => s.CategoryId == cat.Id).ToList();
            if (items.Count == 0)
            {
                throw new InvalidOperationException(
                    $"大项 {cat.Name} 缺少子项。");
            }
        }

        var orphan = subs.FirstOrDefault(s => cats.All(c => c.Id != s.CategoryId));
        if (orphan is not null)
        {
            throw new InvalidOperationException(
                $"子项 {orphan.Id} 归属未知大项 {orphan.CategoryId}。");
        }

        var dupSub = subs.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupSub is not null)
        {
            throw new InvalidOperationException($"子项 Id 重复: {dupSub.Key}。");
        }
    }
}
