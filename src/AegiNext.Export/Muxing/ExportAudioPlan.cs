namespace AegiNext.Export;

internal sealed record ExportAudioPlan(string FfmpegPath, string SourcePath, int? StreamIndex, string? CodecName);
