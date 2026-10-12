using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>根命令向导的交互开关。</summary>
public sealed class RootSettings : CommandSettings
{
    [CommandOption("--non-interactive")]
    [Description("禁止交互询问。请显式指定子命令和必需参数。")]
    public bool NonInteractive { get; set; }
}
