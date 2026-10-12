using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>与合成器共用排版的工程像素测量快照；Position.Offset 与 Bounds 均为画布单位，没有墨迹时使用逻辑回退边界。</summary>
public sealed record SubtitlePlacementMeasurement(SubtitlePosition Position, SKRect Bounds, bool HasInk);
