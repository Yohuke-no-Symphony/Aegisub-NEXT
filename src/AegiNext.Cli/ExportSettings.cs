using System.ComponentModel;
using Spectre.Console.Cli;

namespace AegiNext.Cli;

internal sealed class ExportSettings : CliCommandSettings
{
    [CommandOption("--encoding <CODEC>")]
    [Description("Video codec: auto/h264/hevc/vp8/vp9/av1. Default: auto.")]
    public string? Encoding { get; set; }

    [CommandOption("--use-gpu")]
    [Description("Require hardware encoding; fail if unavailable without falling back to software.")]
    public bool UseGpu { get; set; }

    [CommandOption("--crf")]
    [Description("Software constant-quality mode; mutually exclusive with cq/vbr/cbr.")]
    public bool Crf { get; set; }

    [CommandOption("--cq")]
    [Description("VP8 software quality mode with a bitrate constraint.")]
    public bool Cq { get; set; }

    [CommandOption("--vbr")]
    [Description("Variable bitrate mode. Default video bitrate: 8 Mbps.")]
    public bool Vbr { get; set; }

    [CommandOption("--cbr")]
    [Description("Constant bitrate mode. Default video bitrate: 8 Mbps.")]
    public bool Cbr { get; set; }

    [CommandOption("--quality <N>")]
    [Description("Quality: 0–51 for H264/HEVC, 0–63 for VP8/VP9, and 1–63 for AV1.")]
    public int? Quality { get; set; }

    [CommandOption("--video-bitrate <MBPS>")]
    [Description("Video bitrate: 0.1–200 Mbps, at most three decimal places. Default: 8.")]
    public string? VideoBitrate { get; set; }

    [CommandOption("--speed <PRESET>")]
    [Description("Speed: ultrafast/superfast/veryfast/faster/fast/medium/slow/slower/veryslow.")]
    public string? Speed { get; set; }

    [CommandOption("--audio <MODE>")]
    [Description("Audio: copy/aac/opus/none. Default: copy.")]
    public string? Audio { get; set; }

    [CommandOption("--audio-bitrate <KBPS>")]
    [Description("AAC/Opus audio bitrate: 32–512 kbps. Default: 192.")]
    public int? AudioBitrate { get; set; }

    [CommandOption("--decode <MODE>")]
    [Description("Video decode mode: auto/software/hardware. Default: auto.")]
    public string? Decode { get; set; }

    [CommandOption("--container <FORMAT>")]
    [Description("Container: mp4/mkv/webm; must match the codec, audio mode, and output file extension.")]
    public string? Container { get; set; }

    internal bool IsEmpty => Input is null && Output is null && !NonInteractive && Encoding is null &&
        !UseGpu && !Crf && !Cq && !Vbr && !Cbr && Quality is null && VideoBitrate is null &&
        Speed is null && Audio is null && AudioBitrate is null && Decode is null && Container is null;
}
