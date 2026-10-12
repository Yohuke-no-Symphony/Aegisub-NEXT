using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>ASS、SRT 与工程字幕转换参数。</summary>
public sealed class ConvertSettings : CliCommandSettings
{
    [CommandOption("--target <FORMAT>")]
    [Description("目标字幕格式：ass 或 srt。")]
    public string? Target { get; set; }

    [CommandOption("--accept-loss")]
    [Description("接受转换诊断并写入输出；仍显示诊断内容。")]
    public bool AcceptLoss { get; set; }
}
