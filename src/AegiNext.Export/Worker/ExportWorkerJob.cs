using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

internal sealed record ExportWorkerJob(JsonElement Project, string ProjectDirectory, string TemporaryDirectory,
    string Extension, VideoCodec Codec, string Preset, int Crf, AudioExportMode AudioMode, int AudioBitrate, string FfmpegPath,
    VideoEncodingMode EncodingMode = VideoEncodingMode.SOFTWARE, int VideoBitrate = 8000000,
    VideoDecodeMode DecodeMode = VideoDecodeMode.Auto,
    [property: JsonPropertyName("rate_mode")] VideoRateControlMode RateControlMode = VideoRateControlMode.AUTOMATIC,
    int ProtocolVersion = 0);
