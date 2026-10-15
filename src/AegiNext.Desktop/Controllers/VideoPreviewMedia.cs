using AegiNext.Core.Timing;
using AegiNext.Core.Media;

namespace AegiNext.Desktop.Controllers;

/// <summary>
/// 预览使用的明确视频轨与报告时间范围；未知范围不由帧率推算。
/// </summary>
public sealed record VideoPreviewMedia(int VideoStreamIndex, MediaTime? Start, MediaTime? Duration, int? AudioStreamIndex = null,
    int? VideoWidth = null, int? VideoHeight = null, MediaRatio? FrameRate = null)
{
    public MediaTime? PlaybackOrigin { get; init; }
    public string? AudioCodecName { get; init; }
}
