using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private readonly HashSet<TimelineEffectClipId> expandedEffectClips = [];
    private TimelineEffectClipId? selectedEffectClipId;

    public static readonly StyledProperty<TimelineViewState> TimelineViewStateProperty =
        AvaloniaProperty.Register<SubtitleTimelineControl, TimelineViewState>(nameof(TimelineViewState), new());

    public TimelineViewState TimelineViewState
    {
        get => GetValue(TimelineViewStateProperty);
        set
        {
            value.Validate();
            SetValue(TimelineViewStateProperty, value);
        }
    }

    public TimelineEffectClipId? SelectedEffectClipId
    {
        get => selectedEffectClipId;
        set
        {
            if (selectedEffectClipId == value)
            {
                return;
            }
            selectedEffectClipId = value;
            InvalidateSceneDrawing();
            InvalidateVisual();
        }
    }

    public event EventHandler<TimelineEffectClipCollapseEventArgs>? EffectClipCollapseRequested;
    public event EventHandler<TimelineEffectClipContextEventArgs>? EffectClipContextRequested;
    public event EventHandler<TimelineEffectClipSelectionEventArgs>? EffectClipSelected;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSpectrumVisibleProperty || change.Property == IsWaveformVisibleProperty)
        {
            audioDrawing.Dispose();
        }
        if (change.Property != TimelineViewStateProperty)
        {
            return;
        }
        var state = TimelineViewState;
        state.Validate();
        if (expandedEffectClips.SetEquals(state.ExpandedAnimationClips) && collapsedTrackIds.SetEquals(state.CollapsedTrackIds))
        {
            return;
        }
        CancelDrag();
        expandedEffectClips.Clear();
        expandedEffectClips.UnionWith(state.ExpandedAnimationClips);
        collapsedTrackIds.Clear();
        collapsedTrackIds.UnionWith(state.CollapsedTrackIds);
        RebuildRows();
        PublishViewport(viewport, false);
        InvalidateVisual();
    }

    /// <summary>取得特效块的完整几何；所属轨道折叠或目标不存在时返回 null。</summary>
    public Rect? GetEffectClipRectangle(TimelineEffectClipId id)
    {
        var block = effectClipsByTarget.GetValueOrDefault((id.LayerId, id.Target));
        var row = rowsByLayer.GetValueOrDefault(id.LayerId);
        var layer = layersById.GetValueOrDefault(id.LayerId);
        if (block is null || row is null || layer is null)
        {
            return null;
        }
        var parent = ClipRectangle(layer, row);
        return new(parent.X, RowY(row) + block.Top, parent.Width, block.Height);
    }

    /// <summary>取得可见标题中的折叠按钮；过窄或不可见时可通过上下文菜单折叠。</summary>
    public Rect? GetEffectClipExpanderRectangle(TimelineEffectClipId id)
    {
        if (GetEffectClipRectangle(id) is not { } block)
        {
            return null;
        }
        var visible = block.Intersect(BodyRectangle());
        return visible.Width >= 24 && block.Top >= RulerHeight && block.Top + 20 <= Bounds.Height
            ? new Rect(visible.Left + 2, block.Top + 1, 20, 18) : null;
    }

    /// <summary>取得独立特效块折叠状态；未展开的块默认收起。</summary>
    public bool IsEffectClipCollapsed(TimelineEffectClipId id) => !expandedEffectClips.Contains(id);

    internal bool TryRequestEffectClipCollapse(Point point)
    {
        if (FindEffectClip(point) is not { } block || GetEffectClipExpanderRectangle(block.Id) is not { } button ||
            !button.Contains(point))
        {
            return false;
        }
        CancelDrag();
        EffectClipCollapseRequested?.Invoke(this, new(block.Id, block.IsCollapsed == false));
        return true;
    }

    private bool IsEffectClipExpanderPoint(Point point) => FindEffectClip(point) is { } block &&
        GetEffectClipExpanderRectangle(block.Id) is { } button && button.Contains(point);

    private TimelineEffectClip? FindEffectClip(Point point)
    {
        if (!BodyRectangle().Contains(point) || RowAt(point.Y) is not { } row)
        {
            return null;
        }
        if (PreviewLayerId is { } previewId && rowsByLayer.GetValueOrDefault(previewId) == row)
        {
            foreach (var block in row.Effects.ForLayer(previewId))
            {
                if (GetEffectClipRectangle(block.Id) is { } rectangle && rectangle.Contains(point))
                {
                    return block;
                }
            }
        }
        foreach (var clip in VisibleClipsForRow(row).Reverse())
        {
            if (clip.Id == PreviewLayerId)
            {
                continue;
            }
            foreach (var block in row.Effects.ForLayer(clip.Id))
            {
                if (GetEffectClipRectangle(block.Id) is { } rectangle && rectangle.Contains(point))
                {
                    return block;
                }
            }
        }
        return null;
    }

    private bool TryRequestEffectClipContext(Point point)
    {
        var marker = FindKeyframe(point);
        var id = marker is not null
            ? new TimelineEffectClipId(marker.Identity.LayerId, marker.Identity.Target) : FindEffectClip(point)?.Id;
        if (id is null)
        {
            return false;
        }
        EffectClipContextRequested?.Invoke(this, new(id, marker?.Identity.Time, marker?.Identity.OperationId,
            marker?.Identity.IsOperationStart ?? false));
        return true;
    }

    private bool TrySelectEffectClip(Point point)
    {
        if (FindEffectClip(point) is not { } block)
        {
            return false;
        }
        CancelDrag();
        var request = new TimelineEffectClipSelectionEventArgs(block.Id);
        EffectClipSelected?.Invoke(this, request);
        if (request.SelectionAccepted)
        {
            SelectedEffectClipId = block.Id;
        }
        return true;
    }

    private Point AnimationCurvePoint(ProjectLayer layer, AnimationTrack track, Rect area,
        (double Minimum, double Maximum) range, int component, double x)
    {
        var time = new MediaTime((long)Math.Round((ViewStart + (x - HeaderWidth) / PixelsPerSecond) * 1000000), 1000000) -
            layer.Start + layer.AnimationOffset;
        return new(x, ValueY(SceneEvaluator.EvaluateTrack(track, time).GetComponent(component),
            range.Minimum, range.Maximum, area));
    }

}
