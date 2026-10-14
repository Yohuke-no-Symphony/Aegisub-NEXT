using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnimationPropertyRowViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private bool loading;
    private string originalX = string.Empty;
    private string originalY = string.Empty;
    private string originalZ = string.Empty;
    private AnimationEditTarget? draftTarget;
    private ProjectDocument? draftSource;
    private bool isAnimated;
    private bool isEnabled;
    private bool isOrdered;
    private bool canUseAnimationActions = true;
    private bool isMixed;
    private bool canAddKeyframe;
    private string? error;
    private string? invalidFieldKey;
    private string activePanel = "effects";
    private string editingPanel = "effects";
    private string xEditingPanel = "effects";
    private string yEditingPanel = "effects";
    private string zEditingPanel = "effects";
    private string colorEditingPanel = "effects";

    internal AnimationPropertyRowViewModel(WorkbenchSession session, Guid layerId, AnimationTrackTarget target)
    {
        this.session = session;
        LayerId = layerId;
        Target = target;
        X.PropertyChanged += OnNumberChanged;
        Y.PropertyChanged += OnNumberChanged;
        Z.PropertyChanged += OnNumberChanged;
        Color.Changed += OnColorChanged;
        Color.Committed += (_, _) => session.TryCommitDrafts(false);
        SelectCommand = new RelayCommand(() => session.SelectAnimationProperty(LayerId, Target), () => canUseAnimationActions);
        ToggleAnimationCommand = new AsyncRelayCommand(() => session.SetAnimationPropertyEnabledAsync(LayerId, Target, !IsAnimated), () => CanToggleAnimation);
        AddKeyframeCommand = new AsyncRelayCommand(() => session.AddAnimationPropertyKeyframeAsync(LayerId, Target), () => CanAddKeyframe);
        AnimationOptionsCommand = new RelayCommand(() => session.SelectAnimationProperty(LayerId, Target), () => canUseAnimationActions);
        ResetCommand = new AsyncRelayCommand(() => session.ResetAnimationPropertyAsync(LayerId, Target), () => CanReset);
    }

    internal Guid LayerId { get; }
    internal AnimationTrackTarget Target { get; }
    internal bool HasDraft => X.RawText != originalX || IsVector && Y.RawText != originalY || HasZ && Z.RawText != originalZ || IsColor && Color.IsDirty;
    public string Name => AnimationPropertyLocalization.Get(Target.Property);
    public string FieldKey => $"EffectProperty_{LayerId:N}_{Target.NodeId:N}_{Target.TextRangeId:N}_{Target.State}_{Target.Property}";
    public string XFieldKey => FieldKey + "_X";
    public string YFieldKey => FieldKey + "_Y";
    public string ZFieldKey => FieldKey + "_Z";
    public string XInputName => HasZ ? "RotationXInput" : "RangeValueXInput";
    public string YInputName => HasZ ? "RotationYInput" : "RangeValueYInput";
    public string ZInputName => HasZ ? "RotationInput" : "RangeValueZInput";
    public NumericValueDraft X { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Y { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft Z { get; } = new() { PreserveDoublePrecision = true };
    public ColorDraft Color { get; } = new();
    public bool IsScalar => AnimationPropertyMetadata.GetValueKind(Target.Property) == AnimationValueKind.SCALAR;
    public bool IsVector => AnimationPropertyMetadata.GetValueKind(Target.Property) is AnimationValueKind.VECTOR or AnimationValueKind.VECTOR3;
    public bool HasZ => AnimationPropertyMetadata.GetValueKind(Target.Property) == AnimationValueKind.VECTOR3;
    public bool IsColor => AnimationPropertyMetadata.GetValueKind(Target.Property) == AnimationValueKind.COLOR;
    public decimal Minimum => (decimal)AnimationPropertyMetadata.GetMinimum(Target.Property);
    public decimal Maximum => (decimal)AnimationPropertyMetadata.GetMaximum(Target.Property);
    public decimal Increment => Target.Property switch
    {
        AnimationProperty.SCALE or AnimationProperty.MASK_SCALE => 0.01m,
        AnimationProperty.PATH_PROGRESS => 0.01m,
        AnimationProperty.LETTER_SPACING or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or
            AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_BLUR => 0.5m,
        _ => 1m
    };
    public bool IsAnimated { get => isAnimated; private set => SetProperty(ref isAnimated, value); }
    public bool IsEnabled { get => isEnabled; private set => SetProperty(ref isEnabled, value); }
    public bool CanAddKeyframe { get => canAddKeyframe; private set => SetProperty(ref canAddKeyframe, value); }
    public bool IsOrdered { get => isOrdered; private set => SetProperty(ref isOrdered, value); }
    public ICommand AnimationOptionsCommand { get; }
    public string AnimationOptionsHint => Localization.Format("Workbench.AnimationOptionsForProperty", Name);
    public bool CanReset => IsAnimated && IsEnabled;
    public bool CanToggleAnimation => canUseAnimationActions && (IsAnimated || CanAddKeyframe);
    public bool IsMixed { get => isMixed; private set => SetProperty(ref isMixed, value); }
    public string? EditingHint => !canUseAnimationActions ? Localization.Get("Workbench.PathProgressRequiresPath") :
        IsOrdered ? Localization.Get("Workbench.StyleOrderedTransformHint") :
        Target.Property == AnimationProperty.PATH_PROGRESS && !IsEnabled ? Localization.Get("Workbench.PathProgressRequiresKeyframe") :
        IsMixed ? Localization.Get("Workbench.MixedPropertyValue") :
        Target.Property == AnimationProperty.PERSPECTIVE_DISTANCE ? Localization.Get("Workbench.PerspectiveDistanceHint") : null;
    public string? Error { get => error; private set => SetProperty(ref error, value); }
    public string? InvalidFieldKey { get => invalidFieldKey; private set => SetProperty(ref invalidFieldKey, value); }
    public string AnimationHint => Localization.Get(IsAnimated ? "Workbench.DisablePropertyAnimation" : "Workbench.EnablePropertyAnimation");
    public string KeyframeHint { get; private set; } = Localization.Get("Workbench.Keyframe");
    public string ResetHint { get; private set; } = Localization.Get("Workbench.ResetProperty");
    public ICommand SelectCommand { get; }
    public ICommand ToggleAnimationCommand { get; }
    public ICommand AddKeyframeCommand { get; }
    public ICommand ResetCommand { get; }

    internal void Load(AnimationValue value, AnimationTrack? track, bool mixed)
    {
        IsMixed = mixed;
        OnPropertyChanged(nameof(EditingHint));
        IsAnimated = track is not null;
        IsOrdered = track?.IsOrdered == true;
        var layer = session.DocumentSnapshot.Layers.FirstOrDefault(value => value.Id == LayerId);
        canUseAnimationActions = layer is not null && WorkbenchSession.CanUseAnimationProperty(layer, Target);
        IsEnabled = canUseAnimationActions && !IsOrdered &&
            (Target.Property != AnimationProperty.PATH_PROGRESS || track?.Keyframes.IsEmpty == false);
        OnPropertyChanged(nameof(EditingHint));
        CanAddKeyframe = session.CanAddEffectPropertyKeyframe(LayerId, Target);
        OnPropertyChanged(nameof(CanToggleAnimation));
        OnPropertyChanged(nameof(CanReset));
        ((IRelayCommand)SelectCommand).NotifyCanExecuteChanged();
        ((IRelayCommand)ToggleAnimationCommand).NotifyCanExecuteChanged();
        ((IRelayCommand)AddKeyframeCommand).NotifyCanExecuteChanged();
        ((IRelayCommand)AnimationOptionsCommand).NotifyCanExecuteChanged();
        ((IRelayCommand)ResetCommand).NotifyCanExecuteChanged();
        RefreshLabels();
        if (HasDraft)
        {
            return;
        }
        loading = true;
        try
        {
            if (value.IsColor)
            {
                Color.Load(value.Color);
            }
            else
            {
                X.Load(value.GetComponent(0));
                Y.Load(value.IsVector || value.IsVector3 ? value.GetComponent(1) : 0);
                Z.Load(value.IsVector3 ? value.GetComponent(2) : 0);
            }
            originalX = X.RawText;
            originalY = Y.RawText;
            originalZ = Z.RawText;
            draftTarget = null;
            draftSource = null;
            Error = null;
            InvalidFieldKey = null;
        }
        finally
        {
            loading = false;
        }
    }

    internal void RefreshLabels()
    {
        OnPropertyChanged(nameof(AnimationOptionsHint));
        KeyframeHint = Localization.Get("Workbench.Keyframe");
        ResetHint = Localization.Get("Workbench.ResetProperty");
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(EditingHint));
        OnPropertyChanged(nameof(AnimationHint));
        OnPropertyChanged(nameof(KeyframeHint));
        OnPropertyChanged(nameof(ResetHint));
    }

    internal void BeginEdit(string panelId) => activePanel = panelId;

    internal double GetOriginalComponent(int component) =>
        double.Parse(component switch { 0 => originalX, 1 => originalY, _ => originalZ }, NumberStyles.Float, CultureInfo.CurrentCulture);

    internal bool OwnsField(string? field) => field == FieldKey || field == XFieldKey || field == YFieldKey || HasZ && field == ZFieldKey;

    internal ProjectDocument Prepare(ProjectDocument document)
    {
        if (!HasDraft)
        {
            return document;
        }
        session.ViewModel.InvalidPanelId = editingPanel;
        session.ViewModel.InvalidFieldKey = FieldKey;
        if (!ReferenceEquals(draftSource, session.DocumentSnapshot) || draftTarget is null)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        if (Target.Property == AnimationProperty.PATH_PROGRESS)
        {
            var layer = document.Layers.Single(value => value.Id == LayerId);
            var track = layer.Tracks.FirstOrDefault(value => value.Target == Target);
            var range = LayerAnimationTiming.GetRange(layer);
            var hint = !WorkbenchSession.CanUseAnimationProperty(layer, Target) ? "Workbench.PathProgressRequiresPath" :
                track?.IsOrdered == true ? "Workbench.StyleOrderedTransformHint" :
                track?.Keyframes.IsEmpty != false || draftTarget.LocalTime < range.Minimum || draftTarget.LocalTime > range.Maximum
                    ? "Workbench.PathProgressRequiresKeyframe" : null;
            if (hint is not null)
            {
                InvalidFieldKey = XFieldKey;
                session.ViewModel.InvalidFieldKey = XFieldKey;
                Error = Localization.Get(hint);
                throw new InvalidDataException(Error);
            }
        }
        AnimationValue value;
        if (IsColor)
        {
            session.ViewModel.InvalidPanelId = colorEditingPanel;
            if (!Color.TryCommit(out var color))
            {
                throw new InvalidDataException(Color.Error);
            }
            value = color;
        }
        else
        {
            var x = Read(X, XFieldKey, 0);
            value = HasZ ? AnimationValue.FromVector3(new(x, Read(Y, YFieldKey, 1), Read(Z, ZFieldKey, 2))) :
                IsVector ? new ScenePoint(x, Read(Y, YFieldKey, 1)) : x;
        }
        return AnimationEditOperations.SetValue(document, draftTarget, Target, value);
    }

    internal void Accept()
    {
        originalX = X.RawText;
        originalY = Y.RawText;
        originalZ = Z.RawText;
        if (IsColor)
        {
            Color.Load(Color.Value);
        }
        draftTarget = null;
        draftSource = null;
        Error = null;
        InvalidFieldKey = null;
    }

    internal void Restore(string field)
    {
        loading = true;
        try
        {
            if (field == XFieldKey)
            {
                X.RawText = originalX;
            }
            else if (field == YFieldKey)
            {
                Y.RawText = originalY;
            }
            else if (HasZ && field == ZFieldKey)
            {
                Z.RawText = originalZ;
            }
        }
        finally
        {
            loading = false;
        }
        if (!HasDraft)
        {
            draftSource = null;
            draftTarget = null;
        }
        Error = null;
        InvalidFieldKey = null;
        session.NotifyEffectPropertyDraftChanged();
        session.ClearEffectPropertyDraftError(field);
        session.RefreshMaskPreview();
    }

    private double Read(NumericValueDraft draft, string field, int component)
    {
        if (double.TryParse(draft.RawText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= AnimationPropertyMetadata.GetMinimum(Target.Property, component) &&
            value <= AnimationPropertyMetadata.GetMaximum(Target.Property, component))
        {
            return value;
        }
        session.ViewModel.InvalidPanelId = component switch { 0 => xEditingPanel, 1 => yEditingPanel, _ => zEditingPanel };
        session.ViewModel.InvalidFieldKey = field;
        InvalidFieldKey = field;
        Error = Localization.Get("Workbench.InvalidValue");
        throw new InvalidDataException(Error);
    }

    private void OnNumberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (loading || session.IsUpdating || e.PropertyName != nameof(NumericValueDraft.RawText))
        {
            return;
        }
        if (ReferenceEquals(sender, X))
        {
            xEditingPanel = activePanel;
        }
        else if (ReferenceEquals(sender, Y))
        {
            yEditingPanel = activePanel;
        }
        else
        {
            zEditingPanel = activePanel;
        }
        Changed();
    }

    private void OnColorChanged(object? sender, EventArgs e)
    {
        if (loading || session.IsUpdating)
        {
            return;
        }
        colorEditingPanel = activePanel;
        Changed();
    }

    private void Changed()
    {
        if (loading || session.IsUpdating)
        {
            return;
        }
        editingPanel = activePanel;
        draftSource ??= session.DocumentSnapshot;
        draftTarget ??= session.AnimationTarget is { } current
            ? current with { LayerId = LayerId, Target = Target } : null;
        session.SceneEditing.DraftTarget ??= draftTarget;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        session.NotifyEffectPropertyDraftChanged();
        session.RefreshMaskPreview();
    }
}
