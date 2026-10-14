using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel : ObservableObject
{
    private string rotationText = "0";
    private string keyframeValueText = "1";
    private string? validationError;
    private string? invalidFieldKey;
    private readonly WorkbenchSession session;
    private ProjectDocument document = new();
    private ProjectLayer? selectedLayer;
    private MediaTime position = MediaTime.Zero;
    private bool canEditPosition;
    private decimal? rotation = 0;
    private int blend;
    private string[] blends = [];
    private bool? orientPath = false;
    private AnimationPropertyChoice[] properties = [];
    private decimal? keyframeValue = 1;
    private decimal keyframeMinimum = -65504;
    private decimal keyframeMaximum = 65504;
    private int interpolation = 1;
    private string[] interpolations = [];
    private bool refreshingChoices;
    private bool canAddKeyframe;
    private bool canDeleteKeyframe;

    internal event EventHandler? ChoicesRefreshing;
    internal event EventHandler? ChoicesRefreshed;

    internal EffectsPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        InitializeNumericDrafts();
        PowerExponent.Load(1);
        InitializeOperationDrafts();
        InitializePropertyGrid();
        KeyframeColorDraft.Changed += (_, _) => OnPropertyChanged(nameof(KeyframeColorDraft));
        ResetPositionCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ResetPositionEffects)));
        AddPathPointCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.AddPathPoint)));
        RemovePathPointCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.RemovePathPoint)));
        PathCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.EditPath)));
        ClearPathCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ClearPath)));
        KeyframeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.AddKeyframe)));
        DeleteKeyframeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.DeleteKeyframe)));
        PowerExponent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NumericValueDraft.RawText))
            {
                OnPropertyChanged(nameof(PowerExponent));
            }
        };
    }

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

    public ProjectDocument Document
    {
        get => document;
        set => SetProperty(ref document, value);
    }

    public ProjectLayer? SelectedLayer
    {
        get => selectedLayer;
        set
        {
            if (SetProperty(ref selectedLayer, value))
            {
                OnPropertyChanged(nameof(CanResetPosition));
                OnPropertyChanged(nameof(CanEditOpacity));
                OnPropertyChanged(nameof(OpacityEditingHint));
                OnPropertyChanged(nameof(CanEditScale));
                OnPropertyChanged(nameof(ScaleEditingHint));
                OnPropertyChanged(nameof(CanEditRotation));
                OnPropertyChanged(nameof(RotationEditingHint));
            }
        }
    }

    public bool CanResetPosition => SelectedLayer?.SubtitleId is not null;
    public bool CanEditOpacity => SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = AnimationProperty.OPACITY, NodeId = null } && !track.Transforms.IsEmpty) != true;
    public string? OpacityEditingHint => CanEditOpacity ? null : Localization.Get("Workbench.OpacityOrderedTransformHint");
    public bool CanEditScale => SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = AnimationProperty.SCALE, NodeId = null } && !track.Transforms.IsEmpty) != true;
    public bool CanEditRotation => RotationRow?.IsEnabled ?? true;
    public bool CanEditBlur => SelectedLayer?.Tracks.Any(track =>
        track.Target == Target with { Property = AnimationProperty.BLUR, NodeId = null } && !track.Transforms.IsEmpty) != true;
    public string? BlurEditingHint => CanEditBlur ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public string? ScaleEditingHint => CanEditScale ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public string? RotationEditingHint => CanEditRotation ? null : Localization.Get("Workbench.StyleOrderedTransformHint");
    public static decimal ScaleMinimum => (decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.SCALE);
    public static decimal ScaleMaximum => (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.SCALE);
    public static decimal RotationMinimum => (decimal)AnimationPropertyMetadata.GetMinimum(AnimationProperty.ROTATION);
    public static decimal RotationMaximum => (decimal)AnimationPropertyMetadata.GetMaximum(AnimationProperty.ROTATION);

    public MediaTime Position
    {
        get => position;
        set => SetProperty(ref position, value);
    }

    public CanvasEditMode EditMode
    {
        get => session.SceneEditing.Mode;
        set
        {
            if (session.SceneEditing.Mode != value)
            {
                session.SceneEditing.Mode = value;
                OnPropertyChanged();
            }
        }
    }

    public Guid[] SelectedIds
    {
        get => session.SceneEditing.SelectedLayerIds;
        set
        {
            if (!session.SceneEditing.SelectedLayerIds.SequenceEqual(value))
            {
                session.SceneEditing.SelectedLayerIds = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? PositionX
    {
        get => PositionXDraft.Value;
        set => SetNumericDraftValue(PositionXDraft, value);
    }

    public bool CanEditPosition
    {
        get => canEditPosition && !HasOrderedPosition;
        set => SetProperty(ref canEditPosition, value);
    }

    public decimal? PositionY
    {
        get => PositionYDraft.Value;
        set => SetNumericDraftValue(PositionYDraft, value);
    }

    public decimal? ScaleX
    {
        get => ScaleXDraft.Value;
        set => SetNumericDraftValue(ScaleXDraft, value);
    }

    public decimal? ScaleY
    {
        get => ScaleYDraft.Value;
        set => SetNumericDraftValue(ScaleYDraft, value);
    }

    public decimal? Rotation
    {
        get => RotationRow?.Z.Value ?? rotation;
        set
        {
            if (RotationRow is { } row)
            {
                row.Z.RawText = value?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
            }
            else
            {
                SetProperty(ref rotation, value);
            }
        }
    }

    public decimal? Opacity
    {
        get => OpacityDraft.Value;
        set => SetNumericDraftValue(OpacityDraft, value);
    }

    public decimal? Blur
    {
        get => BlurDraft.Value;
        set => SetNumericDraftValue(BlurDraft, value);
    }

    public int Blend
    {
        get => blend;
        set
        {
            if (!refreshingChoices)
            {
                SetProperty(ref blend, value);
            }
        }
    }

    public string[] Blends
    {
        get => blends;
        set => SetProperty(ref blends, value);
    }

    public bool? OrientPath
    {
        get => orientPath;
        set => SetProperty(ref orientPath, value);
    }

    private string editTargetLabel = string.Empty;

    public string EditTargetLabel
    {
        get => editTargetLabel;
        set => SetProperty(ref editTargetLabel, value);
    }

    public AnimationProperty Property
    {
        get => Target.Property;
        set => Target = AnimationPropertyMetadata.IsMaskProperty(value)
            ? session.MaskEditing.ResolveAnimationTarget(value)
            : Target with { Property = value, NodeId = null };
    }

    public AnimationTrackTarget Target
    {
        get => session.SceneEditing.Target;
        set
        {
            value = RotationTargetCompatibility.Canonicalize(value);
            if (refreshingChoices || Target == value)
            {
                return;
            }
            if (!session.IsUpdating && !session.TryCommitDrafts())
            {
                OnPropertyChanged(nameof(SelectedProperty));
                OnPropertyChanged(nameof(SelectedScope));
                OnPropertyChanged(nameof(SelectedState));
                return;
            }
            var nodeChanged = value.NodeId is { } nodeId && session.SceneEditing.MaskNodeId != nodeId;
            if (nodeChanged)
            {
                session.SceneEditing.MaskNodeId = value.NodeId;
            }
            session.SceneEditing.Target = value;
            if (!session.IsUpdating)
            {
                session.ClearKeyframeSelection();
            }
            session.ViewModel.Timeline.EffectTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Property));
            OnPropertyChanged(nameof(SelectedProperty));
            OnPropertyChanged(nameof(IsVectorProperty));
            OnPropertyChanged(nameof(IsColorProperty));
            OnPropertyChanged(nameof(IsScalarProperty));
            RefreshKeyframeCurveChoices();
            OnPropertyChanged(nameof(OperationComponents));
            OnPropertyChanged(nameof(SelectedOperationComponents));
            OnPropertyChanged(nameof(CanUseRelativeOperation));
            OnPropertyChanged(nameof(ColorSpace));
            OnPropertyChanged(nameof(ColorSpaces));
            OnPropertyChanged(nameof(CanEditScale));
            OnPropertyChanged(nameof(CanEditRotation));
            OnPropertyChanged(nameof(CanEditOpacity));
            session.RefreshEffectsInspectorTarget();
            if (nodeChanged)
            {
                session.MaskEditing.Refresh();
                session.RefreshMaskPreview();
            }
        }
    }

    public AnimationPropertyChoice[] Properties
    {
        get => properties;
        set
        {
            if (properties.SequenceEqual(value))
            {
                return;
            }
            if (SetProperty(ref properties, value))
            {
                OnPropertyChanged(nameof(SelectedProperty));
            }
        }
    }

    public AnimationPropertyChoice? SelectedProperty
    {
        get => Properties.FirstOrDefault(choice => choice.Property == Property);
        set
        {
            if (value is not null)
            {
                Property = value.Property;
            }
        }
    }

    public ColorDraft KeyframeColorDraft { get; } = new();

    private decimal? keyframeValueY = 1;
    private string keyframeValueYText = "1";
    public decimal? KeyframeValueY { get => keyframeValueY; set => SetProperty(ref keyframeValueY, value); }
    public string KeyframeValueYText { get => keyframeValueYText; set => SetProperty(ref keyframeValueYText, value); }
    public bool IsVectorProperty => AnimationPropertyMetadata.GetValueKind(Property) is AnimationValueKind.VECTOR or AnimationValueKind.VECTOR3;
    public bool IsColorProperty => AnimationPropertyMetadata.GetValueKind(Property) == AnimationValueKind.COLOR;
    public bool IsScalarProperty => !IsVectorProperty && !IsColorProperty;

    public decimal? KeyframeValue
    {
        get => keyframeValue;
        set => SetProperty(ref keyframeValue, value);
    }

    public decimal KeyframeMinimum
    {
        get => keyframeMinimum;
        set => SetProperty(ref keyframeMinimum, value);
    }

    public decimal KeyframeMaximum
    {
        get => keyframeMaximum;
        set => SetProperty(ref keyframeMaximum, value);
    }

    public int Interpolation
    {
        get => interpolation;
        set
        {
            if (!refreshingChoices)
            {
                if (SetProperty(ref interpolation, value))
                {
                    OnPropertyChanged(nameof(IsPowerInterpolation));
                }
            }
        }
    }

    public string[] Interpolations
    {
        get => interpolations;
        set => SetProperty(ref interpolations, value);
    }

    public bool CanAddKeyframe
    {
        get => canAddKeyframe;
        set => SetProperty(ref canAddKeyframe, value);
    }

    public bool CanDeleteKeyframe
    {
        get => canDeleteKeyframe;
        set => SetProperty(ref canDeleteKeyframe, value);
    }

    public ICommand ResetPositionCommand { get; }
    public ICommand AddPathPointCommand { get; }
    public ICommand RemovePathPointCommand { get; }

    public ICommand PathCommand { get; }

    public ICommand ClearPathCommand { get; }

    public ICommand KeyframeCommand { get; }

    public ICommand DeleteKeyframeCommand { get; }

    /// <summary>同步图层及多选标识。</summary>
    public void SelectLayer(Guid id, Guid[] selectedIds) => session.SelectLayer(id, selectedIds);
    /// <summary>提交画布完成后的变换与路径参数。</summary>
    public Task CommitCanvasAsync(CanvasLayerEditEventArgs value) => session.CommitCanvasAsync(value);
    /// <summary>提交所有面板的有效草稿。</summary>
    public void CommitDrafts() => session.TryCommitDrafts(false);

    public void RestoreField(string fieldKey) => session.RestoreEffectDraftField(fieldKey);
    /// <summary>向工作台报告画布资源或工程渲染失败。</summary>
    public void ReportRenderingError(Exception error) => session.ShowError(error);
    public string PositionXText
    {
        get => PositionXDraft.RawText;
        set => PositionXDraft.RawText = value;
    }
    public string PositionYText
    {
        get => PositionYDraft.RawText;
        set => PositionYDraft.RawText = value;
    }
    public string ScaleXText
    {
        get => ScaleXDraft.RawText;
        set => ScaleXDraft.RawText = value;
    }
    public string ScaleYText
    {
        get => ScaleYDraft.RawText;
        set => ScaleYDraft.RawText = value;
    }
    public string RotationText
    {
        get => RotationRow?.Z.RawText ?? rotationText;
        set
        {
            if (RotationRow is { } row)
            {
                row.Z.RawText = value;
            }
            else
            {
                SetProperty(ref rotationText, value);
            }
        }
    }
    public string OpacityText
    {
        get => OpacityDraft.RawText;
        set => OpacityDraft.RawText = value;
    }
    public string BlurText
    {
        get => BlurDraft.RawText;
        set => BlurDraft.RawText = value;
    }
    public string KeyframeValueText
    {
        get => keyframeValueText;
        set => SetProperty(ref keyframeValueText, value);
    }
    /// <summary>提交用户选择的混合模式。</summary>
    public void CommitBlend(int value)
    {
        if (refreshingChoices)
        {
            return;
        }
        Blend = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的关键帧插值。</summary>
    public void CommitInterpolation(int value)
    {
        if (refreshingChoices)
        {
            return;
        }
        Interpolation = value;
        session.TryCommitDrafts();
    }

    internal void RefreshChoices(string[] blendOptions, AnimationPropertyChoice[] propertyOptions, string[] interpolationOptions)
    {
        RefreshKeyframeCurveChoices();
        OnPropertyChanged(nameof(OperationExponentHint));
        ColorSpaces = [Localization.Get("Workbench.ColorLinearRgb"), Localization.Get("Workbench.ColorSrgb")];
        OnPropertyChanged(nameof(ColorSpaces));
        RefreshPropertyGrid();
        OnPropertyChanged(nameof(OpacityEditingHint));
        OnPropertyChanged(nameof(ScaleEditingHint));
        OnPropertyChanged(nameof(RotationEditingHint));
        if (Blends.SequenceEqual(blendOptions) && Properties.SequenceEqual(propertyOptions) && Interpolations.SequenceEqual(interpolationOptions))
        {
            return;
        }
        var selection = (blend, interpolation, operationCurveInterpolation);
        refreshingChoices = true;
        try
        {
            ChoicesRefreshing?.Invoke(this, EventArgs.Empty);
            Blends = blendOptions;
            Properties = propertyOptions;
            Interpolations = interpolationOptions;
            blend = selection.blend;
            interpolation = selection.interpolation;
            operationCurveInterpolation = selection.operationCurveInterpolation;
            OnPropertyChanged(nameof(Blend));
            OnPropertyChanged(nameof(Interpolation));
            OnPropertyChanged(nameof(OperationCurveInterpolation));
            OnPropertyChanged(nameof(SelectedProperty));
        }
        finally
        {
            try
            {
                ChoicesRefreshed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                refreshingChoices = false;
            }
        }
    }
    /// <summary>提交用户选择的路径朝向状态。</summary>
    public void CommitOrientPath(bool value)
    {
        OrientPath = value;
        session.TryCommitDrafts();
    }
}
