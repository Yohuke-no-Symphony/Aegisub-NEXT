namespace AegiNext.Cli;

/// <summary>根向导中可执行的一个命令。</summary>
public sealed record CliWizardEntry(string Name, string Description, Func<CancellationToken, Task<int>> Run);
