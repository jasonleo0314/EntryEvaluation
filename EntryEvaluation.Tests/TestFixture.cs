using EntryEvaluation.Models;
using EntryEvaluation.Services;

namespace EntryEvaluation.Tests;

/// <summary>
/// 测试 fixture：通过内联 CSV 文本生成评分目录与工具清单，
/// 与生产代码完全走同一条加载路径，避免在测试代码中重复硬编码。
/// </summary>
internal static class TestFixture
{
    public const string SampleCriteriaCsv =
        "CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription\n" +
        "C1,运行条件,10,S1.1,ReadDocs,适配性,系统/硬件要求\n" +
        "C1,运行条件,10,S1.2,ReadDocs,安装难易度,单机/网络版安装\n" +
        "C1,运行条件,10,S1.3,ReadDocs,操作说明,引导是否清晰\n" +
        "C2,操作性能,30,S2.1,EnterUI,页面友好,是否易上手\n" +
        "C2,操作性能,30,S2.2,ImportData,数据导入,拖拽式导入\n" +
        "C2,操作性能,30,S2.3,ImportData,数据清洗,一键化清洗\n" +
        "C2,操作性能,30,S2.4,RunModel,分析模型,参数易设置\n" +
        "C2,操作性能,30,S2.5,GetResults,结果生成,易读图表\n" +
        "C2,操作性能,30,S2.6,RunModel,运算多样性,逻辑多样\n" +
        "C3,可验证性,40,S3.1,UnderstandResults,数据比较,清洗vs原始\n" +
        "C3,可验证性,40,S3.2,UnderstandResults,结果比较,Excel/办案对照\n" +
        "C3,可验证性,40,S3.3,UnderstandResults,模型间比较,跨模型对照\n" +
        "C4,运算性能,20,S4.1,ReadDocs,技术创新,语言选型\n" +
        "C4,运算性能,20,S4.2,RunModel,处理能力,容量/速度\n" +
        "C4,运算性能,20,S4.3,GetResults,数据协同,入图出图\n";

    public const string SampleWeightsCsv =
        "SubId,Weight\n" +
        "S1.1,0.30\n" +
        "S1.2,0.35\n" +
        "S1.3,0.35\n" +
        "S2.1,0.20\n" +
        "S2.2,0.15\n" +
        "S2.3,0.20\n" +
        "S2.4,0.15\n" +
        "S2.5,0.15\n" +
        "S2.6,0.15\n" +
        "S3.1,0.30\n" +
        "S3.2,0.40\n" +
        "S3.3,0.30\n" +
        "S4.1,0.30\n" +
        "S4.2,0.40\n" +
        "S4.3,0.30\n";

    public const string SampleEntriesCsv =
        "EntryName\n" +
        "“经侦万象”AI大模型\n" +
        "聚网搜工具\n" +
        "“金鉴”智能资金分析鉴定工具\n";

    public static CriteriaSet LoadSample() =>
        CriteriaCsvLoader.LoadFromText(SampleCriteriaCsv);

    public static IReadOnlyDictionary<string, double> LoadSampleWeights() =>
        WeightsCsvLoader.LoadFromText(SampleWeightsCsv);

    public static WeightSnapshot LoadSampleWeightSnapshot()
    {
        var set = LoadSample();
        var raw = LoadSampleWeights();
        return new WeightSnapshot(raw, WeightsCollector.Normalize(set.Categories, set.SubCriteria, raw));
    }

    public static IReadOnlyList<Entry> LoadSampleEntries() =>
        EntriesCsvLoader.LoadFromText(SampleEntriesCsv);

    public static Dictionary<string, int> AllScores(CriteriaSet set, int v) =>
        set.SubCriteria.ToDictionary(s => s.Id, _ => v);
}
