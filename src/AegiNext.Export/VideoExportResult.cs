using AegiNext.Media.Decoding;

namespace AegiNext.Export;

/// <summary>已原子提交的成片路径与帧数。</summary>
public sealed record VideoExportResult(string OutputPath, ulong Frames, string? Encoder = null,
    VideoDecodeSessionInfo? Decoder = null, VideoExportColor? OutputColor = null,
    VideoRateControlInfo? RateControl = null);
