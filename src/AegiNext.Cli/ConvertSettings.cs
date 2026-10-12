using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>ASS、SRT 与工程字幕转换参数。</summary>
public sealed class ConvertSettings : CliCommandSettings
{
    [CommandOption("--target <FORMAT>")]
    [Description("Target subtitle format: ass or srt.")]
    public string? Target { get; set; }

    [CommandOption("--accept-loss")]
    [Description("Accept conversion diagnostics and write output; diagnostics are still displayed.")]
    public bool AcceptLoss { get; set; }
}
