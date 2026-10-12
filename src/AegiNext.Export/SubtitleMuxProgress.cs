using AegiNext.Core.Timing;

namespace AegiNext.Export;

/// <summary>封装已处理的媒体时间与阶段；总时长未知时 Fraction 为空。</summary>
public sealed record SubtitleMuxProgress(MediaTime Position, double? Fraction, string Stage);
