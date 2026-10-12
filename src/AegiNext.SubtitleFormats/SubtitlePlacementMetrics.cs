using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>以工程像素表示的排版基准位置、局部轴心与墨迹边界；空白使用渲染器的逻辑边界。</summary>
public sealed record SubtitlePlacementMetrics(ScenePoint BasePosition, ScenePoint Pivot, ScenePoint BoundsOrigin, ScenePoint BoundsSize);
