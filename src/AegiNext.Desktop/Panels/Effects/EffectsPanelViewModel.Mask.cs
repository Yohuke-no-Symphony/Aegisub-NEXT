using System.Collections.Immutable;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    public NumericValueDraft PowerExponent { get; } = new() { PreserveDoublePrecision = true };
    public bool IsPowerInterpolation => Interpolation == (int)KeyframeInterpolation.POWER;
    private bool isOrderedTransform;
    public bool IsOrderedTransform
    {
        get => isOrderedTransform;
        internal set
        {
            if (SetProperty(ref isOrderedTransform, value))
            {
                OnPropertyChanged(nameof(CanEditKeyframes));
            }
        }
    }
    public bool CanEditKeyframes => !IsOrderedTransform;
    internal double ReadPowerExponent()
    {
        if (!double.TryParse(PowerExponent.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value) || value <= 0)
        {
            session.ViewModel.InvalidPanelId = "effects";
            session.ViewModel.InvalidFieldKey = "PowerExponentInput";
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        return value;
    }

    internal void LoadPowerExponent(double value) => LoadFiniteDraft(PowerExponent, value);

    private static void LoadFiniteDraft(NumericValueDraft draft, double value)
    {
        if (value > (double)decimal.MinValue && value < (double)decimal.MaxValue && (value == 0 || (decimal)value != 0))
        {
            draft.Load(value);
        }
        else
        {
            draft.RawText = value.ToString("G17", System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;
    internal void RefreshMaskState()
    {
        RefreshChoices(Blends, session.MaskPropertyChoices(), Interpolations);
        if (AnimationPropertyMetadata.IsSubtitleOnlyProperty(Property) && SelectedLayer?.Kind != LayerKind.SUBTITLE)
        {
            Target = new(AnimationProperty.OPACITY);
        }
    }

    public AnimationTransformChoice[] TransformOperations { get; private set; } = [];
    private AnimationTransformChoice? selectedOperation;
    public AnimationTransformChoice? SelectedOperation
    {
        get => selectedOperation;
        set
        {
            if (!loadingOperation && !session.IsUpdating && !session.TryCommitDrafts())
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref selectedOperation, value))
            {
                session.SceneEditing.TransformOperationId = value?.Id;
                LoadOperation();
            }
        }
    }
    public ColorDraft OperationColorDraft { get; } = new();
    public EffectComponentChoice[] OperationComponents => IsColorProperty
        ? [new(0, Localization.Get("Workbench.AnimationAllComponents")), new(7, Localization.Get("Workbench.AnimationRgb")),
            new(8, Localization.Get("Workbench.AnimationAlpha")), new(1, "R"), new(2, "G"), new(4, "B")]
        : HasVectorZ ? [new(0, Localization.Get("Workbench.AnimationAllComponents")), new(1, "X"), new(2, "Y"), new(4, "Z")]
        : IsVectorProperty ? [new(0, Localization.Get("Workbench.AnimationAllComponents")), new(1, "X"), new(2, "Y")]
        : [new(0, Localization.Get("Workbench.AnimationAllComponents"))];
    private int operationComponentMask;
    public EffectComponentChoice? SelectedOperationComponents
    {
        get => OperationComponents.FirstOrDefault(choice => choice.Mask == operationComponentMask);
        set
        {
            if (value is not null && SetProperty(ref operationComponentMask, value.Mask) && !loadingOperation && !session.IsUpdating)
            {
                MarkOperationChanged();
            }
        }
    }
    private bool operationIsRelative;
    public bool OperationIsRelative
    {
        get => operationIsRelative;
        set
        {
            if (SetProperty(ref operationIsRelative, value) && !loadingOperation && !session.IsUpdating)
            {
                MarkOperationChanged();
            }
        }
    }
    public bool CanUseRelativeOperation => Property == AnimationProperty.FONT_SIZE;
    private bool hasOperationCurve;
    private int operationCurveInterpolation;
    private bool operationCurveReverse;
    public bool HasOperationCurve => hasOperationCurve;
    public bool CanEditOperationExponent => !HasOperationCurve || OperationCurveInterpolation == (int)KeyframeInterpolation.POWER;
    public string? OperationExponentHint => CanEditOperationExponent ? null : Localization.Get("Workbench.OperationCurveExponentHint");
    public int OperationCurveInterpolation
    {
        get => operationCurveInterpolation;
        set
        {
            if (SetProperty(ref operationCurveInterpolation, value))
            {
                OnPropertyChanged(nameof(CanEditOperationExponent));
                OnPropertyChanged(nameof(OperationExponentHint));
                MarkOperationChanged();
            }
        }
    }
    public bool OperationCurveReverse
    {
        get => operationCurveReverse;
        set
        {
            if (SetProperty(ref operationCurveReverse, value))
            {
                MarkOperationChanged();
            }
        }
    }
    public string[] ColorSpaces { get; private set; } = [Localization.Get("Workbench.ColorLinearRgb"), Localization.Get("Workbench.ColorSrgb")];
    public int ColorSpace
    {
        get => (int)(SelectedLayer?.Tracks.FirstOrDefault(track => track.Target == Target)?.ColorSpace ?? AnimationColorSpace.LINEAR_RGB);
        set
        {
            if (!session.IsUpdating && value is >= 0 and <= 1 && value != ColorSpace)
            {
                _ = session.SetEffectColorSpaceAsync(Target, (AnimationColorSpace)value);
            }
        }
    }
    public NumericValueDraft OperationStart { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationEnd { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationValueX { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationValueY { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationValueZ { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationAcceleration { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationOrder { get; } = new();
    public ICommand DeleteOperationCommand => new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (SelectedLayer is { } layer && SelectedOperation is { } operation)
        {
            session.Editor.Apply("Delete animation transform", document =>
            {
                var normalized = AnimationEditOperations.NormalizeRotation(document, layer.Id, Target);
                return WorkspaceDraftOperations.UpdateLayer(normalized, layer.Id, current => current with
                {
                    Tracks = current.Tracks.Select(track => track.Target == Target ? track with
                    {
                        Transforms = track.Transforms.Where(value => value.Id != operation.Id).ToImmutableArray()
                    } : track).Where(track => !track.Keyframes.IsEmpty || !track.Transforms.IsEmpty).ToImmutableArray()
                });
            });
        }
    })));

    internal void RefreshTransformOperations(AnimationTrack? track)
    {
        TransformOperations = track?.Transforms.Select((operation, index) => new AnimationTransformChoice(operation.Id,
            $"{index + 1} · {Seconds(operation.Start):0.###}–{Seconds(operation.End):0.###}" )).ToArray() ?? [];
        selectedOperation = TransformOperations.FirstOrDefault(operation => operation.Id == session.SceneEditing.TransformOperationId)
            ?? TransformOperations.FirstOrDefault();
        session.SceneEditing.TransformOperationId = selectedOperation?.Id;
        OnPropertyChanged(nameof(TransformOperations));
        OnPropertyChanged(nameof(SelectedOperation));
        LoadOperation();
    }

    private void LoadOperation()
    {
        if (HasOperationDraft && operationLayer == SelectedLayer?.Id)
        {
            return;
        }
        var track = SelectedLayer is { } layer ? session.InspectorTrack(layer, Target) : null;
        var operation = track?.Transforms.FirstOrDefault(operation => operation.Id == SelectedOperation?.Id);
        if (operation is null)
        {
            return;
        }
        loadingOperation = true;
        try
        {
            OperationColorDraft.Load(operation.Value.IsColor ? operation.Value.Color : SceneColor.White);
            operationComponentMask = operation.ComponentMask;
            operationIsRelative = operation.Mode == AnimationTransformMode.MULTIPLY_BY;
            hasOperationCurve = operation.Curve is not null;
            operationCurveInterpolation = (int)(operation.Curve?.Interpolation ?? KeyframeInterpolation.POWER);
            operationCurveReverse = operation.Curve?.Reverse ?? false;
            OnPropertyChanged(nameof(HasOperationCurve));
            OnPropertyChanged(nameof(OperationCurveInterpolation));
            OnPropertyChanged(nameof(OperationCurveReverse));
            OnPropertyChanged(nameof(CanEditOperationExponent));
            OnPropertyChanged(nameof(OperationExponentHint));
            OnPropertyChanged(nameof(SelectedOperationComponents));
            OnPropertyChanged(nameof(OperationIsRelative));
            OperationStart.Load(Seconds(operation.Start));
            OperationEnd.Load(Seconds(operation.End));
            OperationValueX.Load(operation.Value.GetComponent(0));
            OperationValueY.Load(operation.Value.IsVector || operation.Value.IsVector3 ? operation.Value.GetComponent(1) : 0);
            OperationValueZ.Load(operation.Value.IsVector3 ? operation.Value.GetComponent(2) : 0);
            LoadFiniteDraft(OperationAcceleration, operation.Curve?.Exponent ?? operation.Acceleration);
            OperationOrder.Load(track!.Transforms.IndexOf(operation) + 1);
            operationOriginals.Clear();
            foreach (var field in OperationFields)
            {
                operationOriginals[field.Name] = field.Draft.RawText;
            }
        }
        finally
        {
            loadingOperation = false;
        }
    }

}
