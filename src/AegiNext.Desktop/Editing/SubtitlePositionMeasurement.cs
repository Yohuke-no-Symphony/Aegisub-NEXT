using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

/// <summary>模板编辑接收的纯文字测量结果；资源失败以可显示的诊断文本返回。</summary>
public sealed record SubtitlePositionMeasurement(SubtitlePosition? Position,
    LayerPositionDraftGeometry? Geometry, string? Error = null);
