namespace EntryEvaluation.Models;

/// <summary>
/// 真实操作顺序阶段：阅读使用文档 → 进入界面 → 导入数据 → 运行模型 → 获取结果 → 理解结果。
/// </summary>
public enum WorkflowPhase
{
    ReadDocs = 1,
    EnterUI = 2,
    ImportData = 3,
    RunModel = 4,
    GetResults = 5,
    UnderstandResults = 6
}

internal static class WorkflowPhaseExtensions
{
    public static string ToChinese(this WorkflowPhase phase) => phase switch
    {
        WorkflowPhase.ReadDocs => "阅读使用文档",
        WorkflowPhase.EnterUI => "进入界面",
        WorkflowPhase.ImportData => "导入数据",
        WorkflowPhase.RunModel => "运行模型",
        WorkflowPhase.GetResults => "获取结果",
        WorkflowPhase.UnderstandResults => "理解结果",
        _ => phase.ToString()
    };
}
