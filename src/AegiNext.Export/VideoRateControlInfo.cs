namespace AegiNext.Export;

/// <summary>原生编码器确认的有效码控设置及证据强度；CQ 同时携带质量和目标码率。</summary>
public sealed record VideoRateControlInfo(VideoRateControlMode Mode, int VideoBitrate, int Quality,
    VideoRateControlVerification Verification = VideoRateControlVerification.ConfiguredAndInitialized,
    int BitratePrecision = 1000);
