using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>独立于图层关键帧时钟的图片播放参数；偏移允许负值以保留向左裁剪的相位。</summary>
public sealed record ImagePlayback
{
    public MediaTime SourceOffset { get; init; }
    public double Speed { get; init; } = 1;
    public ImagePlaybackMode Mode { get; init; } = ImagePlaybackMode.LOOP;
}
