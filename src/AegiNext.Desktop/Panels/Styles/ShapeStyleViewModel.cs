using System.ComponentModel;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class ShapeStyleViewModel : StyleEditorViewModel
{
    private static readonly ShapeKind[] PARAMETER_KINDS = [ShapeKind.RECTANGLE, ShapeKind.ELLIPSE, ShapeKind.LINE, ShapeKind.ARROW];
    private int shapeType;
    private bool isPath;
    private bool cornersLinked = true;
    private bool synchronizingCorners;
    private Guid? cornerLayerId;
    private ShapeCornerRadii? loadedCorners;
    private NumericValueDraft? linkedCornerSource;
    private string[]? linkedCornerOriginals;

    internal ShapeStyleViewModel(WorkbenchSession session) : base(session)
    {
        Width.PropertyChanged += (_, _) => MarkDraftChanged();
        Height.PropertyChanged += (_, _) => MarkDraftChanged();
        StrokeWidth.PropertyChanged += (_, _) => MarkDraftChanged();
        foreach (var draft in new[] { ArrowHead, ArrowShaft })
        {
            draft.PropertyChanged += (_, _) => MarkDraftChanged();
        }
        foreach (var draft in CornerDrafts)
        {
            draft.PropertyChanged += OnCornerChanged;
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
    public NumericValueDraft CornerTopLeft { get; } = new();
    public NumericValueDraft CornerTopRight { get; } = new();
    public NumericValueDraft CornerBottomRight { get; } = new();
    public NumericValueDraft CornerBottomLeft { get; } = new();
    public bool CornersLinked
    {
        get => cornersLinked;
        set
        {
            if (SetProperty(ref cornersLinked, value))
            {
                ResetLinkedCornerEdit();
            }
        }
    }
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
    public bool IsRectangle => !isPath && SelectedKind == ShapeKind.RECTANGLE;
    public bool IsArrow => !isPath && SelectedKind == ShapeKind.ARROW;
    public bool CanMatchSubtitleTiming => IsActive && Session.Graphics.CanMatchSubtitleTiming;
    internal ShapeKind SelectedKind => isPath ? ShapeKind.PATH : PARAMETER_KINDS[ShapeType];
    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public bool CanEditStrokeWidth => Session.SelectedLayer?.Tracks.Any(track => track.Property == AnimationProperty.STROKE_WIDTH && !track.Transforms.IsEmpty) != true;
    private NumericValueDraft[] CornerDrafts => [CornerTopLeft, CornerTopRight, CornerBottomRight, CornerBottomLeft];

    protected override void LoadCore(ProjectDocument document, ProjectLayer layer)
    {
        ResetLinkedCornerEdit();
        var shape = layer.Shape!;
        isPath = shape.Kind == ShapeKind.PATH;
        ShapeType = Math.Max(0, Array.IndexOf(PARAMETER_KINDS, shape.Kind == ShapeKind.ROUNDED_RECTANGLE ? ShapeKind.RECTANGLE : shape.Kind));
        Width.Load(shape.Width);
        Height.Load(shape.Height);
        var corners = shape.EffectiveCornerRadii;
        if (cornerLayerId != layer.Id || loadedCorners != corners)
        {
            CornersLinked = corners.IsUniform;
        }
        cornerLayerId = layer.Id;
        loadedCorners = corners;
        LoadCorners(corners);
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

    internal override Action? CaptureNumericGesture(string? fieldKey)
    {
        if (!CornersLinked || !IsCornerField(fieldKey))
        {
            return null;
        }
        var originals = CornerDrafts.Select(draft => draft.RawText).ToArray();
        return () => Session.RestoreGraphicStyleField(() =>
        {
            ResetLinkedCornerEdit();
            var drafts = CornerDrafts;
            for (var index = 0; index < drafts.Length; index++)
            {
                drafts[index].RawText = originals[index];
            }
        });
    }

    public override bool RestoreField(string fieldKey)
    {
        if (!IsActive || Session.SelectedLayer is not { Shape: { } shape } layer ||
            fieldKey is not ("LayerWidthInput" or "LayerHeightInput" or "StrokeWidthInput" or "ShapeArrowHeadInput" or "ShapeArrowShaftInput") && !IsCornerField(fieldKey))
        {
            return false;
        }
        Session.RestoreGraphicStyleField(() =>
        {
            var cornerIndex = CornerFieldIndex(fieldKey);
            if (CornersLinked && cornerIndex >= 0 && linkedCornerOriginals is { } originals)
            {
                var drafts = CornerDrafts;
                if (ReferenceEquals(linkedCornerSource, drafts[cornerIndex]))
                {
                    for (var index = 0; index < drafts.Length; index++)
                    {
                        if (index != cornerIndex)
                        {
                            drafts[index].RawText = originals[index];
                        }
                    }
                    ResetLinkedCornerEdit();
                }
            }
            switch (fieldKey)
            {
                case "LayerWidthInput": Width.Load(shape.Width); break;
                case "LayerHeightInput": Height.Load(shape.Height); break;
                case "StrokeWidthInput": StrokeWidth.Load(Session.EvaluateStyleStrokeWidth(layer)); break;
                case "ShapeCornerTopLeftInput": CornerTopLeft.Load(shape.EffectiveCornerRadii.TopLeft); break;
                case "ShapeCornerTopRightInput": CornerTopRight.Load(shape.EffectiveCornerRadii.TopRight); break;
                case "ShapeCornerBottomRightInput": CornerBottomRight.Load(shape.EffectiveCornerRadii.BottomRight); break;
                case "ShapeCornerBottomLeftInput": CornerBottomLeft.Load(shape.EffectiveCornerRadii.BottomLeft); break;
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
        OnPropertyChanged(nameof(IsRectangle));
        OnPropertyChanged(nameof(IsArrow));
        OnPropertyChanged(nameof(CanMatchSubtitleTiming));
    }

    private void LoadCorners(ShapeCornerRadii corners)
    {
        CornerTopLeft.Load(corners.TopLeft);
        CornerTopRight.Load(corners.TopRight);
        CornerBottomRight.Load(corners.BottomRight);
        CornerBottomLeft.Load(corners.BottomLeft);
    }

    private static bool IsCornerField(string? fieldKey) => CornerFieldIndex(fieldKey) >= 0;

    private static int CornerFieldIndex(string? fieldKey) => fieldKey switch
    {
        "ShapeCornerTopLeftInput" => 0,
        "ShapeCornerTopRightInput" => 1,
        "ShapeCornerBottomRightInput" => 2,
        "ShapeCornerBottomLeftInput" => 3,
        _ => -1
    };

    private void ResetLinkedCornerEdit()
    {
        linkedCornerSource = null;
        linkedCornerOriginals = null;
    }

    private void OnCornerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(NumericValueDraft.RawText) || sender is not NumericValueDraft changed ||
            IsLoading || Session.IsUpdating || !IsActive || synchronizingCorners)
        {
            return;
        }
        if (CornersLinked && changed.Parse() is >= 0 and <= 1000000)
        {
            if (!HasDrafts || !ReferenceEquals(linkedCornerSource, changed))
            {
                linkedCornerSource = changed;
                linkedCornerOriginals = CornerDrafts.Select(draft => draft.RawText).ToArray();
            }
            synchronizingCorners = true;
            try
            {
                foreach (var draft in CornerDrafts)
                {
                    if (!ReferenceEquals(draft, changed) && draft.Parse() is >= 0 and <= 1000000)
                    {
                        draft.RawText = changed.RawText;
                    }
                }
            }
            finally
            {
                synchronizingCorners = false;
            }
        }
        MarkDraftChanged();
    }
}
