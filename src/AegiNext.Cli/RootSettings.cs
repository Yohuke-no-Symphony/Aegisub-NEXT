using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>根命令向导的交互开关。</summary>
public sealed class RootSettings : CommandSettings
{
    [CommandOption("--non-interactive")]
    [Description("Disable interactive prompts. Specify a command and all required options.")]
    public bool NonInteractive { get; set; }
}
