using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private static readonly HashSet<string> styleDraftProperties =
    [
        "FontFamily", "FontVariant", "FontSelectionCommitted", "FontDraft", "FontSize", "FontSizeText", "StrokeWidth", "StrokeWidthText", "Fill", "Stroke", "FillDraft", "StrokeDraft", "Bold", "Italic", "Alignment", "AlignmentSelectionCommitted", "Position",
        "ShadowX", "ShadowXText", "ShadowY", "ShadowYText", "ShadowBlur", "ShadowBlurText", "ShadowDraft", "LineHeight", "LineHeightText", "Margins", "LetterSpacing", "LetterSpacingText", "FillBlur", "FillBlurText", "StrokeBlur", "StrokeBlurText", "WrapMode"
    ];
    private static readonly HashSet<string> effectDraftProperties =
    [
        "PositionX", "PositionXText", "PositionY", "PositionYText", "ScaleX", "ScaleXText", "ScaleY", "ScaleYText",
        "Opacity", "OpacityText", "Blur", "BlurText", "Blend", "OrientPath", "KeyframeValue", "KeyframeValueText", "KeyframeValueY", "KeyframeValueYText", "KeyframeValueZ", "KeyframeValueZText", "KeyframeColorDraft", "Interpolation", "PowerExponent"
    ];

    private readonly HashSet<string> changedEffectFields = [];
    internal bool TryCommitDrafts(bool focusInvalid = true)
    {
        return TryCommitDraftsCore(focusInvalid, false);
    }

    internal bool TryCommitDraftsForClose(AegiNext.Application.Tasks.AegiTaskScopeCloseLease closeLease)
    {
        ArgumentNullException.ThrowIfNull(closeLease);
        if (!closing || closeLease.ScopeId != TaskScope || applicationContext.Tasks.GetSnapshots().Any(task =>
            task.ScopeId == TaskScope && !task.IsFinished))
        {
            throw new InvalidOperationException("Closing drafts require a drained project scope.");
        }
        return TryCommitDraftsCore(true, true);
    }

    private bool TryCommitDraftsCore(bool focusInvalid, bool allowClosing)
    {
        NumericGestureCancellationRequested?.Invoke(this, EventArgs.Empty);
        if (IsUpdating || closing && !allowClosing)
        {
            return !closing || allowClosing;
        }

        var document = editor.Snapshot;
        var prepared = document;
        if (!Details.TryPrepare(document, out prepared))
        {
            ViewModel.InvalidPanelId = "subtitleDetails";
            ViewModel.InvalidFieldKey = Details.InvalidFieldKey;
            if (focusInvalid)
            {
                ViewModel.FocusDraftError();
            }
            return false;
        }
        Guid? invalidRow = null;
        try
        {
            var changes = new Dictionary<Guid, SubtitleLine>();
            foreach (var row in ViewModel.Subtitles.Rows.Where(value => value.IsDirty))
            {
                invalidRow = row.Id;
                ViewModel.InvalidPanelId = "subtitles";
                ViewModel.InvalidFieldKey = row.StartText != TimelineTimeText.Format(row.Original.Start) ? "StartText" : row.EndText != TimelineTimeText.Format(row.Original.End) ? "EndText" : "Text";
                var line = prepared.Subtitles.FirstOrDefault(value => value.Id == row.Id);
                if (line is not null)
                {
                    changes.Add(row.Id, row.CreateEditedLine(line, prepared));
                }
            }

            invalidRow = null;
            foreach (var change in changes.Values)
            {
                prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, change.Id, _ => change);
            }

            prepared = PrepareInspectorDrafts(prepared, false);
            prepared = MaskEditing.Prepare(prepared);
            prepared = ViewModel.Effects.PrepareOperationDraft(prepared);
            prepared = PropertyEditing.Prepare(prepared);
            prepared = ViewModel.Effects.PrepareInstanceDraft(prepared);

            if (!ReferenceEquals(prepared, document))
            {
                ProjectValidator.Validate(prepared);
            }
            ViewModel.InvalidPanelId = "export";
            try
            {
                if (ViewModel.Export.IsBurnIn)
                {
                    ViewModel.Export.AcceptSettings(ViewModel.Export.CaptureSettings());
                }
            }
            catch (ExportSettingsValidationException error)
            {
                ViewModel.InvalidFieldKey = error.FieldKey;
                throw new InvalidDataException(Localization.Get(error.LocalizationKey), error);
            }
            var originalStylesDirty = stylesDirty;
            var originalEffectsDirty = effectsDirty;
            stylesDirty = false;
            effectsDirty = false;
            var originalFields = changedEffectFields.ToArray();
            var originalTarget = SceneEditing.DraftTarget;
            SceneEditing.DraftTarget = null;
            changedEffectFields.Clear();
            ClearInspectorPreview();
            MaskEditing.AcceptDrafts();
            ViewModel.Effects.AcceptOperationDraft();
            PropertyEditing.Accept();
            try
            {
                if (prepared != document)
                {
                    prepared = OverlayTimingPreview(prepared);
                    ProjectValidator.Validate(prepared);
                    InvalidateTimingSession();
                    editor.Apply("Commit workspace drafts", _ => prepared);
                    ViewModel.Effects.AcceptInstanceDraft(prepared);
                }
                else
                {
                    RefreshCommittedDrafts(changes, originalStylesDirty, originalEffectsDirty, originalFields);
                }
            }
            catch
            {
                stylesDirty = originalStylesDirty;
                effectsDirty = originalEffectsDirty;
                changedEffectFields.UnionWith(originalFields);
                SceneEditing.DraftTarget = originalTarget;
                throw;
            }

            ViewModel.InvalidPanelId = null;
            ViewModel.InvalidFieldKey = null;
            ViewModel.Subtitles.ValidationError = null;
            ViewModel.Subtitles.InvalidRowId = null;
            ViewModel.Effects.ValidationError = null;
            ViewModel.Effects.InvalidFieldKey = null;
            ViewModel.Masks.ValidationError = null;
            ViewModel.Masks.InvalidFieldKey = null;
            lastDraftDiagnostic = null;
            return true;
        }
        catch (Exception error)
        {
            ViewModel.Subtitles.InvalidRowId = invalidRow;
            ViewModel.Subtitles.ValidationError = error.Message;
            ReportDraftError(error, focusInvalid);
            return false;
        }
    }

    private void RefreshCommittedDrafts(Dictionary<Guid, SubtitleLine> changes, bool refreshStyles, bool refreshEffects,
        string[] effectFields)
    {
        using var updateLease = BeginWorkbenchUpdate();
        try
        {
            if (changes.Count > 0)
            {
                foreach (var row in ViewModel.Subtitles.Rows)
                {
                    if (changes.TryGetValue(row.Id, out var line))
                    {
                        row.Accept(line);
                    }
                }
            }

            MaskEditing.Refresh(true);
            if (refreshStyles || refreshEffects)
            {
                RefreshInspector();
                if (refreshStyles)
                {
                    OnStylePropertyChanged(this, new(nameof(ViewModel.Styles.Subtitle.FontSize)));
                    OnStylePropertyChanged(this, new(nameof(ViewModel.Styles.Subtitle.StrokeWidth)));
                }
                if (refreshEffects)
                {
                    foreach (var field in effectFields)
                    {
                        var property = field.EndsWith("Text", StringComparison.Ordinal) ? field[..^4] : field;
                        OnEffectPropertyChanged(this, new(property));
                    }
                }
            }
            else if (ViewModel.Effects.SelectedOperation is not null)
            {
                ViewModel.Effects.RefreshTransformOperations(SelectedLayer is { } layer ? InspectorTrack(layer, SceneEditing.Target) : null);
            }
            RefreshEditingPreview();
        }
        finally
        {
            updateLease.Dispose();
        }
    }

    internal AnimationValue ReadKeyframeDraft(Panels.Effects.EffectsPanelViewModel vm, bool changed, bool readAll = false)
    {
        var target = RotationTargetCompatibility.Canonicalize(SceneEditing.DraftTarget?.Target ?? SceneEditing.Target);
        var property = target.Property;
        if (AnimationPropertyMetadata.GetValueKind(property) == AnimationValueKind.COLOR)
        {
            return ReadColorDraft(vm.KeyframeColorDraft, "KeyframeColorInput");
        }
        var layer = SelectedLayer ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var edit = SceneEditing.DraftTarget ?? AnimationTarget;
        var track = InspectorTrack(layer, target);
        var value = track is not null && edit is not null ? SceneEvaluator.EvaluateTrack(track, edit.LocalTime) :
            EffectPropertyValue(layer, target);
        double Read(string text, string field, int component)
        {
            ViewModel.InvalidFieldKey = field;
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, InterfaceCulture, out var number) || !double.IsFinite(number) ||
                number < AnimationPropertyMetadata.GetMinimum(property, component) || number > AnimationPropertyMetadata.GetMaximum(property, component))
            {
                throw new InvalidDataException(Localization.Get("Workbench.Property"));
            }
            return number;
        }
        if (value.Kind == AnimationValueKind.SCALAR)
        {
            return changed || readAll ? Read(vm.KeyframeValueText, "KeyframeValueInput", 0) : value;
        }
        var texts = new[] { vm.KeyframeValueText, vm.KeyframeValueYText, vm.KeyframeValueZText };
        var fields = new[] { "KeyframeValue", "KeyframeValueY", "KeyframeValueZ" };
        var inputs = new[] { "KeyframeValueXInput", "KeyframeValueYInput", "KeyframeValueZInput" };
        for (var component = 0; component < value.ComponentCount; component++)
        {
            if (readAll || changedEffectFields.Overlaps([fields[component], fields[component] + "Text"]))
            {
                value = value.WithComponent(component, Read(texts[component], inputs[component], component));
            }
        }
        return value;
    }

    private SceneColor ReadColorDraft(ColorDraft draft, string field)
    {
        if (!draft.TryCommit(out var value))
        {
            ViewModel.InvalidFieldKey = field;
            throw new InvalidDataException(draft.Error ?? Localization.Get("Workbench.Fill"));
        }
        return value;
    }

    private double ReadNumber(string text, double original, string label, string fieldKey)
    {
        var number = RequiredNumber(text, label, fieldKey);
        return (decimal)number == (decimal)original ? original : number;
    }

    private double RequiredNumber(string text, string label, string fieldKey)
    {
        ViewModel.InvalidFieldKey = fieldKey;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float, InterfaceCulture, out var number))
        {
            throw new InvalidDataException(Localization.Get("Workbench." + (label)));
        }
        var (minimum, maximum) = fieldKey switch
        {
            "FontSizeInput" => (0.01m, 4096m),
            "StrokeWidthInput" => ((decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.STROKE_WIDTH),
                (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.STROKE_WIDTH)),
            "LineHeightInput" => (0.1m, 10m),
            "ShadowXInput" or "ShadowYInput" => (-1000000000m, 1000000000m),
            "ShadowBlurInput" or "FillBlurInput" or "StrokeBlurInput" => (0m, 512m),
            "LetterSpacingInput" => (-4096m, 4096m),
            "LayerWidthInput" or "LayerHeightInput" => (1m, 32768m),
            "PositionXInput" or "PositionYInput" => (-2000032768m, 2000032768m),
            "ScaleXInput" or "ScaleYInput" => ((decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.SCALE),
                (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.SCALE)),
            "RotationInput" => ((decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.ROTATION),
                (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.ROTATION)),
            "OpacityInput" => (0m, 1m),
            "BlurInput" => ((decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.BLUR),
                (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.BLUR)),
            "QualityInput" => (ViewModel.Export.QualityMinimum, ViewModel.Export.QualityMaximum),
            "AudioBitrateInput" => (32m, 512m),
            "VideoBitrateInput" => (0.1m, 200m),
            _ => (decimal.MinValue, decimal.MaxValue)
        };
        if (number < minimum || number > maximum)
        {
            throw new InvalidDataException(Localization.Get("Workbench." + (label)));
        }
        return (double)number;
    }

    internal void CommitRow(SubtitleRow row)
    {
        if (!IsProjectBusy && !IsUpdating && row.IsDirty)
        {
            TryCommitDrafts(false);
        }
    }

    internal void SetTextCaret(Guid id, int caret) => textCarets[id] = caret;

    private void OnStylePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "FontSize":
                ViewModel.Styles.Subtitle.FontSizeText = SynchronizeNumericText(ViewModel.Styles.Subtitle.FontSizeText, ViewModel.Styles.Subtitle.FontSize);
                break;
            case "StrokeWidth":
                ViewModel.Styles.Subtitle.StrokeWidthText = SynchronizeNumericText(ViewModel.Styles.Subtitle.StrokeWidthText, ViewModel.Styles.Subtitle.StrokeWidth);
                break;
        }

        if (!IsUpdating && ViewModel.Styles.Subtitle.IsActive && e.PropertyName is { } name && styleDraftProperties.Contains(name))
        {
            ViewModel.Styles.Subtitle.NotifyDraftChanged(name);
        }
    }

    private void OnEffectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "KeyframeValueY":
                ViewModel.Effects.KeyframeValueYText = SynchronizeNumericText(ViewModel.Effects.KeyframeValueYText, ViewModel.Effects.KeyframeValueY);
                break;
            case "KeyframeValueZ":
                ViewModel.Effects.KeyframeValueZText = SynchronizeNumericText(ViewModel.Effects.KeyframeValueZText, ViewModel.Effects.KeyframeValueZ);
                break;
            case "KeyframeValue":
                ViewModel.Effects.KeyframeValueText = SynchronizeNumericText(ViewModel.Effects.KeyframeValueText, ViewModel.Effects.KeyframeValue);
                break;
        }

        if (IsUpdating || e.PropertyName is null)
        {
            return;
        }

        if (effectDraftProperties.Contains(e.PropertyName))
        {
            effectsDirty = true;
            draftRevision++;
            NotifyTaskInputChanged();
            FreezeDraftTarget();
            changedEffectFields.Add(e.PropertyName);
            QueueInspectorPreview();
        }
        else if (e.PropertyName == "EditMode")
        {
            RefreshEditingPreview();
        }
        else if (e.PropertyName == "Property")
        {
            using var updateLease = BeginWorkbenchUpdate();
            try
            {
                ViewModel.Timeline.EffectTarget = SceneEditing.Target;
                RefreshKeyframeInspector();
                RefreshInspector();
            }
            finally
            {
                updateLease.Dispose();
            }
        }
    }

    internal void RefreshDocument(bool preserveDrafts = false)
    {
        using var updateLease = BeginWorkbenchUpdate();
        try
        {
            var document = editor.Snapshot;
            RefreshSubtitleTracks();
            Volatile.Write(ref previewState, CreatePreviewState());
            var oldRows = ViewModel.Subtitles.Rows.ToDictionary(value => value.Id);
            var rows = document.Subtitles.Select((line, index) =>
            {
                if (!oldRows.TryGetValue(line.Id, out var row) || row.Number != index + 1)
                {
                    return new SubtitleRow(line, index + 1);
                }

                if (!row.IsDirty || line != row.Original)
                {
                    row.Accept(line);
                }

                return row;
            }).ToArray();
            if (!ViewModel.Subtitles.Rows.SequenceEqual(rows))
            {
                ViewModel.Subtitles.Rows = rows;
            }

            if (!document.Subtitles.Any(value => value.Id == SelectedCueId))
            {
                SelectedCueId = null;
            }
            ViewModel.Subtitles.RefreshColorTags();
            var layers = document.Layers;
            if (!layers.Any(value => value.Id == SelectedLayerId))
            {
                SelectedLayerId = SelectedCueId is { } cueId ? document.Layers.FirstOrDefault(layer => layer.SubtitleId == cueId)?.Id : null;
            }

            if (SelectedKeyTime is { } selectedTime && SelectedLayer?.Tracks.Any(track => track.Keyframes.Any(frame => frame.Time == selectedTime)) != true)
            {
                SelectedKeyTime = null;
            }
            SyncCurrentTrackForSelection();
            RefreshSubtitleSelection();
            ViewModel.Subtitles.SelectedRow = rows.FirstOrDefault(value => value.Id == SelectedCueId);
            RefreshTitle();
            ViewModel.Timeline.Document = document;
            ViewModel.Timeline.SelectedCueId = SelectedCueId;
            ViewModel.Timeline.SelectedLayer = SelectedLayer;
            var selectedIds = ViewModel.Effects.SelectedIds.Where(id => layers.Any(layer => layer.Id == id)).ToArray();
            if (SelectedLayerId is { } primary && !selectedIds.Contains(primary))
            {
                selectedIds = [.. selectedIds, primary];
            }
            ViewModel.Effects.SelectedIds = selectedIds;
            ViewModel.Timeline.SelectedLayerIds = selectedIds;
            ViewModel.Effects.Document = document;
            ViewModel.Effects.SelectedLayer = SelectedLayer;
            ViewModel.Effects.RefreshPropertyGrid();
            MaskEditing.Refresh(!preserveDrafts);
            RefreshInspector();
            ViewModel.Styles.Subtitle.CanApplyPreset = HasStyleSelection(LayerKind.SUBTITLE) && !IsProjectBusy && !closing && ViewModel.Styles.Subtitle.SelectedPreset is not null;
            Tick();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            updateLease.Dispose();
        }
    }

    private void RefreshInspector()
    {
        var layer = SelectedLayer;
        var placement = ResolvePlacement(editor.Snapshot, layer);
        ViewModel.Styles.Load(editor.Snapshot, layer, TimelineClipIds().Length);

        var effects = ViewModel.Effects;
        if (!effectsDirty)
        {
            var transform = layer?.Transform ?? new();
            if (layer is not null)
            {
                transform = transform with
                {
                    Position = InspectorVector(layer, AnimationProperty.POSITION, transform.Position),
                    Scale = InspectorVector(layer, AnimationProperty.SCALE, transform.Scale)
                };
            }
            var position = InspectorPosition(layer, placement);
            effects.CanEditPosition = position is not null;
            if (position is { } displayedPosition)
            {
                effects.PositionXDraft.Load(displayedPosition.X);
                effects.PositionYDraft.Load(displayedPosition.Y);
            }
            else
            {
                effects.PositionXDraft.RawText = string.Empty;
                effects.PositionYDraft.RawText = string.Empty;
            }
            effects.ScaleXDraft.Load(transform.ScaleX);
            effects.ScaleYDraft.Load(transform.ScaleY);
            effects.OpacityDraft.Load(layer is null ? 1 : InspectorValue(layer, AnimationProperty.OPACITY, layer.Opacity));
            effects.BlurDraft.Load(layer is null ? 0 : InspectorValue(layer, AnimationProperty.BLUR, layer.Blur));
            effects.Blend = (int)(layer?.Blend ?? BlendMode.NORMAL);
            effects.OrientPath = layer?.MotionPath?.OrientToPath ?? false;
            RefreshKeyframeInspector();
        }

        effects.RefreshPropertyGrid();
        RefreshEditingTargetLabel();
        effectScripts?.RefreshChoices();
    }

    private LayerPositionDraftGeometry? PreparePositionGeometry(LayerPositionDraftGeometry? geometry, ProjectLayer? layer)
    {
        if (geometry is null || layer is null)
        {
            return geometry;
        }
        return geometry with { Transform = PositionGeometryTransform(layer) };
    }

    private Rendering.LayerPlacementResolution ResolvePlacement(ProjectDocument document, ProjectLayer? layer)
    {
        var result = layerPlacement.Resolve(document, projectDirectory, layer, AnimationTarget?.LocalTime);
        if (result.Error is { } error)
        {
            placementDiagnostic = new InvalidDataException($"{Localization.Get("Workbench.SubtitlePositionUnavailable")}: {error.Message}", error);
            SetDiagnosticError("Subtitle placement", placementDiagnostic);
            ShowError(placementDiagnostic, false);
        }
        else if (placementDiagnostic is not null)
        {
            var source = placementDiagnostic.InnerException;
            if (ReferenceEquals(LastError, placementDiagnostic) ||
                source is not null && LastError is { } previous && previous.GetType() == source.GetType() && previous.Message == source.Message)
            {
                LastError = null;
                ViewModel.Error = null;
            }

            placementDiagnostic = null;
            SetDiagnosticError("Subtitle placement", null);
        }

        return result;
    }

    private void RestorePlacementDiagnostic()
    {
        if (LastError is null && placementDiagnostic is { } error)
        {
            ShowError(error, false);
        }
    }

    internal void SelectCue(Guid id)
    {
        if (IsUpdating || IsProjectBusy || id == SelectedCueId && SelectedLayer?.SubtitleId == id && SelectedSubtitleIds.Count <= 1 &&
            !ViewModel.Effects.HasSelectedEffectInstance)
        {
            return;
        }

        if (!TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        ViewModel.CancelGestures();
        InvalidateTimingSession();
        var revealed = RevealSubtitleColorTagTargets([id]);
        SynchronizeCueSelection(id);
        RefreshDocument();
        SeekSelectedClipOnSelection();
        if (revealed)
        {
            SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SynchronizeCueSelection(Guid id)
    {
        ViewModel.Effects.SelectInstance(null);
        ViewModel.Timeline.ClearInstanceSelection();
        ResetSubtitleSelection(id);
        SelectedCueId = id;
        ViewModel.Effects.SelectedIds = [];
        SelectedLayerId = editor.Snapshot.Layers.FirstOrDefault(layer => layer.SubtitleId == id)?.Id;
        SelectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
    }

    internal void SelectLayer(Guid id, Guid[] selectedIds)
    {
        if (IsUpdating || IsProjectBusy)
        {
            return;
        }

        if ((id != SelectedLayerId || !ViewModel.Effects.SelectedIds.ToHashSet().SetEquals(selectedIds) ||
             ViewModel.Effects.HasInstanceDrafts) && !TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        ViewModel.CancelGestures();
        if (id != SelectedLayerId || !ViewModel.Effects.SelectedIds.ToHashSet().SetEquals(selectedIds))
        {
            InvalidateTimingSession();
        }
        SelectedLayerId = id;
        ViewModel.Effects.SelectInstance(null);
        ViewModel.Timeline.ClearInstanceSelection();
        var revealed = RevealSubtitleColorTagTargets(editor.Snapshot.Layers.Where(layer => selectedIds.Contains(layer.Id) || layer.Id == id)
            .Select(layer => layer.SubtitleId).OfType<Guid>());
        ViewModel.Effects.SelectedIds = selectedIds;
        SelectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        if (SelectedLayer?.SubtitleId is { } cueId)
        {
            SelectedCueId = cueId;
        }

        SynchronizeSubtitleSelectionFromLayers();
        RefreshDocument();
        SeekSelectedClipOnSelection();
        if (revealed)
        {
            SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        }
    }

}
