using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ShapeStyleViewModel : StyleEditorViewModel
{
    internal ShapeStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += (_, _) => MarkDraftChanged();
        Height.PropertyChanged += (_, _) => MarkDraftChanged();
        StrokeWidth.PropertyChanged += (_, _) => MarkDraftChanged();
        FillDraft.Changed += (_, _) => MarkDraftChanged();
        StrokeDraft.Changed += (_, _) => MarkDraftChanged();
    }

    public override LayerKind Kind => LayerKind.SHAPE;
    public NumericValueDraft Width { get; } = new();
    public NumericValueDraft Height { get; } = new();
    public NumericValueDraft StrokeWidth { get; } = new();
    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public bool CanEditStrokeWidth => Session.SelectedLayer?.Tracks.Any(track => track.Property == AnimationProperty.STROKE_WIDTH && !track.Transforms.IsEmpty) != true;

    protected override void LoadCore(ProjectDocument document, ProjectLayer layer)
    {
        Width.Load(layer.Shape!.Width);
        Height.Load(layer.Shape.Height);
        FillDraft.Load(Session.EvaluateStyleColor(layer, layer.Fill, false));
        StrokeDraft.Load(Session.EvaluateStyleColor(layer, layer.Stroke, true));
        StrokeWidth.Load(Session.EvaluateStyleStrokeWidth(layer));
        OnPropertyChanged(nameof(CanEditStrokeWidth));
    }

    protected override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview) => Session.PrepareShapeStyleDraft(document, layer, this);

    public override bool RestoreField(string fieldKey)
    {
        if (!IsActive || Session.SelectedLayer is not { Shape: { } shape } layer ||
            fieldKey is not ("LayerWidthInput" or "LayerHeightInput" or "StrokeWidthInput"))
        {
            return false;
        }
        Session.RestoreGraphicStyleField(() =>
        {
            switch (fieldKey)
            {
                case "LayerWidthInput": Width.Load(shape.Width); break;
                case "LayerHeightInput": Height.Load(shape.Height); break;
                case "StrokeWidthInput": StrokeWidth.Load(Session.EvaluateStyleStrokeWidth(layer)); break;
            }
        });
        return true;
    }
}
