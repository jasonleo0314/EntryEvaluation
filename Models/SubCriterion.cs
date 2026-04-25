namespace EntryEvaluation.Models;

/// <summary>
/// 子项：0-3 分手填，归属一个大项 + 一个工作流阶段。
/// 权重不在此模型上，由独立的 Weights.csv 提供并允许评委现场调整。
/// </summary>
public sealed record SubCriterion(
    string Id,
    string CategoryId,
    WorkflowPhase Phase,
    string Title,
    string Description)
{
    public const int MinScore = 0;
    public const int MaxScore = 3;
}
