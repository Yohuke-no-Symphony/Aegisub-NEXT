using System.Windows.Input;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed class MaskPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private MaskNumericField[] sourceFields = [];
    private bool refreshingChoices;
    private bool isSnapEnabled = true;
    private string? validationError;
    private string? invalidFieldKey;

    internal MaskPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        CreateRectangleMaskCommand = new RelayCommand(() => session.MaskEditing.CreateRectangle(), () => CanCreateMask);
        CreateVectorMaskCommand = new RelayCommand(() => session.MaskEditing.CreateVector(), () => CanCreateMask);
        ToggleMaskEditingCommand = new RelayCommand(() => session.MaskEditing.ToggleEditing(), () => CanEditExistingMask);
        AddMaskContourCommand = new RelayCommand(() => session.MaskEditing.AddContour());
        ClearMaskCommand = EditCommand(() => session.MaskEditing.Clear());
        InvertMaskCommand = EditCommand(() => session.MaskEditing.Invert());
        SubdivideMaskCommand = EditCommand(() => session.MaskEditing.Subdivide());
        DeleteContourCommand = EditCommand(() => session.MaskEditing.DeleteSelectedContour());
        DeleteNodeCommand = EditCommand(() => session.MaskEditing.DeleteSelectedNode());
    }

    public bool CanEditMask => session.MaskEditing.CanEdit;
    public bool CanCreateMask => session.MaskEditing.CanCreate;
    public bool CanEditExistingMask => CanEditMask && HasClipMask;
    public bool IsMaskEditing => session.MaskEditing.IsEditing;
    public bool IsRectangleTool => session.SceneEditing.Mode == CanvasEditMode.MASK_RECTANGLE;
    public bool IsVectorTool => session.SceneEditing.Mode is CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR;
    public bool CanSubdivide => session.MaskEditing.CanSubdivideSelectedNode;
    public bool HasClipMask => session.SelectedLayer?.Mask is not null;
    public bool IsVectorMask => session.SelectedLayer?.Mask is VectorClipMask;
    public bool CanEditMaskTopology => IsVectorMask && !session.MaskEditing.IsTopologyLocked;
    public bool CanDeleteContour => session.MaskEditing.CanDeleteSelectedContour;
    public bool CanDeleteNode => session.MaskEditing.CanDeleteSelectedNode;
    public bool HasSelectedPoint => SelectedPoint is not null;
    public bool MaskInverted => session.SelectedLayer?.Mask?.Inverted == true;
    public LayerPositionDraft Position => session.MaskEditing.PositionDraft;
    public bool IsSnapEnabled
    {
        get => isSnapEnabled;
        set => SetProperty(ref isSnapEnabled, value);
    }
    public string ContourToolTip => MaskTopologyReason ?? Localization.Get("Workbench.MaskAddContour");
    public string DeleteContourToolTip => MaskTopologyReason ?? Localization.Get("Workbench.MaskDeleteContour");
    public string DeleteNodeToolTip => MaskTopologyReason ?? Localization.Get("Workbench.MaskDeleteNode");
    public string SubdivideToolTip => MaskTopologyReason ?? Localization.Get("Workbench.MaskSubdivide");
    public string? MaskTopologyReason => session.MaskEditing.IsTopologyLocked ? Localization.Get("Workbench.MaskTopologyLocked") : null;
    public MaskVectorField[] GeometryFields { get; private set; } = [];
    public MaskVectorField[] NodeFields { get; private set; } = [];
    public MaskSelectionChoice[] Contours { get; private set; } = [];
    public MaskPointListItem[] Points { get; private set; } = [];
    public string? ValidationError
    {
        get => validationError;
        internal set => SetProperty(ref validationError, value);
    }
    public string? InvalidFieldKey
    {
        get => invalidFieldKey;
        internal set => SetProperty(ref invalidFieldKey, value);
    }
    public MaskSelectionChoice? SelectedContour
    {
        get => Contours.FirstOrDefault(contour => contour.Id == session.SceneEditing.MaskContourId);
        set
        {
            if (!refreshingChoices && !session.IsUpdating && value is not null && value.Id != session.SceneEditing.MaskContourId &&
                session.SelectedLayer?.Mask is VectorClipMask vector)
            {
                if (!session.TryCommitDrafts())
                {
                    OnPropertyChanged(nameof(SelectedContour));
                    return;
                }
                var contour = vector.Contours.Single(contour => contour.Id == value.Id);
                session.SceneEditing.MaskContourId = contour.Id;
                session.MaskEditing.SelectNode(contour.Nodes[0].Id);
            }
        }
    }
    public MaskPointListItem? SelectedPoint
    {
        get => Points.FirstOrDefault(point => point.Id == session.SceneEditing.MaskNodeId);
        set
        {
            if (!refreshingChoices && !session.IsUpdating && value is not null && value.Id != session.SceneEditing.MaskNodeId)
            {
                session.MaskEditing.SelectNode(value.Id);
                OnPropertyChanged(nameof(SelectedPoint));
            }
        }
    }
    public IRelayCommand CreateRectangleMaskCommand { get; }
    public IRelayCommand CreateVectorMaskCommand { get; }
    public IRelayCommand ToggleMaskEditingCommand { get; }
    public ICommand AddMaskContourCommand { get; }
    public ICommand ClearMaskCommand { get; }
    public ICommand InvertMaskCommand { get; }
    public ICommand SubdivideMaskCommand { get; }
    public ICommand DeleteContourCommand { get; }
    public ICommand DeleteNodeCommand { get; }

    internal void Refresh()
    {
        refreshingChoices = true;
        try
        {
            var fields = session.MaskEditing.Fields;
            if (!ReferenceEquals(sourceFields, fields))
            {
                sourceFields = fields;
                var vectors = fields.Where(field => field.Target is { } target &&
                    AnimationPropertyMetadata.GetComponentCount(target.Property) == 2)
                    .GroupBy(field => field.Target).Select(group => new MaskVectorField(
                        group.Single(field => field.Component == 0), group.Single(field => field.Component == 1))).ToArray();
                GeometryFields = vectors.Where(field => field.Target?.Property is
                    AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT).ToArray();
                NodeFields = vectors.Where(field => field.Target is { } target && AnimationPropertyMetadata.IsNodeProperty(target.Property)).ToArray();
                OnPropertyChanged(nameof(GeometryFields));
                OnPropertyChanged(nameof(NodeFields));
            }
            foreach (var field in GeometryFields.Concat(NodeFields))
            {
                field.RefreshLabel();
            }
            var mask = session.SelectedLayer is { } layer ? SceneEvaluator.EvaluateMask(layer, session.AnimationTarget?.LocalTime ?? new(0)) : null;
            if (mask is VectorClipMask vector)
            {
                var contours = vector.Contours.Select((contour, index) => new MaskSelectionChoice(contour.Id, (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture))).ToArray();
                if (!Contours.SequenceEqual(contours))
                {
                    Contours = contours;
                    OnPropertyChanged(nameof(Contours));
                }
                var contour = vector.Contours.FirstOrDefault(contour => contour.Id == session.SceneEditing.MaskContourId);
                var nodes = contour?.Nodes ?? [];
                if (!Points.Select(point => point.Id).SequenceEqual(nodes.Select(node => node.Id)))
                {
                    var previous = Points.ToDictionary(point => point.Id);
                    Points = nodes.Select((node, index) => previous.GetValueOrDefault(node.Id) ?? new MaskPointListItem(node.Id, index + 1, node.Position)).ToArray();
                    OnPropertyChanged(nameof(Points));
                }
                for (var index = 0; index < nodes.Length; index++)
                {
                    Points[index].Update(index + 1, nodes[index].Position);
                }
            }
            else
            {
                if (Contours.Length > 0)
                {
                    Contours = [];
                    OnPropertyChanged(nameof(Contours));
                }
                if (Points.Length > 0)
                {
                    Points = [];
                    OnPropertyChanged(nameof(Points));
                }
            }
            foreach (var name in new[] { nameof(CanEditMask), nameof(CanCreateMask), nameof(CanEditExistingMask), nameof(IsMaskEditing), nameof(IsRectangleTool), nameof(IsVectorTool), nameof(CanSubdivide), nameof(HasClipMask), nameof(IsVectorMask), nameof(CanEditMaskTopology),
                nameof(CanDeleteContour), nameof(CanDeleteNode), nameof(HasSelectedPoint), nameof(MaskInverted), nameof(MaskTopologyReason),
                nameof(SelectedContour), nameof(SelectedPoint), nameof(ContourToolTip), nameof(DeleteContourToolTip),
                nameof(DeleteNodeToolTip), nameof(SubdivideToolTip) })
            {
                OnPropertyChanged(name);
            }
            CreateRectangleMaskCommand.NotifyCanExecuteChanged();
            CreateVectorMaskCommand.NotifyCanExecuteChanged();
            ToggleMaskEditingCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            refreshingChoices = false;
        }
    }

    internal void CommitDrafts() => session.TryCommitDrafts(false);

    internal void RestoreField(string? key)
    {
        if (session.MaskEditing.RestorePositionField(key))
        {
            return;
        }
        var field = session.MaskEditing.Fields.FirstOrDefault(field => field.Key == key);
        if (field is not null)
        {
            session.MaskEditing.Restore(field);
            if (InvalidFieldKey == key)
            {
                InvalidFieldKey = null;
                ValidationError = null;
            }
        }
    }

    private AsyncRelayCommand EditCommand(Action action) => new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(action)));
}
