using Spectre.Console.Cli;

namespace AegiNext.Cli;

internal sealed class ExportCommand(ExportRunner runner) : AsyncCommand<ExportSettings>
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, ExportSettings settings,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(settings, cancellationToken);
    }
}
