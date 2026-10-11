using AegiNext.Core.Timing;

namespace AegiNext.Export;

/// <summary>已渲染帧数、工程时间与阶段；总时长不可知时 Fraction 为空。</summary>
public sealed record VideoExportProgress(ulong FrameCount, MediaTime Position, double? Fraction, string Stage, string? Encoder = null);
