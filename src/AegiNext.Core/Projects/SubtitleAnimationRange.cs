using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>以完整字素构成的有序动画范围；允许重叠，数组顺序决定样式覆盖与变换叠加。</summary>
public sealed record SubtitleAnimationRange(Guid Id, int Utf16Start, int Utf16Length)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScenePoint Offset { get; init; }
    public ScenePoint Scale { get; init; } = new(1, 1);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Rotation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double RotationX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double RotationY { get; init; }
    public double PerspectiveDistance { get; init; } = SceneProjection.DEFAULT_DISTANCE;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SceneVector3 ProjectionTranslation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SubtitleAnimationPivot Pivot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleAnimationRangeOrigin? GeneratedOrigin { get; init; }
}
