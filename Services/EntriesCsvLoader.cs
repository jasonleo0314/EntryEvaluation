using System.Text;
using EntryEvaluation.Models;

namespace EntryEvaluation.Services;

/// <summary>
/// 加载/保存待评参赛作品清单 CSV。CSV 列：EntryName。
/// </summary>
public static class EntriesCsvLoader
{
    public const string DefaultFileName = "Entries.csv";

    private static readonly string[] ExpectedHeader = ["EntryName"];

    public static IReadOnlyList<Entry> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rows = CsvParser.ParseFile(path);
        return Parse(rows, source: path);
    }

    public static IReadOnlyList<Entry> LoadFromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        using var sr = new StringReader(csvText);
        return Parse(CsvParser.Parse(sr), source: "<text>");
    }

    public static IReadOnlyList<Entry> Parse(List<List<string>> rows, string source = "<csv>")
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

        var Entries = new List<Entry>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count < ExpectedHeader.Length)
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行字段数 {row.Count} 少于期望 {ExpectedHeader.Length}。");
            }
            var name = row[0].Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException(
                    $"{source}: 第 {i + 1} 行 EntryName 不能为空。");
            }
            Entries.Add(new Entry(name));
        }
        return Entries;
    }

    public static void Save(string path, IEnumerable<Entry> Entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(Entries);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var rows = new List<IEnumerable<string>> { ExpectedHeader };
        foreach (var t in Entries) rows.Add([t.Name]);

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
