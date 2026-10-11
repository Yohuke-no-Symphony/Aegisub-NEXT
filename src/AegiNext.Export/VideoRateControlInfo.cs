namespace AegiNext.Export;

/// <summary>原生编码器确认的有效码控设置；CRF 的 VideoBitrate 为零，VBR 和 CBR 的 Crf 为零。</summary>
public sealed record VideoRateControlInfo(VideoRateControlMode Mode, int VideoBitrate, int Crf);
