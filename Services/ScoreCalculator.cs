using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 计分结果（含中间量，便于追溯）。
/// </summary>
public sealed record ScoringResult(
    IReadOnlyDictionary<string, double> CategoryScores,
    IReadOnlyDictionary<string, double> CategoryWeightedRates,
    double TotalScore,
    bool InRequiredBand,
    double RequiredMinimum,
    double RequiredMaximum);

/// <summary>
/// 加权计分器。
/// 子项 0-3 → 子项归一化率 r_i = score_i / 3 ∈ [0,1]；
/// 大项加权率 R_c = Σ(r_i × w_i)（同一大项 w_i 之和为 1）；
/// 大项得分 = MaxPts × R_c；
/// 总分 = Σ 大项得分，保留 1 位小数。
/// </summary>
public sealed class ScoreCalculator
{
    private readonly IReadOnlyList<Category> _categories;
    private readonly IReadOnlyList<SubCriterion> _subCriteria;
    private readonly IReadOnlyDictionary<string, double> _weights;
    private readonly ScoringSettings _settings;

    public ScoreCalculator(
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyDictionary<string, double> weights,
        ScoringSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(weights);
        settings ??= new ScoringSettings();
        settings.Validate();
        CriteriaCatalog.Validate(categories, subCriteria, settings);
        ValidateWeights(categories, subCriteria, weights, settings.Tolerance);
        _categories = categories;
        _subCriteria = subCriteria;
        _weights = weights;
        _settings = settings;
    }

    public ScoringResult Compute(IReadOnlyDictionary<string, int> rawScores)
    {
        ArgumentNullException.ThrowIfNull(rawScores);

        foreach (var sub in _subCriteria)
        {
            if (!rawScores.ContainsKey(sub.Id))
            {
                throw new InvalidOperationException($"子项 {sub.Id} 缺少分值。");
            }
            var v = rawScores[sub.Id];
            if (v < _settings.MinimumScore || v > _settings.MaximumScore)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rawScores),
                    $"子项 {sub.Id} 分值 {v} 越界 [{_settings.MinimumScore},{_settings.MaximumScore}]。");
            }
        }

        var catScores = new Dictionary<string, double>(_categories.Count);
        var catRates = new Dictionary<string, double>(_categories.Count);

        foreach (var cat in _categories)
        {
            var items = _subCriteria.Where(s => s.CategoryId == cat.Id).ToList();

            // 同一大项权重之和恒为 1（由 WeightsCollector 归一化，并在构造时校验）
            var weightedRate = items.Sum(s => (rawScores[s.Id] / (double)_settings.MaximumScore) * _weights[s.Id]);
            var catScore = cat.MaxPoints * weightedRate;

            catRates[cat.Id] = weightedRate;
            catScores[cat.Id] = Math.Round(catScore, _settings.RoundingDigits, MidpointRounding.AwayFromZero);
        }

        var total = Math.Round(catScores.Values.Sum(), _settings.RoundingDigits, MidpointRounding.AwayFromZero);
        var inBand = total >= _settings.RequiredMinimum - _settings.Tolerance
                  && total <= _settings.RequiredMaximum + _settings.Tolerance;

        return new ScoringResult(catScores, catRates, total, inBand, _settings.RequiredMinimum, _settings.RequiredMaximum);
    }

    private static void ValidateWeights(
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyDictionary<string, double> weights,
        double tolerance)
    {
        var subIds = subCriteria.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = weights.Keys.FirstOrDefault(k => !subIds.Contains(k));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"权重包含未知子项 {unknown}。");
        }

        foreach (var sub in subCriteria)
        {
            if (!weights.TryGetValue(sub.Id, out var weight))
            {
                throw new InvalidOperationException($"子项 {sub.Id} 缺少权重。");
            }

            if (weight < 0)
            {
                throw new InvalidOperationException($"子项 {sub.Id} 权重不能为负。");
            }
        }

        foreach (var cat in categories)
        {
            var sum = subCriteria
                .Where(s => s.CategoryId == cat.Id)
                .Sum(s => weights[s.Id]);
            if (Math.Abs(sum - 1d) > tolerance)
            {
                throw new InvalidOperationException(
                    $"大项 {cat.Name} 的最终权重和应为 1，当前为 {sum}。");
            }
        }
    }
}
