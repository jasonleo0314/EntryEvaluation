using EntryEvaluation.Models;
using Spectre.Console;

namespace EntryEvaluation.Services;

/// <summary>
/// 交互式评审流程：每个参赛作品对应一张评分表，所有子项以一张表格统一展示，
/// 按行依次提示打分与备注；当某大项全部子项打完后即就地追加大项备注；
/// 全部子项完成后再录入项目总备注。整屏式重绘，参考 Claude Code 风格。
/// </summary>
public sealed class ReviewWorkflow
{
    private readonly IPrompt _prompt;
    private readonly IReadOnlyList<Category> _categories;
    private readonly IReadOnlyList<SubCriterion> _subCriteria;
    private readonly IReadOnlyDictionary<string, double> _rawWeights;
    private readonly IReadOnlyDictionary<string, double> _finalWeights;
    private readonly string _auditDirectory;
    private readonly ScoringSettings _scoring;
    private readonly DisplaySettings _display;

    public ReviewWorkflow(
        IPrompt prompt,
        IReadOnlyList<Category> categories,
        IReadOnlyList<SubCriterion> subCriteria,
        WeightSnapshot weights,
        string auditDirectory,
        ScoringSettings? scoring = null,
        DisplaySettings? display = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCriteria);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditDirectory);
        scoring ??= new ScoringSettings();
        display ??= new DisplaySettings();
        scoring.Validate();
        display.Validate();
        CriteriaCatalog.Validate(categories, subCriteria, scoring);
        WeightsCollector.ValidateCoverage(subCriteria, weights.RawWeights);
        WeightsCollector.ValidateCoverage(subCriteria, weights.NormalizedWeights);
        _prompt = prompt;
        _categories = categories;
        _subCriteria = subCriteria;
        _rawWeights = weights.RawWeights;
        _finalWeights = weights.NormalizedWeights;
        _auditDirectory = auditDirectory;
        _scoring = scoring;
        _display = display;
    }

    private sealed class ReviewRow
    {
        public required SubCriterion Sub { get; init; }
        public required Category Category { get; init; }
        public int? Score { get; set; }
        public string? Comment { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
    }

    public EntryReview ReviewOne(
        Entry Entry,
        InProgressReview? resume = null,
        Action<InProgressReview>? saveProgress = null)
    {
        ArgumentNullException.ThrowIfNull(Entry);

        var startedAt = resume?.StartedAt ?? DateTimeOffset.Now;
        var safeName = MakeSafeFileName($"{Entry.Name}_{startedAt:yyyyMMdd_HHmmss}");
        var auditPath = Path.Combine(_auditDirectory, safeName + ".jsonl");
        using var audit = new AuditLogger(auditPath);

        audit.Log("Begin", new { Entry.Name, StartedAt = startedAt });
        if (resume is not null)
        {
            audit.LogProgressRestored(resume);
        }
        audit.LogWeights(_subCriteria, _rawWeights, _finalWeights);

        var rows = _subCriteria
            .OrderBy(s => (int)s.Phase)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => new ReviewRow
            {
                Sub = s,
                Category = _categories.First(c => c.Id == s.CategoryId)
            })
            .ToList();

        var entries = new List<ScoreEntry>();
        var categoryComments = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (resume is not null)
        {
            foreach (var sc in resume.Scores)
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.Sub.Id, sc.SubCriterionId, StringComparison.Ordinal));
                if (row is null)
                {
                    continue;
                }
                row.Score = sc.RawScore;
                row.Comment = sc.Comment;
                row.RecordedAt = sc.RecordedAt;
                entries.Add(sc);
            }

            foreach (var kv in resume.CategoryComments)
            {
                categoryComments[kv.Key] = kv.Value;
            }
        }

        // 首屏：渲染评分表 + 头部信息（“开始评审：xxx”）
        Render(Entry, rows, categoryComments, activeIndex: -1, auditPath, status: resume is not null ? "已恢复进度，继续填写" : "开始填写评分表");
        _prompt.WriteRule($"开始评审：{Entry.Name}");
        _prompt.WriteMarkupLine($"[grey]审计文件：{Markup.Escape(auditPath)}[/]");
        if (_display.ShowWeightSummary)
        {
            PrintWeightSummary();
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.Score.HasValue)
            {
                MaybeAskCategoryComment(row.Category.Id, rows, categoryComments, audit, saveProgress, Entry, startedAt, entries);
                continue;
            }

            Render(Entry, rows, categoryComments, activeIndex: i, auditPath, status: $"正在评分：{row.Sub.Id} {row.Sub.Title}");
            PrintActiveSub(row);

            var score = AskScore(row.Sub.Id);
            var comment = AskComment();
            var recordedAt = DateTimeOffset.Now;

            row.Score = score;
            row.Comment = comment;
            row.RecordedAt = recordedAt;

            var entry = new ScoreEntry(row.Sub.Id, score, recordedAt, comment);
            entries.Add(entry);
            audit.LogScore(row.Sub, entry, _finalWeights[row.Sub.Id]);
            SaveProgress(saveProgress, Entry, startedAt, entries, categoryComments, projectComment: null);

            // 行填写完毕，重绘一次以反馈。
            Render(Entry, rows, categoryComments, activeIndex: i, auditPath, status: $"✓ 已记录 {row.Sub.Id} = {score}");

            MaybeAskCategoryComment(row.Category.Id, rows, categoryComments, audit, saveProgress, Entry, startedAt, entries);
        }

        // 兜底：处理 resume 场景下还未询问的大项备注。
        foreach (var cat in _categories)
        {
            MaybeAskCategoryComment(cat.Id, rows, categoryComments, audit, saveProgress, Entry, startedAt, entries);
        }

        var projectComment = resume?.ProjectComment;
        if (projectComment is null)
        {
            Render(Entry, rows, categoryComments, activeIndex: -1, auditPath, status: "全部子项与大项备注已完成，请填写项目总备注");
            projectComment = AskProjectComment();
            audit.LogProjectComment(projectComment);
            SaveProgress(saveProgress, Entry, startedAt, entries, categoryComments, projectComment);
        }

        var calculator = new ScoreCalculator(_categories, _subCriteria, _finalWeights, _scoring);
        var rawScores = rows.ToDictionary(r => r.Sub.Id, r => r.Score!.Value, StringComparer.Ordinal);
        var result = calculator.Compute(rawScores);
        audit.LogResult(result);

        Render(Entry, rows, categoryComments, activeIndex: -1, auditPath, status: "✓ 评审完成");
        PrintResult(Entry, result);

        return new EntryReview(
            Entry.Name,
            startedAt,
            entries,
            result.CategoryScores,
            result.TotalScore,
            categoryComments,
            projectComment,
            _finalWeights);
    }

    private void MaybeAskCategoryComment(
        string categoryId,
        IReadOnlyList<ReviewRow> rows,
        Dictionary<string, string?> categoryComments,
        AuditLogger audit,
        Action<InProgressReview>? saveProgress,
        Entry Entry,
        DateTimeOffset startedAt,
        List<ScoreEntry> entries)
    {
        if (categoryComments.ContainsKey(categoryId))
        {
            return;
        }

        var allDone = rows.Where(r => string.Equals(r.Category.Id, categoryId, StringComparison.Ordinal))
            .All(r => r.Score.HasValue);
        if (!allDone)
        {
            return;
        }

        var category = _categories.First(c => c.Id == categoryId);
        Render(Entry, rows, categoryComments, activeIndex: -1, auditPath: null,
            status: $"大项【{category.Name}】已全部打分，请填写大项备注");

        var comment = AskCategoryComment(category);
        categoryComments[categoryId] = comment;
        audit.LogCategoryComment(category, comment);
        SaveProgress(saveProgress, Entry, startedAt, entries, categoryComments, projectComment: null);
    }

    private void Render(
        Entry entry,
        IReadOnlyList<ReviewRow> rows,
        IReadOnlyDictionary<string, string?> categoryComments,
        int activeIndex,
        string? auditPath,
        string? status)
    {
        _prompt.Clear();

        var completed = rows.Count(r => r.Score.HasValue);
        var headerLines = new List<string>
        {
            $"[bold cyan]{Markup.Escape(entry.Name)}[/]",
            $"[grey]进度：{completed}/{rows.Count} 个子项 · 大项备注 {categoryComments.Count(kv => kv.Value != null || categoryComments.ContainsKey(kv.Key))}/{_categories.Count}[/]"
        };
        if (!string.IsNullOrWhiteSpace(auditPath))
        {
            headerLines.Add($"[grey]审计：{Markup.Escape(auditPath!)}[/]");
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            headerLines.Add($"[yellow]{Markup.Escape(status!)}[/]");
        }

        var header = new Panel(string.Join('\n', headerLines))
            .Header($"[bold]{Markup.Escape(_display.EntryNounSingular)}评审[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();
        AnsiConsole.Write(header);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]评分表[/]")
            .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
            .AddColumn("[bold]阶段[/]")
            .AddColumn("[bold]大项[/]")
            .AddColumn("[bold]子项[/]")
            .AddColumn("[bold]标题[/]")
            .AddColumn(new TableColumn("[bold]权重[/]").RightAligned())
            .AddColumn(new TableColumn($"[bold]得分(0~{_scoring.MaximumScore})[/]").Centered())
            .AddColumn("[bold]备注[/]");

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var isActive = i == activeIndex;
            var done = r.Score.HasValue;

            var marker = isActive ? "[yellow bold]▶[/]" : done ? "[green]✓[/]" : "[grey]·[/]";
            var idxStyle = isActive ? "yellow" : done ? "green" : "grey";

            var scoreCell = isActive && !done
                ? "[yellow bold]?[/]"
                : done
                    ? $"[{ScoreColor(r.Score!.Value)} bold]{r.Score.Value}[/]"
                    : "[grey]-[/]";

            var commentText = r.Comment is null ? string.Empty : Truncate(r.Comment, 28);
            var commentCell = string.IsNullOrEmpty(commentText)
                ? (done ? "[grey](已跳过)[/]" : "")
                : (done ? $"[grey]{Markup.Escape(commentText)}[/]" : Markup.Escape(commentText));

            table.AddRow(
                $"{marker} [{idxStyle}]{i + 1}[/]",
                Markup.Escape(r.Sub.Phase.ToChinese()),
                Markup.Escape(r.Category.Name),
                Markup.Escape(r.Sub.Id),
                Markup.Escape(r.Sub.Title),
                $"[grey]{_finalWeights[r.Sub.Id]:F4}[/]",
                scoreCell,
                commentCell);
        }
        AnsiConsole.Write(table);

        // 大项备注状态
        var catTable = new Table()
            .Border(TableBorder.Minimal)
            .Title("[bold]大项备注[/]")
            .AddColumn("[bold]大项[/]")
            .AddColumn("[bold]满分[/]")
            .AddColumn("[bold]备注[/]");
        foreach (var cat in _categories)
        {
            string note;
            if (!categoryComments.TryGetValue(cat.Id, out var c))
            {
                note = "[grey](待填)[/]";
            }
            else if (string.IsNullOrWhiteSpace(c))
            {
                note = "[grey](已跳过)[/]";
            }
            else
            {
                note = Markup.Escape(Truncate(c!, 60));
            }

            catTable.AddRow(Markup.Escape(cat.Name), cat.MaxPoints.ToString(), note);
        }
        AnsiConsole.Write(catTable);
    }

    private void PrintActiveSub(ReviewRow row)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(
            $"[grey italic]{Markup.Escape(row.Sub.Description)}[/]\n" +
            $"[grey]归属：{Markup.Escape(row.Category.Name)}（满分 {row.Category.MaxPoints}） · " +
            $"阶段：{Markup.Escape(row.Sub.Phase.ToChinese())} · " +
            $"权重：{_finalWeights[row.Sub.Id]:F4}[/]")
            .Header($"[yellow bold]▶ [[{Markup.Escape(row.Sub.Id)}]] {Markup.Escape(row.Sub.Title)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow);
        AnsiConsole.Write(panel);
    }

    private static string ScoreColor(int score) => score switch
    {
        0 => "red",
        1 => "yellow",
        2 => "cyan",
        _ => "green"
    };

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
        {
            return s;
        }
        return s[..(max - 1)] + "…";
    }

    private void PrintWeightSummary()
    {
        _prompt.WriteLine();
        _prompt.WriteMarkupLine("[bold]当前权重一览[/][grey]（同一大项内权重之和 = 1.0）：[/]");
        foreach (var cat in _categories)
        {
            _prompt.WriteMarkupLine($"  [bold cyan]· {Markup.Escape(cat.Name)}[/][grey]（满分 {cat.MaxPoints}）[/]");
            foreach (var s in _subCriteria.Where(x => x.CategoryId == cat.Id))
            {
                _prompt.WriteMarkupLine(
                    $"      [[{Markup.Escape(s.Id)}]] {Markup.Escape(s.Title)}  " +
                    $"权重 [cyan]{_finalWeights[s.Id]:F4}[/]  " +
                    $"阶段 [grey]{Markup.Escape(s.Phase.ToChinese())}[/]");
            }
        }
    }

    private int AskScore(string subId)
    {
        while (true)
        {
            _prompt.WriteMarkupLine($"  请输入 [bold]{Markup.Escape(subId)}[/] 的分值 [cyan]({FormatScoreChoices()})[/]：");
            _prompt.Write(string.Empty);
            var line = _prompt.ReadLine();
            if (int.TryParse(line, out var v)
                && v >= _scoring.MinimumScore
                && v <= _scoring.MaximumScore)
            {
                return v;
            }
            _prompt.WriteMarkupLine($"  [red]输入无效，请输入 {FormatScoreChoices('、')} 中的一个。[/]");
        }
    }

    private string FormatScoreChoices(char separator = '/') =>
        string.Join(separator, Enumerable.Range(
            _scoring.MinimumScore,
            _scoring.MaximumScore - _scoring.MinimumScore + 1));

    private string? AskComment()
    {
        _prompt.WriteMarkupLine("[grey]  备注（可空，回车跳过）：[/]");
        _prompt.Write(string.Empty);
        var line = _prompt.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    private string? AskCategoryComment(Category category)
    {
        _prompt.WriteMarkupLine($"[grey]  大项备注【{Markup.Escape(category.Name)}】（可空，回车跳过）：[/]");
        _prompt.Write(string.Empty);
        var line = _prompt.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    private string? AskProjectComment()
    {
        _prompt.WriteMarkupLine("[grey]  项目总备注（可空，回车跳过）：[/]");
        _prompt.Write(string.Empty);
        var line = _prompt.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    private void SaveProgress(
        Action<InProgressReview>? saveProgress,
        Entry Entry,
        DateTimeOffset startedAt,
        IReadOnlyList<ScoreEntry> entries,
        IReadOnlyDictionary<string, string?> categoryComments,
        string? projectComment)
    {
        saveProgress?.Invoke(new InProgressReview(
            Entry.Name,
            startedAt,
            entries.ToList(),
            categoryComments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            projectComment,
            _rawWeights,
            _finalWeights));
    }

    private void PrintResult(Entry Entry, ScoringResult result)
    {
        _prompt.WriteLine();
        _prompt.WriteRule($"评审小结：{Entry.Name}");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]大项[/]"))
            .AddColumn(new TableColumn("[bold]得分[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]满分[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]加权率[/]").RightAligned());

        foreach (var cat in _categories)
        {
            var s = result.CategoryScores[cat.Id];
            var r = result.CategoryWeightedRates[cat.Id];
            var rateColor = r >= 0.8 ? "green" : r >= 0.6 ? "yellow" : "red";
            table.AddRow(
                Markup.Escape(cat.Name),
                $"[cyan]{s:F1}[/]",
                cat.MaxPoints.ToString(),
                $"[{rateColor}]{r:P1}[/]");
        }

        AnsiConsole.Write(table);

        var totalColor = result.TotalScore >= _scoring.CategoryTotalPoints * 0.8 ? "green"
            : result.TotalScore >= _scoring.CategoryTotalPoints * 0.6 ? "yellow" : "red";
        _prompt.WriteMarkupLine(
            $"  未标准化总分：[{totalColor} bold]{result.TotalScore:F1}[/] / {_scoring.CategoryTotalPoints}");
        _prompt.WriteMarkupLine(
            $"  [grey italic]标准化分数将在全部{Markup.Escape(_display.EntryNounSingular)}完成评审后统一生成。[/]");
        _prompt.WriteRule();
    }

    private static string MakeSafeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
