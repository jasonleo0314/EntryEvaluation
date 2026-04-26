using EntryEvaluation.Models;
using EntryEvaluation.Services;
using Spectre.Console;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;
AnsiConsole.Profile.Encoding = System.Text.Encoding.UTF8;

// 支持参数：--config <path> --criteria <path> --Entries <path> --weights <path> --progress <path>
var prompt = new SpectrePrompt();
ReviewSettings settings;
var configPath = ArgValue(args, "--config") ?? ReviewSettings.ResolveDefaultPath();
try
{
    settings = ReviewSettings.Load(configPath);
}
catch (IOException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 加载配置失败：{Markup.Escape(ex.Message)}[/]");
    return 1;
}
catch (UnauthorizedAccessException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 加载配置失败：{Markup.Escape(ex.Message)}[/]");
    return 1;
}
catch (InvalidDataException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 加载配置失败：{Markup.Escape(ex.Message)}[/]");
    return 1;
}
catch (JsonException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 加载配置失败：{Markup.Escape(ex.Message)}[/]");
    return 1;
}

var criteriaPath = ArgValue(args, "--criteria") ?? ResolveDefaultDataPath(settings.Paths.CriteriaFileName);
var toolsPath = ArgValue(args, "--Entries") ?? ResolveDefaultDataPath(settings.Paths.EntriesFileName);
var weightsPath = ArgValue(args, "--weights") ?? ResolveDefaultDataPath(settings.Paths.WeightsFileName);
var configuredOutPath = ArgValue(args, "--out");
var outPath = configuredOutPath
    ?? BuildRawOutputPath(settings.Paths);
var configuredStandardizedOutPath = ArgValue(args, "--standardized-out");
var standardizedOutPath = configuredStandardizedOutPath
    ?? BuildStandardizedOutputPath(outPath, settings.Paths.StandardizedResultsSuffix);
var progressPath = ArgValue(args, "--progress")
    ?? Path.Combine(AppContext.BaseDirectory, settings.Paths.ProgressFileName);

CriteriaSet? criteriaSet = null;
IReadOnlyList<Entry> entries = [];
var summary = new List<EntryReview>();
var resultWritten = false;
var standardizedResultWritten = false;
var shutdownRequested = false;
var resultWriteLock = new object();
var resultsDirty = false;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdownRequested = true;
    prompt.WriteLine();
    prompt.WriteMarkupLine("[yellow]收到中断请求，正在保存已完成的输出结果。[/]");
    TryWriteResultsSnapshot("中断时");
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => TryWriteResultsSnapshot("进程退出时");

try
{
    var entryNoun = settings.Display.EntryNounSingular;

    // 横幅
    AnsiConsole.Write(new Rule($"[bold cyan]{Markup.Escape(settings.Display.AppTitle)}[/]").RuleStyle("cyan dim"));
    AnsiConsole.WriteLine();

    // 文件路径信息面板
    var infoPanel = new Table().Border(TableBorder.None).HideHeaders()
        .AddColumn(new TableColumn(string.Empty).Width(20))
        .AddColumn(new TableColumn(string.Empty));
    infoPanel.AddRow("[grey]配置文件[/]",        $"[grey]{Markup.Escape(configPath)}[/]");
    infoPanel.AddRow("[grey]评分目录 CSV[/]",     $"[grey]{Markup.Escape(criteriaPath)}[/]");
    infoPanel.AddRow($"[grey]{Markup.Escape(entryNoun)}清单 CSV[/]", $"[grey]{Markup.Escape(toolsPath)}[/]");
    infoPanel.AddRow("[grey]默认权重 CSV[/]",     $"[grey]{Markup.Escape(weightsPath)}[/]");
    infoPanel.AddRow("[grey]未标准化输出[/]",     $"[grey]{Markup.Escape(outPath)}[/]");
    infoPanel.AddRow("[grey]标准化输出[/]",       $"[grey]{Markup.Escape(standardizedOutPath)}[/]");
    infoPanel.AddRow("[grey]进度文件[/]",         $"[grey]{Markup.Escape(progressPath)}[/]");
    AnsiConsole.Write(infoPanel);

    IReadOnlyDictionary<string, double> defaultWeights;
    try
    {
        criteriaSet = CriteriaCsvLoader.Load(criteriaPath);
        entries = EntriesCsvLoader.Load(toolsPath);
        defaultWeights = WeightsCsvLoader.Load(weightsPath);
    }
    catch (IOException ex)
    {
        prompt.WriteMarkupLine($"[red]✗ 加载 CSV 失败：{Markup.Escape(ex.Message)}[/]");
        return 1;
    }
    catch (UnauthorizedAccessException ex)
    {
        prompt.WriteMarkupLine($"[red]✗ 加载 CSV 失败：{Markup.Escape(ex.Message)}[/]");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        prompt.WriteMarkupLine($"[red]✗ 加载 CSV 失败：{Markup.Escape(ex.Message)}[/]");
        return 1;
    }
    catch (ArgumentException ex)
    {
        prompt.WriteMarkupLine($"[red]✗ 加载 CSV 失败：{Markup.Escape(ex.Message)}[/]");
        return 1;
    }

    var totalMax = criteriaSet.Categories.Sum(c => c.MaxPoints);
    prompt.WriteMarkupLine(
        $"[green]✓ 已加载：[bold]{criteriaSet.Categories.Count}[/] 个大项 / [bold]{criteriaSet.SubCriteria.Count}[/] 个子项 / 满分合计 [bold]{totalMax}[/][/]");
    prompt.WriteMarkupLine(
        $"[grey]打分方式：每个子项 {settings.Scoring.MinimumScore}~{settings.Scoring.MaximumScore} 分手填；中途输出未标准化分数，全部{Markup.Escape(entryNoun)}完成后另行输出标准化计分表[/]");

    var auditDir = Path.Combine(AppContext.BaseDirectory, settings.Paths.AuditDirectoryName);
    prompt.WriteMarkupLine($"[grey]审计目录：{Markup.Escape(auditDir)}[/]");
    prompt.WriteLine();

    var progressStore = new ProgressStore(progressPath);
    var progress = progressStore.Load();
    summary.AddRange(progress.CompletedReviews);

    WeightSnapshot weights;
    var resumeReview = TryGetResumeReview(prompt, progress, entries);
    if (resumeReview is not null)
    {
        weights = new WeightSnapshot(resumeReview.RawWeights, resumeReview.FinalWeights);
        prompt.WriteMarkupLine("[green]✓ 已使用恢复点中的权重快照继续评审。[/]");
    }
    else
    {
        var collector = new WeightsCollector(prompt, criteriaSet.Categories, criteriaSet.SubCriteria);
        weights = collector.Collect(defaultWeights);
    }

    var workflow = new ReviewWorkflow(
        prompt, criteriaSet.Categories, criteriaSet.SubCriteria, weights, auditDir, settings.Scoring, settings.Display);

    if (resumeReview is not null && !shutdownRequested)
    {
        var resumedTool = new Entry(resumeReview.EntryName);
        var review = workflow.ReviewOne(
            resumedTool,
            resumeReview,
            current => progressStore.Save(new ReviewProgress(summary, current)));
        UpsertReview(summary, review);
        progressStore.Save(new ReviewProgress(summary, null));
        MarkResultsDirty();
        prompt.WriteMarkupLine("[green]✓ 已完成恢复评审，并更新进度文件。[/]");
        if (AllToolsReviewed(entries, summary))
        {
            if (!TryWriteFinalResults())
            {
                return 2;
            }

            prompt.WriteMarkupLine($"[green bold]✓ 所有{Markup.Escape(entryNoun)}均已完成评审，已重新写出最终计分表。可继续重新评审，或输入 q 退出。[/]");
        }
    }

    // 参选项目列表
    prompt.WriteLine();
    var entryTable = new Table()
        .Border(TableBorder.Rounded)
        .Title($"[bold cyan]{Markup.Escape(entryNoun)}列表（共 {entries.Count} 项）[/]")
        .AddColumn(new TableColumn("[bold]序号[/]").RightAligned())
        .AddColumn(new TableColumn($"[bold]{Markup.Escape(entryNoun)}名称[/]"));
    var completedNames = summary.Select(r => r.EntryName).ToHashSet(StringComparer.Ordinal);
    for (var i = 0; i < entries.Count; i++)
    {
        var name = entries[i].Name;
        var label = completedNames.Contains(name)
            ? $"[green]{Markup.Escape(name)} ✓[/]"
            : Markup.Escape(name);
        entryTable.AddRow($"[grey]{i + 1}[/]", label);
    }
    AnsiConsole.Write(entryTable);

    while (!shutdownRequested)
    {
        prompt.WriteLine();
        prompt.WriteMarkupLine($"请选择[bold]{Markup.Escape(entryNoun)}[/]序号开始评审 [grey]（输入 [bold]q[/] 退出，[bold]s[/] 查看汇总）[/]：");
        prompt.Write("> ");
        var line = prompt.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) continue;
        line = line.Trim();
        if (line.Equals("q", StringComparison.OrdinalIgnoreCase)) break;
        if (line.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            PrintSummary(prompt, summary, entries);
            continue;
        }
        if (!int.TryParse(line, out var idx) || idx < 1 || idx > entries.Count)
        {
            prompt.WriteMarkupLine($"[red]无效输入，请输入 1~{entries.Count} 或 q/s。[/]");
            continue;
        }

        var review = workflow.ReviewOne(
            entries[idx - 1],
            resume: null,
            current => progressStore.Save(new ReviewProgress(summary, current)));
        UpsertReview(summary, review);
        progressStore.Save(new ReviewProgress(summary, null));
        MarkResultsDirty();
        if (AllToolsReviewed(entries, summary))
        {
            if (!TryWriteFinalResults())
            {
                return 2;
            }

            prompt.WriteMarkupLine($"[green bold]✓ 所有{Markup.Escape(entryNoun)}均已完成评审，已重新写出最终计分表。可继续重新评审，或输入 q 退出。[/]");
        }
    }

    PrintSummary(prompt, summary, entries);
    if (AllToolsReviewed(entries, summary) && resultsDirty)
    {
        if (!TryWriteFinalResults())
        {
            return 2;
        }
    }
    else if (!AllToolsReviewed(entries, summary) && !TryWriteResultsSnapshot("退出时"))
    {
        return 2;
    }

    prompt.WriteMarkupLine("[bold cyan]再见。[/]");
    return shutdownRequested ? 130 : 0;
}
catch (IOException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 程序异常退出：{Markup.Escape(ex.Message)}[/]");
    TryWriteResultsSnapshot("异常退出时");
    return 3;
}
catch (UnauthorizedAccessException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 程序异常退出：{Markup.Escape(ex.Message)}[/]");
    TryWriteResultsSnapshot("异常退出时");
    return 3;
}
catch (InvalidOperationException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 程序异常退出：{Markup.Escape(ex.Message)}[/]");
    TryWriteResultsSnapshot("异常退出时");
    return 3;
}
catch (ArgumentException ex)
{
    prompt.WriteMarkupLine($"[red]✗ 程序异常退出：{Markup.Escape(ex.Message)}[/]");
    TryWriteResultsSnapshot("异常退出时");
    return 3;
}

static string? ArgValue(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }
    return null;
}

static string ResolveDefaultDataPath(string fileName)
{
    foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            return path;
        }
    }

    return Path.Combine(AppContext.BaseDirectory, fileName);
}

static string BuildRawOutputPath(PathSettings paths)
{
    var timestamp = DateTimeOffset.Now.ToString(paths.OutputTimestampFormat);
    var fileName = paths.RawResultsFileNamePattern.Replace("{timestamp}", timestamp, StringComparison.Ordinal);
    return Path.Combine(AppContext.BaseDirectory, fileName);
}

static string BuildStandardizedOutputPath(string rawOutputPath, string suffix)
{
    var dir = Path.GetDirectoryName(rawOutputPath);
    var fileName = Path.GetFileNameWithoutExtension(rawOutputPath);
    var extension = Path.GetExtension(rawOutputPath);
    return Path.Combine(
        string.IsNullOrEmpty(dir) ? string.Empty : dir,
        $"{fileName}{suffix}{extension}");
}

static string BuildUniqueOutputPath(string path)
{
    if (!File.Exists(path))
    {
        return path;
    }

    var dir = Path.GetDirectoryName(path);
    var fileName = Path.GetFileNameWithoutExtension(path);
    var extension = Path.GetExtension(path);
    var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff");
    var candidate = Path.Combine(
        string.IsNullOrEmpty(dir) ? string.Empty : dir,
        $"{fileName}_{timestamp}{extension}");
    var index = 1;
    while (File.Exists(candidate))
    {
        candidate = Path.Combine(
            string.IsNullOrEmpty(dir) ? string.Empty : dir,
            $"{fileName}_{timestamp}_{index}{extension}");
        index++;
    }

    return candidate;
}

static void UpsertReview(List<EntryReview> reviews, EntryReview review)
{
    var existingIndex = reviews.FindIndex(r => string.Equals(r.EntryName, review.EntryName, StringComparison.Ordinal));
    if (existingIndex >= 0)
    {
        reviews[existingIndex] = review;
        return;
    }

    reviews.Add(review);
}

void MarkResultsDirty()
{
    resultWritten = false;
    standardizedResultWritten = false;
    resultsDirty = true;
}

void PrintSummary(
    IPrompt p,
    IReadOnlyList<EntryReview> all,
    IReadOnlyList<Entry>? entries = null,
    string title = "评审汇总")
{
    var entryNoun = settings.Display.EntryNounSingular;
    p.WriteLine();
    p.WriteRule(title);

    var completedNames = all
        .Select(r => r.EntryName)
        .ToHashSet(StringComparer.Ordinal);
    var totalCount = entries?.Count ?? all.Count;
    var entryOrder = entries?
        .Select((entry, index) => new { entry.Name, Sequence = index + 1 })
        .ToDictionary(item => item.Name, item => item.Sequence, StringComparer.Ordinal);
    var pending = entries is null
        ? Array.Empty<Entry>()
        : entries.Where(t => !completedNames.Contains(t.Name)).ToArray();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .Title($"[bold]已完成 {all.Count}/{totalCount} 项[/]")
        .AddColumn(new TableColumn("[bold]序号[/]").RightAligned())
        .AddColumn(new TableColumn("[bold]排名[/]").RightAligned())
        .AddColumn(new TableColumn($"[bold]{Markup.Escape(entryNoun)}名称[/]"))
        .AddColumn(new TableColumn("[bold]总分[/]").RightAligned())
        .AddColumn(new TableColumn("[bold]状态[/]").Centered());

    if (all.Count == 0)
    {
        table.AddRow("[grey]-[/]", "[grey]-[/]", $"[grey]（暂无已完成{Markup.Escape(entryNoun)}）[/]", "[grey]-[/]", "[grey]-[/]");
    }
    else
    {
        var ranked = all.OrderByDescending(x => x.TotalScore).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i];
            var sequenceLabel = entryOrder is not null && entryOrder.TryGetValue(r.EntryName, out var sequence)
                ? $"[grey]{sequence}[/]"
                : "[grey]-[/]";
            var rankLabel = i == 0 ? "[gold1 bold]🥇 1[/]" : i == 1 ? "[silver bold]🥈 2[/]" : i == 2 ? "[#cd7f32 bold]🥉 3[/]" : $"[grey]{i + 1}[/]";
            table.AddRow(sequenceLabel, rankLabel, Markup.Escape(r.EntryName), $"[cyan bold]{r.TotalScore:F1}[/]", "[green]✓ 已完成[/]");
        }
    }

    if (entries is not null && pending.Length > 0)
    {
        foreach (var e in pending)
        {
            var sequenceLabel = entryOrder!.TryGetValue(e.Name, out var sequence)
                ? $"[grey]{sequence}[/]"
                : "[grey]-[/]";
            table.AddRow(sequenceLabel, "[grey]-[/]", Markup.Escape(e.Name), "[grey]-[/]", "[yellow]○ 待评审[/]");
        }
    }

    AnsiConsole.Write(table);
}

bool TryWriteResultsSnapshot(string reason)
{
    lock (resultWriteLock)
    {
        if (resultWritten || criteriaSet is null)
        {
            return true;
        }

        try
        {
            outPath = BuildUniqueOutputPath(configuredOutPath ?? BuildRawOutputPath(settings.Paths));
            ResultsCsvWriter.Write(outPath, criteriaSet.Categories, criteriaSet.SubCriteria, summary, settings.Display);
            resultWritten = true;
            standardizedResultWritten = false;
            prompt.WriteMarkupLine($"[green]✓ {Markup.Escape(reason)}汇总 CSV 已写出：{Markup.Escape(outPath)}[/]");
            return true;
        }
        catch (IOException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ {Markup.Escape(reason)}写出汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ {Markup.Escape(reason)}写出汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
        catch (ArgumentException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ {Markup.Escape(reason)}写出汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }
}

bool TryWriteFinalResults()
{
    if (!TryWriteResultsSnapshot("最终"))
    {
        return false;
    }

    lock (resultWriteLock)
    {
        if (standardizedResultWritten || criteriaSet is null)
        {
            return true;
        }

        try
        {
            var standardizedReviews = ScoreStandardizer.Standardize(
                criteriaSet.Categories,
                summary,
                settings.Scoring,
                settings.Standardization);
            standardizedOutPath = BuildUniqueOutputPath(
                configuredStandardizedOutPath
                ?? BuildStandardizedOutputPath(outPath, settings.Paths.StandardizedResultsSuffix));
            ResultsCsvWriter.Write(
                standardizedOutPath,
                criteriaSet.Categories,
                criteriaSet.SubCriteria,
                standardizedReviews,
                settings.Display);
            standardizedResultWritten = true;
            resultsDirty = false;
            prompt.WriteMarkupLine($"[green]✓ 最终标准化汇总 CSV 已写出：{Markup.Escape(standardizedOutPath)}[/]");
            PrintSummary(prompt, standardizedReviews, entries, "标准化汇总");
            return true;
        }
        catch (IOException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ 最终写出标准化汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ 最终写出标准化汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ 最终写出标准化汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
        catch (ArgumentException ex)
        {
            prompt.WriteMarkupLine($"[red]✗ 最终写出标准化汇总 CSV 失败：{Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }
}

static bool AllToolsReviewed(IReadOnlyList<Entry> Entries, IReadOnlyList<EntryReview> reviews)
{
    var completed = reviews
        .Select(r => r.EntryName)
        .ToHashSet(StringComparer.Ordinal);

    return Entries.All(t => completed.Contains(t.Name));
}

static InProgressReview? TryGetResumeReview(
    IPrompt prompt,
    ReviewProgress progress,
    IReadOnlyList<Entry> Entries)
{
    var current = progress.CurrentReview;
    if (current is null)
    {
        return null;
    }

    var stillExists = Entries.Any(t =>
        string.Equals(t.Name, current.EntryName, StringComparison.Ordinal));
    if (!stillExists)
    {
        prompt.WriteMarkupLine($"[yellow]⚠ 发现恢复点，但清单中已不存在：{Markup.Escape(current.EntryName)}。将重新开始。[/]");
            return null;
        }

        prompt.WriteMarkupLine($"[cyan]发现未完成评审：[bold]{Markup.Escape(current.EntryName)}[/]，已录入 {current.Scores.Count} 个子项。[/]");
        prompt.WriteMarkupLine("是否恢复该评审？[bold](y/N)[/]：");
        prompt.Write("> ");
        var line = prompt.ReadLine();
        return string.Equals(line?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            ? current
            : null;
}
