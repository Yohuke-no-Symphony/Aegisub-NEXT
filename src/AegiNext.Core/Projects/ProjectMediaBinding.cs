using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>绑定源媒体流；工程 0 对应原始媒体的 MediaOrigin，不改写原始 PTS。</summary>
public sealed record ProjectMediaBinding(Guid AssetId, int? VideoStreamIndex, int? AudioStreamIndex, MediaTime MediaOrigin)
{
    public MediaTime? PlaybackOrigin { get; init; }
}
