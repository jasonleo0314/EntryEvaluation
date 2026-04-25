using System.Text;
using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 评审结果汇总写出器：把多次 EntryReview 写成一个 CSV。
/// 列：参赛作品名称, [各大项分…], 总分, 备注汇总（列名可配置）。
/// </summary>
public static class ResultsCsvWriter
{
    public const string DefaultFileName = "ReviewResults.csv";

    public static void Write(
        string path,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyList<EntryReview> reviews,
        DisplaySettings? display = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(reviews);
        display ??= new DisplaySettings();
        display.Validate();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(true));
        CsvParser.Write(sw, BuildRows(categories, subCriteria, reviews, display));
    }

    public static string BuildCsvText(
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyList<EntryReview> reviews,
        DisplaySettings? display = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(reviews);
        display ??= new DisplaySettings();
        display.Validate();
        using var sw = new StringWriter();
        CsvParser.Write(sw, BuildRows(categories, subCriteria, reviews, display));
        return sw.ToString();
    }

    private static IEnumerable<IEnumerable<string>> BuildRows(
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        IReadOnlyList<EntryReview> reviews,
        DisplaySettings display)
    {
        var header = new List<string> { display.EntryNameColumnHeader };
        header.AddRange(categories.Select(c => c.Name));
        header.Add(display.ResultsTotalColumnHeader);
        header.Add(display.ResultsCommentColumnHeader);
        yield return header;

        var categoriesById = categories.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var subCriteriaById = subCriteria.ToDictionary(s => s.Id, StringComparer.Ordinal);

        foreach (var r in reviews)
        {
            var row = new List<string> { r.EntryName };
            foreach (var cat in categories)
            {
                row.Add(r.CategoryScores.TryGetValue(cat.Id, out var s)
                    ? s.ToString("F1")
                    : string.Empty);
            }
            row.Add(r.TotalScore.ToString("F1"));
            row.Add(SummarizeComments(r, categoriesById, subCriteriaById));
            yield return row;
        }
    }

    private static string SummarizeComments(
        EntryReview review,
        IReadOnlyDictionary<string, Category> categoriesById,
        IReadOnlyDictionary<string, SubCriterion> subCriteriaById)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(review.ProjectComment))
        {
            parts.Add($"[项目总备注] {review.ProjectComment.Trim()}");
        }

        parts.AddRange(review.CategoryComments
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => categoriesById.TryGetValue(kv.Key, out var category)
                ? $"[{category.Name}] {kv.Value!.Trim()}"
                : $"[{kv.Key}] {kv.Value!.Trim()}"));

        parts.AddRange(review.Scores
            .Where(e => !string.IsNullOrWhiteSpace(e.Comment))
            .OrderBy(e => e.SubCriterionId, StringComparer.Ordinal)
            .Select(e => subCriteriaById.TryGetValue(e.SubCriterionId, out var sub)
                ? $"[{sub.Id} {sub.Title}] {e.Comment!.Trim()}"
                : $"[{e.SubCriterionId}] {e.Comment!.Trim()}"));

        return string.Join("; ", parts);
    }

}
