using System.Text.Json.Serialization;
using AegiNext.Media.Decoding;

namespace AegiNext.Export.Worker;

internal sealed record ExportWorkerVideoOptions(VideoCodec Codec, string Preset, int Quality,
    AudioExportMode AudioMode, int AudioBitrate, VideoEncodingMode EncodingMode = VideoEncodingMode.SOFTWARE,
    int VideoBitrate = 8000000, VideoDecodeMode DecodeMode = VideoDecodeMode.Auto,
    [property: JsonPropertyName("rate_mode")] VideoRateControlMode RateControlMode = VideoRateControlMode.AUTOMATIC);
