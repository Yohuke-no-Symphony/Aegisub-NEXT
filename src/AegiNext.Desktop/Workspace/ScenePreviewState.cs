using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Workspace;

/// <summary>唯一视频编辑表面的纯场景投影，共享工程快照，不拥有位图或控制器。</summary>
internal sealed record ScenePreviewState(ProjectDocument Document, ProjectLayer? SelectedLayer, MediaTime Position,
    CanvasEditMode Mode, string AssetDirectory, bool IsEditingPose, bool IsInteractive = false,
    PreviewQuality Quality = PreviewQuality.LOW, ShapeKind CreationShapeKind = ShapeKind.RECTANGLE,
    AnimationTrackTarget SpatialTarget = default);
