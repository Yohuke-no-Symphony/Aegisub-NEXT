using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Editing;

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
        if (ViewModel.InvalidPanelId == "effects" || sharedPlacement || animationOptions)
        {
            ViewModel.Effects.InvalidFieldKey = ViewModel.InvalidFieldKey;
            ViewModel.Effects.ValidationError = error.Message;
        }
        if (ViewModel.InvalidPanelId == "masks" || sharedPlacement)
        {
            ViewModel.Masks.InvalidFieldKey = ViewModel.InvalidFieldKey;
            ViewModel.Masks.ValidationError = error.Message;
        }
        var focus = (draftRevision, ViewModel.InvalidPanelId, ViewModel.InvalidFieldKey);
        if (focusInvalid && lastDraftFocus != focus)
        {
            lastDraftFocus = focus;
            ViewModel.FocusDraftError();
        }
    }

    private double ReadEffectNumber(string text, double original, string label, string field, string property) =>
        changedEffectFields.Contains(property) ? ReadNumber(text, original, label, field) : original;

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
                case "LayerStart": vm.LayerStart = TimelineTimeText.Format(layer.Start); break;
                case "LayerEnd": vm.LayerEnd = TimelineTimeText.Format(layer.End); break;
                case "PositionX": vm.PositionX = position is { } x ? (decimal)x.X : null; break;
                case "PositionY": vm.PositionY = position is { } y ? (decimal)y.Y : null; break;
                case "ScaleX": vm.ScaleX = (decimal)InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).X; break;
                case "ScaleY": vm.ScaleY = (decimal)InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).Y; break;
                case "Opacity": vm.Opacity = (decimal)InspectorValue(layer, AnimationProperty.OPACITY, layer.Opacity); break;
                case "Blur": vm.Blur = (decimal)InspectorValue(layer, AnimationProperty.BLUR, layer.Blur); break;
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
                    var subtitle = layer.SubtitleId is { } subtitleId ? DocumentSnapshot.Subtitles.Single(line => line.Id == subtitleId) : null;
                    var value = track is null ? SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, identity) : SceneEvaluator.EvaluateTrack(track, target.LocalTime);
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
            switch (propertyName)
            {
                case "PositionX": vm.PositionXText = vm.PositionX?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "PositionY": vm.PositionYText = vm.PositionY?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "ScaleX": vm.ScaleXText = vm.ScaleX?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "ScaleY": vm.ScaleYText = vm.ScaleY?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "Opacity": vm.OpacityText = vm.Opacity?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "Blur": vm.BlurText = vm.Blur?.ToString(InterfaceCulture) ?? string.Empty; break;
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
}
