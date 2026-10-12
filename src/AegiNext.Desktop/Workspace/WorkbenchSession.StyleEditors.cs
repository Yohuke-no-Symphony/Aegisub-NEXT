using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private void OnStyleDraftChanged(object? sender, EventArgs e)
    {
        draftRevision++;
        NotifyTaskInputChanged();
        FreezeDraftTarget();
        QueueInspectorPreview();
    }

    internal void LoadSubtitleStyleDraft(SubtitleStyleViewModel vm, ProjectDocument document, ProjectLayer layer)
    {
        var style = document.Subtitles.Single(value => value.Id == layer.SubtitleId).Style;
        var placement = ResolvePlacement(document, layer);
        vm.LoadCanvasSize(document.Width, document.Height);
        vm.HasCue = true;
        vm.RefreshAppearanceEditing();
        vm.Scaling.Load(style.Scaling, InterfaceCulture);
        vm.LoadFont(style);
        vm.FontSize = (decimal)style.FontSize;
        vm.FontSizeText = vm.FontSize.Value.ToString(InterfaceCulture);
        vm.StrokeWidth = (decimal)InspectorValue(layer, AnimationProperty.STROKE_WIDTH, style.StrokeWidth);
        vm.StrokeWidthText = vm.StrokeWidth.Value.ToString(InterfaceCulture);
        vm.FillDraft.Load(InspectorColor(layer, style.Fill, false));
        vm.StrokeDraft.Load(InspectorColor(layer, style.Stroke, true));
        vm.LoadStyleNumbers(style with
        {
            LetterSpacing = InspectorValue(layer, AnimationProperty.LETTER_SPACING, style.LetterSpacing),
            FillBlur = InspectorValue(layer, AnimationProperty.FILL_BLUR, style.FillBlur),
            StrokeBlur = InspectorValue(layer, AnimationProperty.STROKE_BLUR, style.StrokeBlur)
        }, InterfaceCulture);
        vm.WrapMode = (int)style.WrapMode;
        vm.ShadowDraft.Load(style.ShadowColor);
        vm.Bold = style.Bold;
        vm.Italic = style.Italic;
        vm.LoadAlignment(style.Alignment);
        vm.Position.Load(style, placement.Position, placement.BasePosition is not null,
            PreparePositionGeometry(placement.Geometry, layer));
    }

    internal double ReadStyleNumber(string text, double original, string label, string fieldKey) => ReadNumber(text, original, label, fieldKey);
    internal SceneColor EvaluateStyleColor(ProjectLayer layer, SceneColor original, bool stroke) => InspectorColor(layer, original, stroke);
    internal double EvaluateStyleStrokeWidth(ProjectLayer layer) => InspectorValue(layer, AnimationProperty.STROKE_WIDTH, layer.StrokeWidth);

    internal ProjectDocument PrepareShapeStyleDraft(ProjectDocument document, ProjectLayer layer, ShapeStyleViewModel vm)
    {
        var shape = layer.Shape!;
        var geometry = ReadShapeGeometry(shape, vm);
        var fill = PrepareColor(ref document, layer, ReadColorDraft(vm.FillDraft, "FillPicker"), layer.Fill, false);
        var stroke = PrepareColor(ref document, layer, ReadColorDraft(vm.StrokeDraft, "StrokePicker"), layer.Stroke, true);
        var strokeWidth = PrepareStrokeWidth(ref document, layer, layer.StrokeWidth, vm.StrokeWidth.RawText);
        if (geometry.Kind == ShapeKind.LINE && shape.Kind != ShapeKind.LINE && strokeWidth == 0)
        {
            strokeWidth = 4;
        }
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => current with
        {
            Shape = geometry,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth
        });
    }

    internal void RestoreGraphicStyleField(Action restore)
    {
        using (BeginWorkbenchUpdate())
        {
            restore();
            draftRevision++;
            if (ViewModel.InvalidPanelId == "styles")
            {
                ViewModel.InvalidPanelId = null;
                ViewModel.InvalidFieldKey = null;
                lastDraftDiagnostic = null;
                lastDraftFocus = null;
            }
        }
        QueueInspectorPreview(true);
    }
}
