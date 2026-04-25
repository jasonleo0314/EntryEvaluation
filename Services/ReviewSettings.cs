using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntryEvaluation.Services;

/// <summary>
/// 应用运行配置，集中管理路径、评分规则、标准化规则和显示参数。
/// </summary>
public sealed record ReviewSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const string DefaultFileName = "appsettings.json";

    public PathSettings Paths { get; init; } = new();

    public ScoringSettings Scoring { get; init; } = new();

    public StandardizationSettings Standardization { get; init; } = new();

    public DisplaySettings Display { get; init; } = new();

    public static ReviewSettings Default { get; } = new();

    public static ReviewSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Default;
        }

        using var stream = File.OpenRead(path);
        var settings = JsonSerializer.Deserialize<ReviewSettings>(stream, JsonOptions)
            ?? throw new InvalidDataException($"配置文件为空或格式无效: {path}");
        settings.Validate();
        return settings;
    }

    public static string ResolveDefaultPath()
    {
        foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var path = Path.Combine(dir, DefaultFileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, DefaultFileName);
    }

    public void Validate()
    {
        Paths.Validate();
        Scoring.Validate();
        Standardization.Validate();
        Display.Validate();
    }
}

/// <summary>
/// 文件与目录配置。
/// </summary>
public sealed record PathSettings
{
    public string CriteriaFileName { get; init; } = "Criteria.csv";

    public string EntriesFileName { get; init; } = "Entries.csv";

    public string WeightsFileName { get; init; } = "Weights.csv";

    public string ProgressFileName { get; init; } = "ReviewProgress.json";

    public string AuditDirectoryName { get; init; } = "Audit";

    public string RawResultsFileNamePattern { get; init; } = "ReviewResults_{timestamp}.csv";

    public string StandardizedResultsSuffix { get; init; } = "_Standardized";

    public string OutputTimestampFormat { get; init; } = "yyyyMMdd_HHmmss";

    public void Validate()
    {
        ThrowIfWhiteSpace(CriteriaFileName, nameof(CriteriaFileName));
        ThrowIfWhiteSpace(EntriesFileName, nameof(EntriesFileName));
        ThrowIfWhiteSpace(WeightsFileName, nameof(WeightsFileName));
        ThrowIfWhiteSpace(ProgressFileName, nameof(ProgressFileName));
        ThrowIfWhiteSpace(AuditDirectoryName, nameof(AuditDirectoryName));
        ThrowIfWhiteSpace(RawResultsFileNamePattern, nameof(RawResultsFileNamePattern));
        ThrowIfWhiteSpace(StandardizedResultsSuffix, nameof(StandardizedResultsSuffix));
        ThrowIfWhiteSpace(OutputTimestampFormat, nameof(OutputTimestampFormat));
    }

    private static void ThrowIfWhiteSpace(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"配置项 Paths.{name} 不能为空。");
        }
    }
}

/// <summary>
/// 评分规则配置。
/// </summary>
public sealed record ScoringSettings
{
    public int MinimumScore { get; init; } = 0;

    public int MaximumScore { get; init; } = 3;

    public double RequiredMinimum { get; init; } = 0.0;

    public double RequiredMaximum { get; init; } = 100.0;

    public double CategoryTotalPoints { get; init; } = 100.0;

    public int RoundingDigits { get; init; } = 1;

    public double Tolerance { get; init; } = 1e-6;

    public void Validate()
    {
        if (MinimumScore > MaximumScore)
        {
            throw new InvalidDataException("配置项 Scoring.MinimumScore 不能大于 Scoring.MaximumScore。");
        }

        if (MaximumScore <= 0)
        {
            throw new InvalidDataException("配置项 Scoring.MaximumScore 必须大于 0。");
        }

        if (RequiredMinimum > RequiredMaximum)
        {
            throw new InvalidDataException("配置项 Scoring.RequiredMinimum 不能大于 Scoring.RequiredMaximum。");
        }

        if (CategoryTotalPoints <= 0)
        {
            throw new InvalidDataException("配置项 Scoring.CategoryTotalPoints 必须大于 0。");
        }

        if (RoundingDigits is < 0 or > 15)
        {
            throw new InvalidDataException("配置项 Scoring.RoundingDigits 必须在 0 到 15 之间。");
        }

        if (Tolerance < 0)
        {
            throw new InvalidDataException("配置项 Scoring.Tolerance 不能为负。");
        }
    }
}

/// <summary>
/// 标准化规则配置。
/// </summary>
public sealed record StandardizationSettings
{
    public double TargetMean { get; init; } = 90.0;

    public double TargetThreeStandardDeviations { get; init; } = 10.0;

    public double Epsilon { get; init; } = 1e-9;

    public void Validate()
    {
        if (TargetThreeStandardDeviations < 0)
        {
            throw new InvalidDataException("配置项 Standardization.TargetThreeStandardDeviations 不能为负。");
        }

        if (Epsilon < 0)
        {
            throw new InvalidDataException("配置项 Standardization.Epsilon 不能为负。");
        }
    }
}

/// <summary>
/// 控制台显示与文案配置。
/// </summary>
public sealed record DisplaySettings
{
    public int SeparatorWidth { get; init; } = 70;

    public string AppTitle { get; init; } = "参赛作品评审 — 交互式打分系统";

    public string EntryNounSingular { get; init; } = "参赛作品";

    public string EntryNameColumnHeader { get; init; } = "参赛作品名称";

    public string ResultsCommentColumnHeader { get; init; } = "备注汇总";

    public string ResultsTotalColumnHeader { get; init; } = "总分";

    public bool ShowWeightSummary { get; init; } = true;

    public void Validate()
    {
        if (SeparatorWidth <= 0)
        {
            throw new InvalidDataException("配置项 Display.SeparatorWidth 必须大于 0。");
        }

        ThrowIfWhiteSpace(AppTitle, nameof(AppTitle));
        ThrowIfWhiteSpace(EntryNounSingular, nameof(EntryNounSingular));
        ThrowIfWhiteSpace(EntryNameColumnHeader, nameof(EntryNameColumnHeader));
        ThrowIfWhiteSpace(ResultsCommentColumnHeader, nameof(ResultsCommentColumnHeader));
        ThrowIfWhiteSpace(ResultsTotalColumnHeader, nameof(ResultsTotalColumnHeader));
    }

    private static void ThrowIfWhiteSpace(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"配置项 Display.{name} 不能为空。");
        }
    }
}
