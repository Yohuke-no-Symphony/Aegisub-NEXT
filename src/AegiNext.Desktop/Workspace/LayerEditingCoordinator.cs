using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class LayerEditingCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private Guid? valueLayerId;
    private AnimationTrackTarget? valueProperty;

    internal AnimationProperty ActiveProperty => ActiveTarget.Property;
    internal AnimationTrackTarget ActiveTarget => session.SceneEditing.Target;
    internal Keyframe? SelectedKeyframe => session.SelectedKeyTime is { } time
        ? session.SelectedLayer is { } layer ? session.InspectorTrack(layer, ActiveTarget)?.Keyframes.FirstOrDefault(frame => frame.Time == time) : null
        : null;

    internal void UpdateStyle(Func<SubtitleStyle, SubtitleStyle> change)
    {
        if (session.SelectedLayer?.SubtitleId is { } id)
        {
            session.Editor.UpdateSubtitle(id, cue => cue with { Style = change(cue.Style) });
        }
    }

    internal void UpdateColorStyle(Func<SubtitleStyle, SubtitleStyle> style, Func<ProjectLayer, ProjectLayer> layer)
    {
        if (session.SelectedLayer?.Kind == LayerKind.SUBTITLE)
        {
            UpdateStyle(style);
        }
        else
        {
            UpdateLayer(layer);
        }
    }

    internal void UpdateLayer(Func<ProjectLayer, ProjectLayer> change)
    {
        if (session.SelectedLayer is { } layer)
        {
            session.Editor.UpdateLayer(layer.Id, change);
        }
    }

    internal void SetLayerTiming(string text, bool start)
    {
        if (session.SelectedLayer is not { } layer)
        {
            return;
        }

        if (text == TimelineTimeText.Format(start ? layer.Start : layer.End))
        {
            return;
        }

        var time = TimelineTimeText.Parse(text);

        if (layer.SubtitleId is { } id)
        {
            session.Editor.SetSubtitleTiming(id, start ? time : layer.Start, start ? layer.End : time,
                AegiNext.Core.Editing.TimelineEditMode.CROP);
        }
        else
        {
            session.Editor.SetLayerTiming(layer.Id, start ? time : layer.Start, start ? layer.End : time, TimelineEditMode.CROP);
        }
    }

    internal async Task ImportFontAsync()
    {
        if (session.SelectedLayer?.SubtitleId is not { } id || session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        var path = await dialogs.OpenFileAsync("ImportFont", "Fonts", ["*.ttf", "*.otf", "*.ttc"]);
        if (path is null)
        {
            return;
        }

        var captured = session.Editor.Snapshot;
        await session.ApplicationContext.Tasks.Submit(new ImportSubtitleFontTask(session, path, id,
            captured, session.TaskInputRevision, session.ProjectDirectory)).Completion;
    }

    internal void BeginPathEdit()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        if (layer.MotionPath is null)
        {
            var path = new PathGeometry(new(0, 0), [new(new(120, -120), new(240, 120), new(360, 0))]);
            session.Editor.UpdateLayer(layer.Id, value => value with { MotionPath = new(path, layer.End - layer.Start) });
        }
        session.ViewModel.Effects.EditMode = CanvasEditMode.PATH;
    }

    internal void AddPathPoint()
    {
        UpdateLayer(layer =>
        {
            var path = layer.MotionPath?.Path ?? new PathGeometry(new(0, 0), [new(new(40, 0), new(80, 0), new(120, 0))]);
            var end = path.Segments[^1].End;
            var edited = PathOperations.AppendPoint(path, new(end.X + 120, end.Y));
            return layer with { MotionPath = new(edited, layer.MotionPath?.Duration ?? layer.End - layer.Start, layer.MotionPath?.OrientToPath ?? false) };
        });
        session.ViewModel.Effects.EditMode = CanvasEditMode.PATH;
    }

    internal void RemovePathPoint()
    {
        if (session.SelectedLayer?.MotionPath is not { } path || path.Path.Segments.Length <= 1)
        {
            return;
        }
        UpdateLayer(layer => layer with { MotionPath = path with { Path = PathOperations.RemovePoint(path.Path, path.Path.Segments.Length) } });
    }

    internal void ResetPositionEffects()
    {
        if (session.SelectedLayer?.SubtitleId is { } id)
        {
            session.ViewModel.CancelGestures();
            ClearKeyframeSelection();
            session.Editor.ResetSubtitlePositionEffects(id);
            session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        }
    }

    internal void ResetAutomaticPosition()
    {
        if (session.SelectedCue is { } cue)
        {
            session.ViewModel.CancelGestures();
            ClearKeyframeSelection();
            session.Editor.ResetSubtitlePosition(cue.Id);
            session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        }
    }

    internal void CreateKaraoke(Guid? presetId)
    {
        var cue = session.SelectedCue ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var preset = presetId is { } id
            ? session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == id)
            : null;
        var highlightStyle = preset is not null
            ? KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, SubtitleStyleScaling.ConvertUnits(preset.Style,
                cue.Style.Scaling, session.DocumentSnapshot.Width, session.DocumentSnapshot.Height))
            : presetId is { } existingId && cue.KaraokeStyle?.PresetId == existingId
                ? cue.KaraokeStyle
                : null;
        if (presetId is null)
        {
            session.Editor.SetSubtitleKaraokeEnabled(cue.Id, true);
            return;
        }
        session.Editor.Apply("Apply subtitle highlight preset", document =>
        {
            var enabled = ProjectEditingOperations.SetSubtitleKaraokeEnabled(document, cue.Id, true);
            if (enabled.Subtitles.Single(line => line.Id == cue.Id).KaraokeStyle == highlightStyle)
            {
                return enabled;
            }
            return enabled with
            {
                Subtitles = enabled.Subtitles.Select(line => line.Id == cue.Id ? line with { KaraokeStyle = highlightStyle } : line).ToImmutableArray()
            };
        });
    }

    internal void ClearKeyframeSelection()
    {
        session.SelectedKeyTime = null;
        session.ViewModel.Effects.CanDeleteKeyframe = false;
        session.CancelCanvasGesture();
    }

    internal bool SelectKeyframe(TimelineKeyframeEventArgs e)
    {
        if (!session.TryCommitDrafts())
        {
            return false;
        }
        var layer = session.Editor.Snapshot.Layers.FirstOrDefault(value => value.Id == e.LayerId);
        var target = RotationTargetCompatibility.Canonicalize(e.Target);
        var selectedTrack = layer is not null ? session.InspectorTrack(layer, target) : null;
        if (layer is null || (e.OperationId is { } operationId ? selectedTrack?.Transforms.Any(operation => operation.Id == operationId) != true :
                selectedTrack?.Keyframes.FirstOrDefault(frame => frame.Time == e.OldTime) is null))
        {
            return false;
        }

        session.PrepareTimelineEffectSelection(layer);
        using var updateLease = session.BeginWorkbenchUpdate();
        try
        {
            session.SelectedLayerId = layer.Id;
            session.SelectedCueId = layer.SubtitleId;
            session.ViewModel.Effects.SelectedIds = [layer.Id];
            session.ViewModel.Effects.Document = session.Editor.Snapshot;
            session.ViewModel.Effects.SelectedLayer = layer;
            session.SelectedKeyTime = e.OperationId is null ? e.OldTime : null;
            session.SceneEditing.TransformOperationId = e.OperationId;
            session.ViewModel.Effects.Target = target;
            session.SceneEditing.MaskNodeId = e.Target.NodeId ?? session.SceneEditing.MaskNodeId;
            session.ViewModel.Timeline.EffectTarget = target;
            session.RefreshDocument();
            RefreshKeyframeInspector();
            _ = session.RunCommandAsync(async () =>
            {
                await session.PauseForSceneEditAsync();
                if (!session.IsClosing && session.SelectedLayerId == e.LayerId && (e.OperationId is not null || session.SelectedKeyTime == e.OldTime))
                {
                    await session.SeekForEditingAsync(layer.Start + e.OldTime - layer.AnimationOffset);
                }
            });
        }
        finally
        {
            updateLease.Dispose();
        }

        return true;
    }

    internal void RefreshKeyframeInspector()
    {
        using var updateLease = session.BeginWorkbenchUpdate();
        try
        {
            var frame = SelectedKeyframe;

            var minimum = (decimal)AnimationPropertyMetadata.GetMinimum(ActiveProperty, 0);
            var maximum = (decimal)AnimationPropertyMetadata.GetMaximum(ActiveProperty, 0);
            var input = session.ViewModel.Effects;
            input.KeyframeMaximum = maximum;
            input.KeyframeMinimum = minimum;
            var fallback = session.SelectedLayer is { } layer ? BaseValue(layer, ActiveTarget) : AnimationValue.FromScalar(0);
            var track = session.SelectedLayer is { } selected ? session.InspectorTrack(selected, ActiveTarget) : null;
            var current = frame?.Value ?? (track is not null && session.AnimationTarget is { } target
                ? SceneEvaluator.EvaluateTrack(track, target.LocalTime) : fallback);
            if (frame is not null || track is not null || valueLayerId != session.SelectedLayerId || valueProperty != ActiveTarget)
            {
                if (current.IsColor)
                {
                    input.KeyframeColorDraft.Load(current.Color, !session.HasEffectDrafts);
                }
                else
                {
                    input.LoadKeyframeNumbers(current);
                }
            }
            session.ViewModel.Effects.IsOrderedTransform = track is not null && !track.Transforms.IsEmpty;
            session.ViewModel.Effects.RefreshTransformOperations(track);
            valueLayerId = session.SelectedLayerId;
            valueProperty = ActiveTarget;
            if (frame is not null)
            {
                input.AnimationExpanded = true;
                input.LoadKeyframeCurve(frame);
            }

            RefreshKeyframeAvailability();
            session.ViewModel.Effects.CanDeleteKeyframe = frame is not null;
        }
        finally
        {
            updateLease.Dispose();
        }
    }

    private AnimationValue BaseValue(ProjectLayer layer, AnimationTrackTarget target)
    {
        var subtitle = layer.SubtitleId is { } id
            ? session.DocumentSnapshot.Subtitles.Single(value => value.Id == id) : null;
        return SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
    }

    internal void RefreshKeyframeCurveInspector()
    {
        if (SelectedKeyframe is not { } frame)
        {
            return;
        }
        using var update = session.BeginWorkbenchUpdate();
        session.ViewModel.Effects.LoadKeyframeCurve(frame);
    }

    internal void UpdateSelectedKeyframe(Func<Keyframe, Keyframe> change)
    {
        if (session.SelectedLayer is { } layer && SelectedKeyframe is { } frame)
        {
            session.Editor.Apply("Update keyframe", document => WorkspaceDraftOperations.SetKeyframe(
                AnimationEditOperations.NormalizeRotation(document, layer.Id, ActiveTarget), layer.Id, ActiveTarget, change(frame)));
        }
    }

    internal void AddKeyframe()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var time = session.ProjectPosition - layer.Start + layer.AnimationOffset;
        if (LayerAnimationTiming.ClampTime(layer, time) != time)
        {
            return;
        }

        var value = session.ReadKeyframeDraft(session.ViewModel.Effects, true, true);
        var frame = new Keyframe(time,
            value,
            (KeyframeInterpolation)Math.Max(0, session.ViewModel.Effects.Interpolation))
        {
            Exponent = session.ViewModel.Effects.ReadPowerExponent()
        };
        session.Editor.Apply("Add keyframe", document => WorkspaceDraftOperations.SetKeyframe(
            AnimationEditOperations.NormalizeRotation(document, layer.Id, ActiveTarget), layer.Id, ActiveTarget, frame));
        session.ViewModel.Timeline.EffectTarget = ActiveTarget;
        SelectKeyframe(new(layer.Id, ActiveTarget, time, time));
    }

    internal void DeleteKeyframe()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var time = session.SelectedKeyTime ?? session.ProjectPosition - layer.Start + layer.AnimationOffset;
        session.Editor.Apply("Delete keyframe", document => WorkspaceDraftOperations.UpdateLayer(
            AnimationEditOperations.NormalizeRotation(document, layer.Id, ActiveTarget), layer.Id, value => value with
        {
            Tracks = value.Tracks.Select(track => track.Target == ActiveTarget
                    ? track with
                    {
                        Keyframes = track.Keyframes.Where(frame => frame.Time != time).ToImmutableArray()
                    }
                    : track)
                .Where(track => !track.Keyframes.IsEmpty || !track.Transforms.IsEmpty).ToImmutableArray()
        }));
        ClearKeyframeSelection();
    }

    internal void MoveKeyframe(TimelineKeyframeEventArgs e)
    {
        if (e.OperationId is { } operationId)
        {
            session.Editor.UpdateLayer(e.LayerId, layer => layer with
            {
                Tracks = layer.Tracks.Select(track => track.Target != e.Target ? track : track with
                {
                    Transforms = track.Transforms.Select(operation => operation.Id != operationId ? operation :
                        e.IsOperationStart ? operation with { Start = e.NewTime <= operation.End ? e.NewTime : operation.End } :
                        operation with { End = e.NewTime >= operation.Start ? e.NewTime : operation.Start }).ToImmutableArray()
                }).ToImmutableArray()
            });
            return;
        }
        var movedTime = e.NewTime;
        session.Editor.UpdateLayer(e.LayerId, layer =>
        {
            movedTime = LayerAnimationTiming.ClampTime(layer, e.NewTime);
            return layer with
            {
                Tracks = layer.Tracks.Select(track => track.Target == e.Target
                ? track with
                {
                    Keyframes = track.Keyframes
                        .Where(frame => frame.Time != movedTime || frame.Time == e.OldTime)
                        .Select(frame =>
                            frame.Time == e.OldTime
                                ? frame with { Time = movedTime }
                                : frame).OrderBy(frame => frame.Time).ToImmutableArray()
                }
                : track).ToImmutableArray()
            };
        });
        SelectKeyframe(new(e.LayerId, e.Target, movedTime, movedTime));
    }

    internal void RefreshKeyframeAvailability()
    {
        if (session.SelectedLayer is not { } layer ||
            session.InspectorTrack(layer, ActiveTarget)?.Transforms.IsEmpty == false)
        {
            session.ViewModel.Effects.CanAddKeyframe = false;
            return;
        }

        var time = session.ProjectPosition - layer.Start + layer.AnimationOffset;
        session.ViewModel.Effects.CanAddKeyframe = LayerAnimationTiming.ClampTime(layer, time) == time;
    }

    internal void EditPath() => BeginPathEdit();
    internal void ClearPath() => UpdateLayer(layer => layer with { MotionPath = null, Tracks = layer.Tracks.Where(track => track.Property != AnimationProperty.PATH_PROGRESS).ToImmutableArray() });
    internal Task ApplySelectedPresetAsync() => session.EffectScripts.ApplySelectedAsync();
    internal void ClearKaraoke()
    {
        if (session.SelectedCue is { } cue)
        {
            session.Editor.SetSubtitleKaraokeEnabled(cue.Id, false);
        }
    }
}
