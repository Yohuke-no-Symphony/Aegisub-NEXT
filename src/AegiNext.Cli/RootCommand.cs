using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>无参数时引导用户选择并填写一个命令。</summary>
public sealed class RootCommand(CliRuntime runtime, CliWizardCatalog catalog) : AsyncCommand<RootSettings>
{
    /// <summary>运行所选子命令的完整向导。</summary>
    public override async Task<int> ExecuteAsync(CommandContext context, RootSettings settings, CancellationToken cancellationToken)
    {
        runtime.RequireInteractive(settings.NonInteractive);
        var labels = catalog.Entries.Select(entry => $"{entry.Name} — {entry.Description}").ToArray();
        var selected = await runtime.SelectAsync("Select an operation:", labels, cancellationToken).ConfigureAwait(false);
        var entry = catalog.Entries[Array.IndexOf(labels, selected)];
        return await entry.Run(cancellationToken).ConfigureAwait(false);
    }
}
