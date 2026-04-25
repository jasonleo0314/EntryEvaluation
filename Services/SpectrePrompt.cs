using Spectre.Console;

namespace EntryEvaluation.Services;

/// <summary>
/// 基于 Spectre.Console 的富文本 CLI 交互实现。
/// </summary>
public sealed class SpectrePrompt : IPrompt
{
    public void WriteLine(string text = "") => AnsiConsole.WriteLine(text);
    public void Write(string text) => AnsiConsole.Write(text);
    public string? ReadLine() => Console.ReadLine();

    /// <inheritdoc/>
    public void WriteMarkupLine(string markup) => AnsiConsole.MarkupLine(markup);

    /// <inheritdoc/>
    public void WriteRule(string title = "")
    {
        var rule = string.IsNullOrEmpty(title)
            ? new Rule()
            : new Rule($"[bold grey]{Markup.Escape(title)}[/]");
        rule.RuleStyle("grey dim");
        AnsiConsole.Write(rule);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        AnsiConsole.Clear();
    }
}
