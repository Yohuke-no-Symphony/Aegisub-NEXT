using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

internal sealed class ExportSettings : CliCommandSettings
{
    [CommandOption("--encoding <CODEC>")]
    [Description("视频编码：auto/h264/hevc/vp8/vp9/av1，默认 auto。")]
    public string? Encoding { get; set; }

    [CommandOption("--use-gpu")]
    [Description("严格使用硬件编码；不可用时失败，不回退软件。")]
    public bool UseGpu { get; set; }

    [CommandOption("--crf")]
    [Description("软件恒定质量模式，与 cq/vbr/cbr 互斥。")]
    public bool Crf { get; set; }

    [CommandOption("--cq")]
    [Description("VP8 软件质量与码率约束模式。")]
    public bool Cq { get; set; }

    [CommandOption("--vbr")]
    [Description("可变码率模式，未指定视频码率时使用 8 Mbps。")]
    public bool Vbr { get; set; }

    [CommandOption("--cbr")]
    [Description("恒定码率模式，未指定视频码率时使用 8 Mbps。")]
    public bool Cbr { get; set; }

    [CommandOption("--quality <N>")]
    [Description("质量值：H264/HEVC 为 0–51，VP8/VP9 为 0–63，AV1 为 1–63。")]
    public int? Quality { get; set; }

    [CommandOption("--video-bitrate <MBPS>")]
    [Description("视频码率 0.1–200 Mbps，最多三位小数，默认 8。")]
    public string? VideoBitrate { get; set; }

    [CommandOption("--speed <PRESET>")]
    [Description("速度：ultrafast/superfast/veryfast/faster/fast/medium/slow/slower/veryslow。")]
    public string? Speed { get; set; }

    [CommandOption("--audio <MODE>")]
    [Description("音频：copy/aac/opus/none，默认 copy。")]
    public string? Audio { get; set; }

    [CommandOption("--audio-bitrate <KBPS>")]
    [Description("AAC/Opus 音频码率 32–512 kbps，默认 192。")]
    public int? AudioBitrate { get; set; }

    [CommandOption("--decode <MODE>")]
    [Description("视频解码：auto/software/hardware，默认 auto。")]
    public string? Decode { get; set; }

    [CommandOption("--container <FORMAT>")]
    [Description("容器：mp4/mkv/webm，必须兼容编码、音频及输出文件扩展名。")]
    public string? Container { get; set; }

    internal bool IsEmpty => Input is null && Output is null && !NonInteractive && Encoding is null &&
        !UseGpu && !Crf && !Cq && !Vbr && !Cbr && Quality is null && VideoBitrate is null &&
        Speed is null && Audio is null && AudioBitrate is null && Decode is null && Container is null;
}
