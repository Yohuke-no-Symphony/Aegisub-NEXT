using AegiNext.Media.Decoding;

namespace AegiNext.Export;

internal sealed record NativeVideoExportResult(ulong Frames, string Encoder,
    VideoDecodeSessionInfo Decoder, VideoExportColor OutputColor, VideoRateControlInfo RateControl);
