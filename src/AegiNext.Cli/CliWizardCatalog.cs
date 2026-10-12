namespace AegiNext.Cli;

/// <summary>统一登记命令向导，避免根命令递归调用参数解析器。</summary>
public sealed class CliWizardCatalog
{
    private readonly List<CliWizardEntry> entries = [];

    public IReadOnlyList<CliWizardEntry> Entries => entries;

    /// <summary>添加一个根向导选项。</summary>
    public void Add(CliWizardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entries.Add(entry);
    }
}
