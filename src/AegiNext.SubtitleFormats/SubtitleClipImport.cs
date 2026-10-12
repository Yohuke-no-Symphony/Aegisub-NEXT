using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>字幕格式导入的原子片段：文字、图层变换、工程坐标蒙版及属性动画。</summary>
public sealed record SubtitleClipImport(SubtitleLine Line, ClipMask? Mask, ImmutableArray<AnimationTrack> Tracks, MediaTime ContentOffset = default)
{
    public LayerTransform Transform { get; init; } = new();
}
