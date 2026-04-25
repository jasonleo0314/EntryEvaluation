using System.Globalization;
using System.Text;
using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 加载/保存评分目录（大项 + 子项）的 CSV。
/// CSV 列：
///   CategoryId, CategoryName, CategoryMaxPoints, SubId, Phase, SubTitle, SubDescription
/// 同一 CategoryId 的多行必须给出相同的 CategoryName 与 CategoryMaxPoints。
/// 大项满分合计必须为 100；子项权重由独立的 Weights.csv 提供。
/// </summary>
public static class CriteriaCsvLoader
{
    public const string DefaultFileName = "Criteria.csv";

    private static readonly string[] ExpectedHeader =
    [
        "CategoryId", "CategoryName", "CategoryMaxPoints",
        "SubId", "Phase", "SubTitle", "SubDescription"
    ];

    public static CriteriaSet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rows = CsvParser.ParseFile(path);
        return Parse(rows, source: path);
    }

    public static CriteriaSet LoadFromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        using var sr = new StringReader(csvText);
        return Parse(CsvParser.Parse(sr), source: "<text>");
    }

    public static CriteriaSet Parse(List<List<string>> rows, string source = "<csv>")
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            throw new InvalidDataException($"{source}: CSV 内容为空。");
        }

        var header = rows[0].Select(h => h.Trim()).ToList();
        if (header.Count < ExpectedHeader.Length
            || !header.Take(ExpectedHeader.Length)
                      .SequenceEqual(ExpectedHeader, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{source}: CSV 表头不匹配，期望 [{string.Join(", ", ExpectedHeader)}]，" +
                $"实际 [{string.Join(", ", header)}]。");
        }

        var categoriesById = new Dictionary<string, Category>(StringComparer.Ordinal);
        var categoryOrder = new List<string>();
        var subs = new List<SubCriterion>();
        var seenSubIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue; // 跳过空行
            if (row.Count < ExpectedHeader.Length)
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行字段数 {row.Count} 少于期望 {ExpectedHeader.Length}。");
            }

            var catId = row[0].Trim();
            var catName = row[1].Trim();
            var catMaxText = row[2].Trim();
            var subId = row[3].Trim();
            var phaseText = row[4].Trim();
            var subTitle = row[5].Trim();
            var subDesc = row[6].Trim();

            if (string.IsNullOrEmpty(catId) || string.IsNullOrEmpty(subId))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 CategoryId/SubId 不能为空。");
            }

            if (!double.TryParse(catMaxText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var maxPts))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 CategoryMaxPoints 无法解析: '{catMaxText}'。");
            }

            if (!Enum.TryParse<WorkflowPhase>(phaseText, ignoreCase: true, out var phase))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 Phase 无效: '{phaseText}'，" +
                    $"取值范围: {string.Join("/", Enum.GetNames<WorkflowPhase>())}。");
            }

            if (categoriesById.TryGetValue(catId, out var existing))
            {
                if (!string.Equals(existing.Name, catName, StringComparison.Ordinal)
                    || Math.Abs(existing.MaxPoints - maxPts) > 1e-9)
                {
                    throw new InvalidDataException(
                        $"{source}: 第 {i + 1} 行 CategoryId='{catId}' 的名称/分值与首次出现不一致。");
                }
            }
            else
            {
                categoriesById[catId] = new Category(catId, catName, maxPts);
                categoryOrder.Add(catId);
            }

            if (!seenSubIds.Add(subId))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 SubId='{subId}' 重复。");
            }

            subs.Add(new SubCriterion(subId, catId, phase, subTitle, subDesc));
        }

        var categories = categoryOrder.Select(id => categoriesById[id]).ToList();
        CriteriaCatalog.Validate(categories, subs);
        return new CriteriaSet(categories, subs);
    }

    public static void Save(string path, CriteriaSet set)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(set);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var rows = new List<IEnumerable<string>> { ExpectedHeader };
        var catById = set.Categories.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var s in set.SubCriteria)
        {
            var c = catById[s.CategoryId];
            rows.Add(
            [
                c.Id, c.Name,
                c.MaxPoints.ToString(CultureInfo.InvariantCulture),
                s.Id, s.Phase.ToString(), s.Title, s.Description
            ]);
        }

        // 写入带 BOM 的 UTF-8，便于 Excel 直接打开识别中文。
        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(true));
        CsvParser.Write(sw, rows);
    }

    /// <summary>解析候选目录列表：当前工作目录、可执行所在目录。</summary>
    public static string ResolveDefaultPath()
    {
        foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var p = Path.Combine(dir, DefaultFileName);
            if (File.Exists(p)) return p;
        }
        return Path.Combine(AppContext.BaseDirectory, DefaultFileName);
    }
}
