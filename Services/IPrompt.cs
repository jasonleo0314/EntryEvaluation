using System.Text.RegularExpressions;

namespace EntryEvaluation.Services;

/// <summary>
/// I/O 抽象，便于测试注入预设输入。
/// </summary>
public interface IPrompt
{
    void WriteLine(string text = "");
    void Write(string text);
    string? ReadLine();

    /// <summary>
    /// 输出带 Spectre.Console markup 的一行文本。
    /// 默认实现剥离标签后以纯文本输出。
    /// </summary>
    void WriteMarkupLine(string markup) =>
        WriteLine(SpectreMarkupHelper.Strip(markup));

    /// <summary>
    /// 输出水平分隔线（可带标题）。
    /// 默认实现输出纯文本横线。
    /// </summary>
    void WriteRule(string title = "")
    {
        if (string.IsNullOrEmpty(title))
            WriteLine(new string('─', 60));
        else
            WriteLine($"── {title} ──");
    }

    /// <summary>
    /// 清屏。富终端应清空控制台以重绘整屏；非交互/重定向输出可忽略。
    /// 默认实现为 no-op，方便测试桩与受限终端使用。
    /// </summary>
    void Clear() { }
}

/// <summary>
/// 剥离 Spectre.Console markup 标签，还原纯文本。
/// </summary>
internal static partial class SpectreMarkupHelper
{
    [GeneratedRegex(@"\[/?[^\[\]]*\]")]
    private static partial Regex TagPattern();

    public static string Strip(string markup) =>
        TagPattern().Replace(markup, string.Empty);
}

public sealed class ConsolePrompt : IPrompt
{
    public void WriteLine(string text = "") => Console.WriteLine(text);
    public void Write(string text) => Console.Write(text);
    public string? ReadLine() => Console.ReadLine();
}
