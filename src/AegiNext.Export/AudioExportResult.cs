namespace AegiNext.Export;

/// <summary>已验证并提交的独立音频文件及其源流、编码和音频参数。</summary>
public sealed record AudioExportResult(string OutputPath, int SourceStreamIndex, string Codec, int SampleRate, int Channels);
