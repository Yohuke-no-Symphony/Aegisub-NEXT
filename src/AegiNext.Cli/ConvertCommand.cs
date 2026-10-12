using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>字幕转换子命令。</summary>
public sealed class ConvertCommand(ConvertRunner runner) : AsyncCommand<ConvertSettings>
{
    /// <summary>通过共用转换流程执行命令。</summary>
    public override Task<int> ExecuteAsync(CommandContext context, ConvertSettings settings, CancellationToken cancellationToken)
    {
        return runner.RunAsync(settings, cancellationToken);
    }
}
