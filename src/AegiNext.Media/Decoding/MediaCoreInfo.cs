namespace AegiNext.Media.Decoding;

/// <summary>当前原生解码模块报告的共享媒体核心版本与能力位。</summary>
public sealed record MediaCoreInfo(uint Version, uint Capabilities);
