using AegiNext.Export;
using AegiNext.Media.Decoding;

namespace AegiNext.Cli;

internal sealed record ExportConfiguration(VideoExportSettings Settings, VideoDecodeMode DecodeMode, string Extension);
