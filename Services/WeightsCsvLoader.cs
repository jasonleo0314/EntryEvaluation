using System.Globalization;
using System.Text;

namespace EntryEvaluation.Services;

/// <summary>
/// 加载/保存子项权重 CSV。列：SubId, Weight。
/// 权重既可作为默认值供评委微调，也允许加和不为 1（由 WeightsCollector 归一化）。
/// </summary>
public static class WeightsCsvLoader
{
    public const string DefaultFileName = "Weights.csv";

    private static readonly string[] ExpectedHeader = ["SubId", "Weight"];

    public static IReadOnlyDictionary<string, double> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rows = CsvParser.ParseFile(path);
        return Parse(rows, source: path);
    }

    public static IReadOnlyDictionary<string, double> LoadFromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        using var sr = new StringReader(csvText);
        return Parse(CsvParser.Parse(sr), source: "<text>");
    }

    public static IReadOnlyDictionary<string, double> Parse(
        List<List<string>> rows, string source = "<csv>")
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

        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count < ExpectedHeader.Length)
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行字段数 {row.Count} 少于期望 {ExpectedHeader.Length}。");
            }
            var subId = row[0].Trim();
            var weightText = row[1].Trim();
            if (string.IsNullOrEmpty(subId))
            {
                throw new InvalidDataException($"{source}: 第 {i + 1} 行 SubId 为空。");
            }
            if (!double.TryParse(weightText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var w))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 Weight 无法解析: '{weightText}'。");
            }
            if (w < 0)
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 Weight 不能为负: {w}。");
            }
            if (!dict.TryAdd(subId, w))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 SubId='{subId}' 重复。");
            }
        }
        return dict;
    }

    public static void Save(string path, IReadOnlyDictionary<string, double> weights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(weights);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var rows = new List<IEnumerable<string>> { ExpectedHeader };
        foreach (var kv in weights)
        {
            rows.Add([kv.Key, kv.Value.ToString("F4", CultureInfo.InvariantCulture)]);
        }
        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(true));
        CsvParser.Write(sw, rows);
    }

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
