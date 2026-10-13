using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>高级标签编辑的有效内容、诊断与文字来源映射。</summary>
public sealed record AssTextEditResult(SubtitleLine Line, ImmutableArray<SubtitleFormatDiagnostic> Diagnostics,
    ImmutableArray<AssSourceMapEntry> SourceMap)
{
    public MediaTime ContentOffset { get; init; }
    public ClipMask? Mask { get; init; }
    public ImmutableArray<AnimationTrack> MaskTracks { get; init; } = [];
    public ImmutableArray<AnimationTrack>? TextAnimationTracks { get; init; }

    internal LayerTransform Transform { get; init; } = new();
    internal ImmutableArray<AnimationTrack> PlacementTracks { get; init; } = [];
    internal ImmutableArray<AnimationTrack> OpacityTracks { get; init; } = [];
    internal ImmutableArray<AnimationTrack> NumericTracks { get; init; } = [];
    internal ImmutableArray<AssKaraokeSourceMapEntry> KaraokeSourceMap { get; init; } = [];
    internal ImmutableArray<AssDrawingRun> Drawings { get; init; } = [];
}
