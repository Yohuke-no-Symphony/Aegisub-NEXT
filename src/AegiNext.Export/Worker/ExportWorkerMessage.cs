using AegiNext.Export.Worker;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

internal sealed record ExportWorkerMessage(string Type, ulong Frames = 0, long PositionNumerator = 0,
    long PositionDenominator = 1, string? Error = null, string? Encoder = null,
    VideoDecodeSessionInfo? Decoder = null, VideoExportColor? OutputColor = null,
    VideoRateControlInfo? RateControl = null, ExportWorkerOperation Operation = ExportWorkerOperation.UNKNOWN,
    SubtitleMuxReceipt? SubtitleMux = null, double? Fraction = null, string? Stage = null,
    VideoExportGeometry? OutputGeometry = null);
