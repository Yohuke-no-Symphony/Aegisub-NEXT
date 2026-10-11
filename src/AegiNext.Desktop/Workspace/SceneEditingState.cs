using AegiNext.Core.Timing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

/// <summary>工作台共享的场景选择与暂存手势目标，不持有视图或渲染资源。</summary>
internal sealed class SceneEditingState
{
    internal ShapeKind CreationShapeKind { get; set; } = ShapeKind.RECTANGLE;
    internal event EventHandler? Changed;
    private Guid? layerId;
    internal Guid? LayerId
    {
        get => layerId;
        set
        {
            if (!EqualityComparer<Guid?>.Default.Equals(layerId, value))
            {
                layerId = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private Guid? cueId;
    internal Guid? CueId
    {
        get => cueId;
        set
        {
            if (!EqualityComparer<Guid?>.Default.Equals(cueId, value))
            {
                cueId = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private Guid[] selectedLayerIds = [];
    internal Guid[] SelectedLayerIds
    {
        get => selectedLayerIds;
        set
        {
            if (!selectedLayerIds.AsSpan().SequenceEqual(value))
            {
                selectedLayerIds = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private AnimationTrackTarget target;
    internal AnimationTrackTarget Target
    {
        get => target;
        set
        {
            if (!EqualityComparer<AnimationTrackTarget>.Default.Equals(target, value))
            {
                target = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    internal AnimationProperty Property
    {
        get => Target.Property;
        set => Target = Target with { Property = value };
    }
    private Guid? maskNodeId;
    internal Guid? MaskNodeId
    {
        get => maskNodeId;
        set
        {
            if (!EqualityComparer<Guid?>.Default.Equals(maskNodeId, value))
            {
                maskNodeId = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private Guid? maskContourId;
    internal Guid? MaskContourId
    {
        get => maskContourId;
        set
        {
            if (!EqualityComparer<Guid?>.Default.Equals(maskContourId, value))
            {
                maskContourId = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private Guid? transformOperationId;
    internal Guid? TransformOperationId
    {
        get => transformOperationId;
        set
        {
            if (!EqualityComparer<Guid?>.Default.Equals(transformOperationId, value))
            {
                transformOperationId = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private CanvasEditMode mode;
    internal CanvasEditMode Mode
    {
        get => mode;
        set
        {
            if (!EqualityComparer<CanvasEditMode>.Default.Equals(mode, value))
            {
                mode = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private MediaTime? keyframeTime;
    internal MediaTime? KeyframeTime
    {
        get => keyframeTime;
        set
        {
            if (!EqualityComparer<MediaTime?>.Default.Equals(keyframeTime, value))
            {
                keyframeTime = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private AnimationEditTarget? draftTarget;
    internal AnimationEditTarget? DraftTarget
    {
        get => draftTarget;
        set
        {
            if (!EqualityComparer<AnimationEditTarget?>.Default.Equals(draftTarget, value))
            {
                draftTarget = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private AnimationEditTarget? gestureTarget;
    internal AnimationEditTarget? GestureTarget
    {
        get => gestureTarget;
        set
        {
            if (!EqualityComparer<AnimationEditTarget?>.Default.Equals(gestureTarget, value))
            {
                gestureTarget = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
