using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool RestoreStyleNumericDraftField(string fieldKey)
    {
        if (SelectedLayer?.SubtitleId is null || SelectedCue is not { } cue || IsUpdating || closing)
        {
            return false;
        }
        var value = fieldKey switch
        {
            "ReferenceWidthInput" => cue.Style.Scaling.ReferenceWidth,
            "ReferenceHeightInput" => cue.Style.Scaling.ReferenceHeight,
            "MatchWidthOrHeightInput" => cue.Style.Scaling.MatchWidthOrHeight,
            "LetterSpacingInput" => InspectorValue(SelectedLayer, AnimationProperty.LETTER_SPACING, cue.Style.LetterSpacing),
            "FillBlurInput" => InspectorValue(SelectedLayer, AnimationProperty.FILL_BLUR, cue.Style.FillBlur),
            "StrokeBlurInput" => InspectorValue(SelectedLayer, AnimationProperty.STROKE_BLUR, cue.Style.StrokeBlur),
            "LineHeightInput" => cue.Style.LineHeight,
            "MarginLeftInput" => cue.Style.Margins.Left,
            "MarginRightInput" => cue.Style.Margins.Right,
            "MarginVerticalInput" => cue.Style.Margins.Vertical,
            "ShadowXInput" => cue.Style.ShadowOffset.X,
            "ShadowYInput" => cue.Style.ShadowOffset.Y,
            "ShadowBlurInput" => cue.Style.ShadowBlur,
            _ => (double?)null
        };
        if (value is not { } number)
        {
            return false;
        }
        var vm = ViewModel.Styles.Subtitle;
        using var updateLease = BeginWorkbenchUpdate();
        try
        {
            if (fieldKey is "ReferenceWidthInput" or "ReferenceHeightInput" or "MatchWidthOrHeightInput")
            {
                vm.Scaling.LoadField(fieldKey, number, InterfaceCulture);
            }
            else
            {
                vm.LoadStyleNumber(fieldKey, number, InterfaceCulture);
            }
            draftRevision++;
            if (ViewModel.InvalidPanelId == "styles" && ViewModel.InvalidFieldKey == fieldKey)
            {
                ViewModel.InvalidPanelId = null;
                ViewModel.InvalidFieldKey = null;
                lastDraftDiagnostic = null;
                lastDraftFocus = null;
            }
        }
        finally
        {
            updateLease.Dispose();
        }
        QueueInspectorPreview(true);
        return true;
    }
}
