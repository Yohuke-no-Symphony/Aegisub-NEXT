using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool HasEffectDrafts => effectsDirty || PropertyEditing.HasDrafts;
    internal bool CanChangeKeyframeCurveComponent => !changedEffectFields.Overlaps(["Interpolation", "PowerExponent"]);
    internal void RefreshKeyframeCurveInspector() => layerEditing.RefreshKeyframeCurveInspector();
    private long draftRevision;
    private (long Revision, string? Panel, string? Field, string Message)? lastDraftDiagnostic;
    private (long Revision, string? Panel, string? Field)? lastDraftFocus;

    private void ReportDraftError(Exception error, bool focusInvalid)
    {
        var diagnostic = (draftRevision, ViewModel.InvalidPanelId, ViewModel.InvalidFieldKey, error.Message);
        if (lastDraftDiagnostic != diagnostic)
        {
            lastDraftDiagnostic = diagnostic;
            ShowError(error);
        }
        var sharedPlacement = ClipMaskPositionEditingCoordinator.LocalFieldKey(ViewModel.InvalidFieldKey) is not null;
        var animationOptions = ViewModel.InvalidFieldKey is "PowerExponentInput" ||
            ViewModel.InvalidFieldKey?.StartsWith("Operation", StringComparison.Ordinal) == true;
        if (ViewModel.InvalidPanelId == "effects" || animationOptions)
        {
            ViewModel.Effects.InvalidFieldKey = ViewModel.InvalidFieldKey;
            ViewModel.Effects.ValidationError = error.Message;
        }
        if (ViewModel.InvalidPanelId == "masks" || sharedPlacement)
        {
            ViewModel.Masks.InvalidFieldKey = ViewModel.InvalidFieldKey;
            ViewModel.Masks.ValidationError = error.Message;
        }
        if (ViewModel.InvalidPanelId == "timeline")
        {
            ViewModel.Timeline.InvalidClipTimingField = ViewModel.InvalidFieldKey;
            ViewModel.Timeline.ClipTimingValidationError = error.Message;
        }
        var focus = (draftRevision, ViewModel.InvalidPanelId, ViewModel.InvalidFieldKey);
        if (focusInvalid && lastDraftFocus != focus)
        {
            lastDraftFocus = focus;
            ViewModel.FocusDraftError();
        }
    }

    private double ReadEffectNumber(string text, double original, string label, string field, string property)
    {
        if (!changedEffectFields.Contains(property))
        {
            return original;
        }
        var animationProperty = field switch
        {
            "PositionXInput" or "PositionYInput" => AnimationProperty.POSITION,
            "ScaleXInput" or "ScaleYInput" => AnimationProperty.SCALE,
            "OpacityInput" => AnimationProperty.OPACITY,
            "BlurInput" => AnimationProperty.BLUR,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var minimum = animationProperty == AnimationProperty.POSITION ? -2000032768 : AnimationPropertyMetadata.GetMinimum(animationProperty);
        var maximum = animationProperty == AnimationProperty.POSITION ? 2000032768 : AnimationPropertyMetadata.GetMaximum(animationProperty);
        ViewModel.InvalidFieldKey = field;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, InterfaceCulture, out var number) || !double.IsFinite(number) ||
            number < minimum || number > maximum)
        {
            throw new InvalidDataException(Localization.Get("Workbench." + label));
        }
        return number;
    }

    internal void RestoreEffectDraftField(string fieldKey)
    {
        if (fieldKey == "RotationInput" && ViewModel.Effects.RotationRow is { } rotationRow)
        {
            rotationRow.Restore(rotationRow.ZFieldKey);
            return;
        }
        if (SelectedLayer is not { } layer || IsUpdating)
        {
            return;
        }
        var vm = ViewModel.Effects;
        var target = AnimationTarget!;
        var placement = ResolvePlacement(editor.Snapshot, layer);
        var position = InspectorPosition(layer, placement);
        var propertyName = fieldKey.EndsWith("Input", StringComparison.Ordinal) ? fieldKey[..^5] : fieldKey;
        using var updateLease = BeginWorkbenchUpdate();
        try
        {
            switch (propertyName)
            {
                case "PositionX":
                    if (position is { } x)
                    {
                        vm.PositionXDraft.Load(x.X);
                    }
                    else
                    {
                        vm.PositionXText = string.Empty;
                    }
                    break;
                case "PositionY":
                    if (position is { } y)
                    {
                        vm.PositionYDraft.Load(y.Y);
                    }
                    else
                    {
                        vm.PositionYText = string.Empty;
                    }
                    break;
                case "ScaleX": vm.ScaleXDraft.Load(InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).X); break;
                case "ScaleY": vm.ScaleYDraft.Load(InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).Y); break;
                case "Opacity": vm.OpacityDraft.Load(InspectorValue(layer, AnimationProperty.OPACITY, layer.Opacity)); break;
                case "Blur": vm.BlurDraft.Load(InspectorValue(layer, AnimationProperty.BLUR, layer.Blur)); break;
                case "PowerExponent":
                    var exponentFrame = InspectorTrack(layer, target.Target ?? SceneEditing.Target)?.Keyframes.FirstOrDefault(key => key.Time == target.LocalTime);
                    var curveComponent = vm.KeyframeCurveComponentMask == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount((uint)vm.KeyframeCurveComponentMask);
                    vm.LoadPowerExponent(exponentFrame?.GetCurve(curveComponent).Exponent ?? 1);
                    break;
                case "KeyframeValueX":
                case "KeyframeValueY":
                case "KeyframeValueZ":
                case "KeyframeValue":
                    var identity = RotationTargetCompatibility.Canonicalize(target.Target ?? SceneEditing.Target);
                    var track = InspectorTrack(layer, identity);
                    var value = track is null ? EffectPropertyValue(layer, identity) : SceneEvaluator.EvaluateTrack(track, target.LocalTime);
                    var component = propertyName == "KeyframeValueY" ? 1 : propertyName == "KeyframeValueZ" ? 2 : 0;
                    var rawText = value.GetComponent(component).ToString("R", InterfaceCulture);
                    if (propertyName == "KeyframeValueY")
                    {
                        vm.KeyframeValueYText = rawText;
                    }
                    else if (propertyName == "KeyframeValueZ")
                    {
                        vm.KeyframeValueZText = rawText;
                    }
                    else
                    {
                        vm.KeyframeValueText = rawText;
                        propertyName = "KeyframeValue";
                    }
                    break;
                default: return;
            }
            changedEffectFields.Remove(propertyName);
            changedEffectFields.Remove(propertyName + "Text");
            effectsDirty = changedEffectFields.Count > 0;
            if (!effectsDirty && !stylesDirty)
            {
                SceneEditing.DraftTarget = null;
            }
            draftRevision++;
            if (vm.InvalidFieldKey == fieldKey)
            {
                vm.InvalidFieldKey = null;
                vm.ValidationError = null;
                ViewModel.InvalidPanelId = null;
                ViewModel.InvalidFieldKey = null;
            }
        }
        finally
        {
            updateLease.Dispose();
        }
        QueueInspectorPreview(true);
    }

    internal void RestoreClipTimingField(string fieldKey)
    {
        if (IsUpdating || fieldKey is not ("TimelineClipStartInput" or "TimelineClipEndInput"))
        {
            return;
        }
        var property = fieldKey == "TimelineClipStartInput" ? "LayerStart" : "LayerEnd";
        using (BeginWorkbenchUpdate())
        {
            var timeline = ViewModel.Timeline;
            if (property == "LayerStart")
            {
                timeline.LayerStart = SelectedLayer is { } layer ? TimelineTimeText.Format(layer.Start) : string.Empty;
            }
            else
            {
                timeline.LayerEnd = SelectedLayer is { } layer ? TimelineTimeText.Format(layer.End) : string.Empty;
            }
            changedClipTimingFields.Remove(property);
            if (!HasClipTimingDrafts)
            {
                ResetClipTimingDraftState();
            }
            if (timeline.InvalidClipTimingField == fieldKey)
            {
                timeline.InvalidClipTimingField = null;
                timeline.ClipTimingValidationError = null;
                if (ViewModel.InvalidPanelId == "timeline" && ViewModel.InvalidFieldKey == fieldKey)
                {
                    ViewModel.InvalidPanelId = null;
                    ViewModel.InvalidFieldKey = null;
                }
            }
        }
        draftRevision++;
        NotifyTaskInputChanged();
        QueueInspectorPreview(true);
    }
}
