using Spectre.Console;

namespace AegiNext.Cli;

/// <summary>可注入的终端能力和逐步输入接口。</summary>
public sealed class CliRuntime(IAnsiConsole console, bool isInteractive)
{
    public IAnsiConsole Console { get; } = console;

    /// <summary>判断当前命令是否允许终端交互。</summary>
    public bool CanPrompt(CliCommandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return isInteractive && Console.Profile.Capabilities.Interactive && !settings.NonInteractive;
    }

    /// <summary>要求可交互终端，否则报告参数不足。</summary>
    public void RequireInteractive(bool nonInteractive = false)
    {
        if (!isInteractive || !Console.Profile.Capabilities.Interactive || nonInteractive)
        {
            throw new CliUsageException("Interactive prompts are disabled in this environment. Specify a command and all required options. Use --help for usage.");
        }
    }

    /// <summary>输入文本，支持可选默认值及空值。</summary>
    public Task<string> PromptTextAsync(string title, CancellationToken cancellationToken,
        string? defaultValue = null, bool allowEmpty = false)
    {
        var prompt = new TextPrompt<string>(Markup.Escape(title));
        if (defaultValue is not null)
        {
            prompt.DefaultValue(defaultValue);
        }
        if (allowEmpty)
        {
            prompt.AllowEmpty();
        }
        return Console.PromptAsync(prompt, cancellationToken);
    }

    /// <summary>从当前有效选项中选择一个值。</summary>
    public Task<string> SelectAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
    {
        var prompt = new SelectionPrompt<string>().Title(Markup.Escape(title)).UseConverter(Markup.Escape).AddChoices(choices);
        return Console.PromptAsync(prompt, cancellationToken);
    }

    /// <summary>显示明确的确认步骤。</summary>
    public Task<bool> ConfirmAsync(string title, CancellationToken cancellationToken, bool defaultValue = false)
    {
        return Console.PromptAsync(new ConfirmationPrompt(Markup.Escape(title)) { DefaultValue = defaultValue }, cancellationToken);
    }
}
