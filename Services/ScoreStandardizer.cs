using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 在全部参赛作品完成评审后：先对各作品的总分按 z-score 标准化，
/// 再把每个作品的标准化总分按其原始大项得分比例分配回各大项，
/// 并以瀑布式（water-fill）算法在每个大项不超出 <see cref="Category.MaxPoints"/> 的前提下重新分配溢出。
/// </summary>
public static class ScoreStandardizer
{
    public static IReadOnlyList<EntryReview> Standardize(
        IReadOnlyList<Category> categories,
        IReadOnlyList<EntryReview> reviews,
        ScoringSettings? scoring = null,
        StandardizationSettings? standardization = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(reviews);
        scoring ??= new ScoringSettings();
        standardization ??= new StandardizationSettings();
        scoring.Validate();
        standardization.Validate();

        if (reviews.Count == 0)
        {
            return [];
        }

        var standardizedTotals = StandardizeSeries(
            reviews.Select(r => r.TotalScore).ToList(),
            scoring,
            standardization);

        var standardizedReviews = new List<EntryReview>(reviews.Count);
        for (var i = 0; i < reviews.Count; i++)
        {
            var review = reviews[i];
            var distributed = DistributeProportionally(
                categories,
                review.CategoryScores,
                standardizedTotals[i],
                scoring);

            // 由于封顶/取整可能让大项之和与目标总分有微小偏差，
            // 总分仍以分配后的实际之和为准，保持总分 = Σ 大项分。
            var actualTotal = Math.Round(
                distributed.Values.Sum(),
                scoring.RoundingDigits,
                MidpointRounding.AwayFromZero);

            standardizedReviews.Add(review with
            {
                CategoryScores = distributed,
                TotalScore = actualTotal
            });
        }

        return standardizedReviews;
    }

    private static IReadOnlyList<double> StandardizeSeries(
        IReadOnlyList<double> values,
        ScoringSettings scoring,
        StandardizationSettings standardization)
    {
        var mean = values.Average();
        var standardDeviation = Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / values.Count);
        if (standardDeviation < standardization.Epsilon)
        {
            return values.Select(_ => standardization.TargetMean).ToList();
        }

        var scale = standardization.TargetThreeStandardDeviations / 3.0;
        return values
            .Select(v => Math.Clamp(
                standardization.TargetMean + ((v - mean) / standardDeviation) * scale,
                scoring.RequiredMinimum,
                scoring.RequiredMaximum))
            .ToList();
    }

    /// <summary>
    /// 把 <paramref name="standardizedTotal"/> 按 <paramref name="rawCategoryScores"/> 的比例分配到各大项，
    /// 任一大项不得超过其 <see cref="Category.MaxPoints"/>；超出部分按未封顶大项的比例继续再分配，
    /// 直到无溢出或所有大项均封顶。
    /// </summary>
    private static IReadOnlyDictionary<string, double> DistributeProportionally(
        IReadOnlyList<Category> categories,
        IReadOnlyDictionary<string, double> rawCategoryScores,
        double standardizedTotal,
        ScoringSettings scoring)
    {
        var allocations = categories.ToDictionary(c => c.Id, _ => 0.0, StringComparer.Ordinal);
        var capped = new HashSet<string>(StringComparer.Ordinal);
        var totalCap = categories.Sum(c => c.MaxPoints);
        var remaining = Math.Min(standardizedTotal, totalCap);

        // 大项原始得分作为分配权重；若全部为 0，则按 MaxPoints 比例分配。
        var rawTotal = categories.Sum(c => rawCategoryScores.TryGetValue(c.Id, out var v) ? v : 0.0);
        var useMaxPointsAsBasis = rawTotal <= scoring.Tolerance;

        // 最多迭代次数 = 大项数量 + 1，保证收敛。
        for (var iter = 0; iter <= categories.Count && remaining > scoring.Tolerance; iter++)
        {
            var openCats = categories.Where(c => !capped.Contains(c.Id)).ToList();
            if (openCats.Count == 0)
            {
                break;
            }

            double Basis(Category c) => useMaxPointsAsBasis
                ? c.MaxPoints
                : (rawCategoryScores.TryGetValue(c.Id, out var v) ? v : 0.0);

            var basisSum = openCats.Sum(Basis);

            // 退化：开放大项的基准全为 0 → 在其中按 MaxPoints 等比分配。
            Func<Category, double> basisFn = Basis;
            if (basisSum <= scoring.Tolerance)
            {
                basisFn = c => c.MaxPoints;
                basisSum = openCats.Sum(basisFn);
                if (basisSum <= scoring.Tolerance)
                {
                    break;
                }
            }

            var overflow = 0.0;
            var anyCapHit = false;
            foreach (var c in openCats)
            {
                var share = remaining * basisFn(c) / basisSum;
                var headroom = c.MaxPoints - allocations[c.Id];
                if (share >= headroom - scoring.Tolerance)
                {
                    overflow += share - headroom;
                    allocations[c.Id] = c.MaxPoints;
                    capped.Add(c.Id);
                    anyCapHit = true;
                }
                else
                {
                    allocations[c.Id] += share;
                }
            }

            remaining = overflow;
            if (!anyCapHit)
            {
                break;
            }
        }

        return categories.ToDictionary(
            c => c.Id,
            c => Math.Round(allocations[c.Id], scoring.RoundingDigits, MidpointRounding.AwayFromZero),
            StringComparer.Ordinal);
    }
}
