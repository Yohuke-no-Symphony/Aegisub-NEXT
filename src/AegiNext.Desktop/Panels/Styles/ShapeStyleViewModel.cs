using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ShapeStyleViewModel : StyleEditorViewModel
{
    private static readonly ShapeKind[] PARAMETER_KINDS = [ShapeKind.RECTANGLE, ShapeKind.ROUNDED_RECTANGLE, ShapeKind.ELLIPSE, ShapeKind.LINE, ShapeKind.ARROW];
    private int shapeType;
    private bool isPath;

    internal ShapeStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += (_, _) => MarkDraftChanged();
        Height.PropertyChanged += (_, _) => MarkDraftChanged();
        StrokeWidth.PropertyChanged += (_, _) => MarkDraftChanged();
        foreach (var draft in new[] { CornerRadius, ArrowHead, ArrowShaft })
        {
            draft.PropertyChanged += (_, _) => MarkDraftChanged();
        }
        FillDraft.Changed += (_, _) => MarkDraftChanged();
        StrokeDraft.Changed += (_, _) => MarkDraftChanged();
        ConvertToPathCommand = new AsyncRelayCommand(() => IsActive ? Session.Graphics.ConvertSelectedShapeToPathAsync() : Task.CompletedTask);
        EditPathCommand = new RelayCommand(() =>
        {
            if (IsActive)
            {
                Session.SetGraphicCanvasMode(CanvasEditMode.SHAPE_PATH);
            }
        });
        MatchSubtitleTimingCommand = new AsyncRelayCommand(() => IsActive ? Session.Graphics.MatchSelectedSubtitleTimingAsync() : Task.CompletedTask);
    }

    public override LayerKind Kind => LayerKind.SHAPE;
    public NumericValueDraft Width { get; } = new();
    public NumericValueDraft Height { get; } = new();
    public NumericValueDraft StrokeWidth { get; } = new();
    public NumericValueDraft CornerRadius { get; } = new();
    public NumericValueDraft ArrowHead { get; } = new();
    public NumericValueDraft ArrowShaft { get; } = new();
    public ICommand ConvertToPathCommand { get; }
    public ICommand EditPathCommand { get; }
    public ICommand MatchSubtitleTimingCommand { get; }
    public GraphicStyleChoice[] ShapeChoices { get; } = PARAMETER_KINDS.Select(kind => new GraphicStyleChoice("Workbench.Shape" + kind)).ToArray();
    public int ShapeType
    {
        get => shapeType;
        set
        {
            if (value >= 0 && value < PARAMETER_KINDS.Length && SetProperty(ref shapeType, value))
            {
                MarkDraftChanged();
                RefreshContext();
            }
        }
    }
    public bool IsPath => isPath;
    public bool IsParametric => !isPath;
    public bool IsRoundedRectangle => !isPath && SelectedKind == ShapeKind.ROUNDED_RECTANGLE;
    public bool IsArrow => !isPath && SelectedKind == ShapeKind.ARROW;
    public bool CanMatchSubtitleTiming => IsActive && Session.Graphics.CanMatchSubtitleTiming;
    internal ShapeKind SelectedKind => isPath ? ShapeKind.PATH : PARAMETER_KINDS[ShapeType];
    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public bool CanEditStrokeWidth => Session.SelectedLayer?.Tracks.Any(track => track.Property == AnimationProperty.STROKE_WIDTH && !track.Transforms.IsEmpty) != true;

    protected override void LoadCore(ProjectDocument document, ProjectLayer layer)
    {
        var shape = layer.Shape!;
        isPath = shape.Kind == ShapeKind.PATH;
        ShapeType = Math.Max(0, Array.IndexOf(PARAMETER_KINDS, shape.Kind));
        Width.Load(shape.Width);
        Height.Load(shape.Height);
        CornerRadius.Load(shape.CornerRadius);
        ArrowHead.Load(shape.ArrowHeadRatio * 100);
        ArrowShaft.Load(shape.ArrowShaftRatio * 100);
        FillDraft.Load(Session.EvaluateStyleColor(layer, layer.Fill, false));
        StrokeDraft.Load(Session.EvaluateStyleColor(layer, layer.Stroke, true));
        StrokeWidth.Load(Session.EvaluateStyleStrokeWidth(layer));
        OnPropertyChanged(nameof(CanEditStrokeWidth));
        RefreshLanguage();
        RefreshContext();
    }

    protected override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview) => Session.PrepareShapeStyleDraft(document, layer, this);

    public override bool RestoreField(string fieldKey)
    {
        if (!IsActive || Session.SelectedLayer is not { Shape: { } shape } layer ||
            fieldKey is not ("LayerWidthInput" or "LayerHeightInput" or "StrokeWidthInput" or "ShapeCornerRadiusInput" or "ShapeArrowHeadInput" or "ShapeArrowShaftInput"))
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
                case "ShapeCornerRadiusInput": CornerRadius.Load(shape.CornerRadius); break;
                case "ShapeArrowHeadInput": ArrowHead.Load(shape.ArrowHeadRatio * 100); break;
                case "ShapeArrowShaftInput": ArrowShaft.Load(shape.ArrowShaftRatio * 100); break;
            }
        });
        return true;
    }

    internal void RefreshLanguage()
    {
        foreach (var choice in ShapeChoices)
        {
            choice.RefreshLanguage();
        }
    }

    internal void RefreshContext()
    {
        OnPropertyChanged(nameof(IsPath));
        OnPropertyChanged(nameof(IsParametric));
        OnPropertyChanged(nameof(IsRoundedRectangle));
        OnPropertyChanged(nameof(IsArrow));
        OnPropertyChanged(nameof(CanMatchSubtitleTiming));
    }
}
