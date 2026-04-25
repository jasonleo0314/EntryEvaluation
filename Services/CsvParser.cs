using System.Text;

namespace EntryEvaluation.Services;

/// <summary>
/// 极简 CSV 解析/写入：支持 UTF-8 BOM、双引号包裹字段、字段内 "" 转义、字段内逗号与换行。
/// 不依赖任何第三方包，便于在评审环境离线部署。
/// </summary>
public static class CsvParser
{
    /// <summary>把 CSV 文本解析为二维行集合。</summary>
    public static List<List<string>> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            var c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when !fieldStarted:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r':
                    break; // swallow, expect \n next
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (fieldStarted || row.Count > 0 || field.Length > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // 剥离首行首字段的 UTF-8 BOM
        if (rows.Count > 0 && rows[0].Count > 0 && rows[0][0].Length > 0
            && rows[0][0][0] == '\uFEFF')
        {
            rows[0][0] = rows[0][0][1..];
        }

        return rows;
    }

    public static List<List<string>> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到 CSV 文件: {path}", path);
        }
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    /// <summary>把单元格写入 CSV，必要时加引号并转义内部双引号。</summary>
    public static string EscapeField(string value)
    {
        value ??= string.Empty;
        var needQuote = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (!needQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static void Write(TextWriter writer, IEnumerable<IEnumerable<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var r in rows)
        {
            var first = true;
            foreach (var f in r)
            {
                if (!first) writer.Write(',');
                writer.Write(EscapeField(f));
                first = false;
            }
            writer.Write('\n');
        }
    }
}
