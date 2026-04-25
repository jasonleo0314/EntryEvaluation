using System.Globalization;
using EntryEvaluation.Models;
using Spectre.Console;

namespace EntryEvaluation.Services;

/// <summary>
/// 采集评委现场权重，并把同一大项内的权重归一化为 1。
/// </summary>
public sealed class WeightsCollector
{
    private readonly IPrompt _prompt;
    private readonly IReadOnlyList<Category> _categories;
    private readonly IReadOnlyList<SubCriterion> _subCriteria;

    public WeightsCollector(
        IPrompt prompt,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        CriteriaCatalog.Validate(categories, subCriteria);
        _prompt = prompt;
        _categories = categories;
        _subCriteria = subCriteria;
    }

    public WeightSnapshot Collect(IReadOnlyDictionary<string, double> defaultWeights)
    {
        ArgumentNullException.ThrowIfNull(defaultWeights);
        ValidateCoverage(_subCriteria, defaultWeights);

        _prompt.WriteLine();
        PrintDefaultWeights(defaultWeights);
        _prompt.WriteMarkupLine("[grey]是否需要修改默认权重？[/][bold](y/N)[/]: ");
        _prompt.Write(string.Empty);
        var answer = _prompt.ReadLine();
        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedDefaults = Normalize(_categories, _subCriteria, defaultWeights);
            _prompt.WriteMarkupLine("[green]✓ 已使用默认权重。[/]");
            PrintFinalWeights(normalizedDefaults);
            return new WeightSnapshot(defaultWeights, normalizedDefaults);
        }

        _prompt.WriteMarkupLine("[yellow]请逐项输入权重。直接回车保留默认值；同一大项内最终会自动归一化为 1。[/]");

        var rawWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var cat in _categories)
        {
            _prompt.WriteLine();
            _prompt.WriteMarkupLine($"[bold cyan]【{Markup.Escape(cat.Name)}】[/][grey]满分 {cat.MaxPoints}[/]");
            foreach (var sub in _subCriteria.Where(s => s.CategoryId == cat.Id))
            {
                var defaultWeight = defaultWeights[sub.Id];
                rawWeights[sub.Id] = AskWeight(sub, defaultWeight);
            }
        }

        var normalizedWeights = Normalize(_categories, _subCriteria, rawWeights);
        PrintFinalWeights(normalizedWeights);
        return new WeightSnapshot(rawWeights, normalizedWeights);
    }

    public static IReadOnlyDictionary<string, double> Normalize(
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyDictionary<string, double> rawWeights)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(rawWeights);
        CriteriaCatalog.Validate(categories, subCriteria);
        ValidateCoverage(subCriteria, rawWeights);

        var normalized = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var cat in categories)
        {
            var items = subCriteria.Where(s => s.CategoryId == cat.Id).ToList();
            var sum = items.Sum(s => rawWeights[s.Id]);
            if (sum <= 0)
            {
                var equalWeight = 1d / items.Count;
                foreach (var item in items)
                {
                    normalized[item.Id] = equalWeight;
                }
                continue;
            }

            foreach (var item in items)
            {
                normalized[item.Id] = rawWeights[item.Id] / sum;
            }
        }

        return normalized;
    }

    public static void ValidateCoverage(
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyDictionary<string, double> weights)
    {
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(weights);

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
    }

    private double AskWeight(SubCriterion sub, double defaultWeight)
    {
        while (true)
        {
            _prompt.WriteMarkupLine(
                $"  [bold]{Markup.Escape(sub.Id)}[/] [grey]{Markup.Escape(sub.Title)}[/] " +
                $"权重（默认 [cyan]{defaultWeight.ToString("F4", CultureInfo.InvariantCulture)}[/]）：");
            _prompt.Write(string.Empty);
            var line = _prompt.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return defaultWeight;
            }

            if (double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
                && weight >= 0)
            {
                return weight;
            }

            _prompt.WriteMarkupLine("[red]  输入无效，请输入非负数字，或直接回车保留默认值。[/]");
        }
    }

    private void PrintFinalWeights(IReadOnlyDictionary<string, double> normalizedWeights)
    {
        _prompt.WriteLine();
        _prompt.WriteMarkupLine("[bold green]✓ 最终权重如下（同一大项内已归一化）：[/]");
        foreach (var cat in _categories)
        {
            _prompt.WriteMarkupLine($"  [bold cyan]· {Markup.Escape(cat.Name)}[/]");
            foreach (var sub in _subCriteria.Where(s => s.CategoryId == cat.Id))
            {
                _prompt.WriteMarkupLine(
                    $"      [[{Markup.Escape(sub.Id)}]] {Markup.Escape(sub.Title)}：[cyan]{normalizedWeights[sub.Id]:F4}[/]");
            }
        }
    }

    private void PrintDefaultWeights(IReadOnlyDictionary<string, double> defaultWeights)
    {
        _prompt.WriteMarkupLine("[bold]默认权重如下（请先确认）：[/]");
        foreach (var cat in _categories)
        {
            _prompt.WriteMarkupLine($"  [bold cyan]· {Markup.Escape(cat.Name)}[/]");
            foreach (var sub in _subCriteria.Where(s => s.CategoryId == cat.Id))
            {
                _prompt.WriteMarkupLine(
                    $"      [[{Markup.Escape(sub.Id)}]] {Markup.Escape(sub.Title)}：[grey]{defaultWeights[sub.Id]:F4}[/]");
            }
        }
    }
}

public sealed record WeightSnapshot(
    IReadOnlyDictionary<string, double> RawWeights,
    IReadOnlyDictionary<string, double> NormalizedWeights);
