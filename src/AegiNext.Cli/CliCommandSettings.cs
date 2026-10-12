using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>文件命令共用的输入、输出与交互开关。</summary>
public abstract class CliCommandSettings : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("输入文件路径。")]
    public string? Input { get; set; }

    [CommandOption("-o|--output <PATH>")]
    [Description("输出文件或目录；省略时使用命令的默认位置。")]
    public string? Output { get; set; }

    [CommandOption("--non-interactive")]
    [Description("禁止交互询问；缺少必需参数时直接报错。")]
    public bool NonInteractive { get; set; }
}
