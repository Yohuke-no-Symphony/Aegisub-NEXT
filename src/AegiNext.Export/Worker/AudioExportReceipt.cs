namespace AegiNext.Export.Worker;

internal sealed record AudioExportReceipt(int SourceStreamIndex, string Codec, int SampleRate, int Channels, bool OutputVerified);
