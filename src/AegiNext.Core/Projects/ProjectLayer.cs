using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>归属唯一轨道的平面片段；时间为绝对工程时间，动画相对片段 Start。</summary>
public sealed record ProjectLayer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TrackId { get; init; } = ProjectTrack.DEFAULT_TRACK_ID;
    public string Name { get; init; } = "Clip";
    public LayerKind Kind { get; init; } = LayerKind.SUBTITLE;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; } = new(10);
    public MediaTime AnimationOffset { get; init; }
    public LayerTransform Transform { get; init; } = new();
    public LayerPosition? Position { get; init; }
    public double Opacity { get; init; } = 1;
    public BlendMode Blend { get; init; } = BlendMode.NORMAL;
    public SceneColor Fill { get; init; } = SceneColor.White;
    public SceneColor Stroke { get; init; } = SceneColor.Black;
    public double StrokeWidth { get; init; }
    public double Blur { get; init; }
    public Guid? SubtitleId { get; init; }
    public LayerShape? Shape { get; init; }
    public LayerImage? Image { get; init; }
    public ClipMask? Mask { get; init; }
    public MotionPath? MotionPath { get; init; }
    public ImmutableArray<AnimationTrack> Tracks { get; init; } = [];
}
