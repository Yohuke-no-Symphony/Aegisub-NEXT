using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>文件命令共用的输入、输出与交互开关。</summary>
public abstract class CliCommandSettings : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path.")]
    public string? Input { get; set; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Output file or directory; defaults to the command output location.")]
    public string? Output { get; set; }

    [CommandOption("--non-interactive")]
    [Description("Disable interactive prompts; fail if required options are missing.")]
    public bool NonInteractive { get; set; }
}
