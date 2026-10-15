using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelViewModel
{
    private TimelineEffectClipId? effectClipContext;
    private ProjectDocument? effectClipContextDocument;
    private TimelineKeyframeIdentity? contextKeyframe;
    private TimelineEffectClipId? selectedEffectClipId;
    private TimelineKeyframeIdentity? selectedTimelineKeyframe;
    private ProjectDocument? effectSelectionDocument;
    private bool suppressParentClipDelete;

    public TimelineEffectClipId? SelectedEffectClipId => selectedEffectClipId;
    internal TimelineKeyframeIdentity? SelectedTimelineKeyframe => selectedTimelineKeyframe;
    public bool CanDeleteTimelineSelection => suppressParentClipDelete || session.CanCopyTimelineClips;
    public bool IsContextEffectClipCollapsed => effectClipContext is { } id && !TimelineViewState.ExpandedAnimationClips.Contains(id);
    public bool HasContextKeyframe => contextKeyframe.HasValue;
    public bool IsContextEffectInstance => effectClipContext?.InstanceId.HasValue == true;
    private bool CanDeleteContextKeyframe => IsEffectClipContextCurrent && contextKeyframe is { } key &&
        Document.Layers.Single(layer => layer.Id == key.LayerId).Tracks.Any(track => track.Target == key.Target &&
            (key.OperationId is { } operationId ? track.Transforms.Any(operation => operation.Id == operationId) :
                track.Keyframes.Any(frame => frame.Time == key.Time)));
    private bool IsEffectClipContextCurrent => effectClipContext is { } id &&
        ReferenceEquals(Document, effectClipContextDocument) && !session.IsClosing && !session.IsProjectBusy &&
        WorkbenchSession.HasTimelineEffectClip(Document, id);

    /// <summary>验证草稿后原子选择父片段和完整属性目标，不改变播放位置。</summary>
    public bool SelectEffectClip(TimelineEffectClipSelectionEventArgs value)
    {
        if (!session.SelectTimelineEffectClip(value.Id))
        {
            return false;
        }

        SetEffectSelection(value.Id, null);
        return true;
    }

    /// <summary>选择准确关键帧或变换端点，并保存时间线的删除目标。</summary>
    public bool SelectKeyframe(TimelineKeyframeEventArgs value)
    {
        if (session.IsClosing || session.IsProjectBusy || !session.SelectKeyframe(value))
        {
            return false;
        }

        SetEffectSelection(new(value.LayerId, value.Target),
            new(value.LayerId, value.Target, value.OldTime, value.OperationId, value.IsOperationStart));
        return true;
    }

    /// <summary>只更新独立视图状态，保留其他面板中的原始草稿。</summary>
    public void SetEffectClipCollapsed(TimelineEffectClipCollapseEventArgs value) =>
        session.SetTimelineEffectClipCollapsed(value.Id, value.IsCollapsed);

    /// <summary>提交关键帧的时间手势并更新其准确删除身份。</summary>
    public async Task MoveKeyframeAsync(TimelineKeyframeEventArgs value)
    {
        var source = Document;
        var wasSelected = selectedTimelineKeyframe is { } selected && selected.LayerId == value.LayerId &&
            selected.Target == value.Target && selected.Time == value.OldTime && selected.OperationId == value.OperationId &&
            selected.IsOperationStart == value.IsOperationStart;
        await session.RunCommandAsync(() => session.EditAsync(() => session.MoveKeyframe(value)));
        if (wasSelected && !ReferenceEquals(source, Document))
        {
            var time = value.OperationId is null ? session.SelectedKeyTime : value.NewTime;
            if (time is { } selectedTime)
            {
                SetEffectSelection(new(value.LayerId, value.Target),
                    new(value.LayerId, value.Target, selectedTime, value.OperationId, value.IsOperationStart));
            }
        }
    }

    /// <summary>冻结块菜单的完整身份和工程快照，不改变当前选择。</summary>
    public void SetEffectClipContext(TimelineEffectClipContextEventArgs value)
    {
        effectClipContext = value.Id;
        effectClipContextDocument = Document;
        contextKeyframe = value.KeyframeTime is { } time
            ? new(value.Id.LayerId, value.Id.Target, time, value.OperationId, value.IsOperationStart) : null;
        RefreshClipCommands();
    }

    /// <summary>按时间线最近的显式选择删除关键帧、特效块或父片段。</summary>
    public Task DeleteTimelineSelectionAsync()
    {
        if (!suppressParentClipDelete)
        {
            return DeleteSelectedClipsAsync();
        }

        if (selectedEffectClipId is not { } id || !ReferenceEquals(effectSelectionDocument, Document) ||
            session.SelectedLayerId != id.LayerId || (id.InstanceId is { } instanceId
                ? session.ViewModel.Effects.SelectedEffectInstance?.Id != instanceId : session.SceneEditing.Target != id.Target))
        {
            return Task.CompletedTask;
        }

        if (selectedTimelineKeyframe is { } key)
        {
            var isCurrent = key.OperationId is { } operationId
                ? session.SceneEditing.TransformOperationId == operationId
                : session.SelectedKeyTime == key.Time;
            return isCurrent ? session.DeleteTimelineKeyframeAsync(key, Document) : Task.CompletedTask;
        }
        return session.ClearTimelineEffectClipAsync(id, Document);
    }

    /// <summary>以冻结身份和工程版本提交一个特效块时间手势。</summary>
    public Task CommitEffectInstanceTimingAsync(TimelineEffectInstanceTimingEventArgs value) => session.CommitEffectInstanceTimingAsync(value);

    private void RebindEffectSelection(ProjectDocument value)
    {
        if (selectedEffectClipId is { InstanceId: not null } id && WorkbenchSession.HasTimelineEffectClip(value, id))
        {
            effectSelectionDocument = value;
        }
        else
        {
            InvalidateEffectSelection();
        }
    }

    internal void ResetEffectSelection()
    {
        suppressParentClipDelete = false;
        InvalidateEffectSelection();
        effectClipContext = null;
        effectClipContextDocument = null;
        contextKeyframe = null;
    }

    internal void ClearInstanceSelection()
    {
        if (selectedEffectClipId?.InstanceId.HasValue == true)
        {
            InvalidateEffectSelection();
        }
    }

    private void InvalidateEffectSelection()
    {
        selectedTimelineKeyframe = null;
        effectSelectionDocument = null;
        SetProperty(ref selectedEffectClipId, null, nameof(SelectedEffectClipId));
    }

    private void SetEffectSelection(TimelineEffectClipId id, TimelineKeyframeIdentity? key)
    {
        suppressParentClipDelete = true;
        selectedTimelineKeyframe = key;
        effectSelectionDocument = Document;
        SetProperty(ref selectedEffectClipId, id, nameof(SelectedEffectClipId));
    }

    private Task ClearContextEffectClipAsync() => IsEffectClipContextCurrent && effectClipContext is { } id
        ? session.ClearTimelineEffectClipAsync(id, effectClipContextDocument!) : Task.CompletedTask;

    private Task DeleteContextKeyframeAsync() => CanDeleteContextKeyframe && contextKeyframe is { } key
        ? session.DeleteTimelineKeyframeAsync(key, effectClipContextDocument!) : Task.CompletedTask;

    private void ToggleContextEffectClipCollapse()
    {
        if (IsEffectClipContextCurrent && effectClipContext is { InstanceId: null } id)
        {
            session.SetTimelineEffectClipCollapsed(id, !IsContextEffectClipCollapsed);
        }
    }
}
