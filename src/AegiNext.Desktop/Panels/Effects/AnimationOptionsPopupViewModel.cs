using System.ComponentModel;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed class AnimationOptionsPopupViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchSession session;
    private readonly EffectsPanelViewModel drafts;
    private readonly Guid owner;
    private ProjectDocument sourceSnapshot;
    private bool active;
    private bool disposed;
    private int mutationDepth;

    internal AnimationOptionsPopupViewModel(WorkbenchSession session, Guid owner, AnimationEditTarget target)
    {
        this.session = session;
        this.owner = owner;
        drafts = session.ViewModel.Effects;
        CapturedTarget = target;
        CapturedOperationId = session.SceneEditing.TransformOperationId;
        sourceSnapshot = session.DocumentSnapshot;
        DeleteOperationCommand = new AsyncRelayCommand(DeleteOperationAsync);
        drafts.PropertyChanged += OnDraftPropertyChanged;
        drafts.ChoicesRefreshing += OnChoicesRefreshing;
        drafts.ChoicesRefreshed += OnChoicesRefreshed;
    }

    /// <summary>弹窗打开时冻结的完整属性目标与局部时间。</summary>
    public AnimationEditTarget CapturedTarget { get; }

    /// <summary>当前弹窗接受的工程快照，仅随本弹窗提交更新。</summary>
    public ProjectDocument SourceSnapshot => sourceSnapshot;

    /// <summary>当前显式绑定的有序操作身份，普通关键帧为 null。</summary>
    public Guid? CapturedOperationId { get; private set; }

    internal bool IsChangingOperation { get; private set; }

    internal event EventHandler? ContextInvalidated;
    internal event EventHandler? ChoicesRefreshing;
    internal event EventHandler? ChoicesRefreshed;

    public string AnimationOptionsLabel => drafts.AnimationOptionsLabel;
    public string EditTargetLabel => drafts.EditTargetLabel;
    public string? ValidationError => drafts.ValidationError;
    public string? InvalidFieldKey => drafts.InvalidFieldKey;
    public bool NeedsAnimationKeyframe => drafts.NeedsAnimationKeyframe;
    public bool IsOrderedTransform => drafts.IsOrderedTransform;
    public bool CanEditKeyframes => drafts.CanEditKeyframes;
    public bool HasKeyframeCurveComponents => drafts.HasKeyframeCurveComponents;
    public bool IsPowerInterpolation => drafts.IsPowerInterpolation;
    public string[] Interpolations => drafts.Interpolations;
    public EffectComponentChoice[] KeyframeCurveComponents => drafts.KeyframeCurveComponents;
    public NumericValueDraft PowerExponent => drafts.PowerExponent;
    public AnimationTransformChoice[] TransformOperations => drafts.TransformOperations;
    public NumericValueDraft OperationStart => drafts.OperationStart;
    public NumericValueDraft OperationEnd => drafts.OperationEnd;
    public bool HasVectorZ => drafts.HasVectorZ;
    public bool IsVectorProperty => drafts.IsVectorProperty;
    public bool IsColorProperty => drafts.IsColorProperty;
    public bool IsScalarProperty => drafts.IsScalarProperty;
    public NumericValueDraft OperationValueX => drafts.OperationValueX;
    public NumericValueDraft OperationValueY => drafts.OperationValueY;
    public NumericValueDraft OperationValueZ => drafts.OperationValueZ;
    public decimal KeyframeMinimum => drafts.KeyframeMinimum;
    public decimal KeyframeMaximum => drafts.KeyframeMaximum;
    public ColorDraft OperationColorDraft => drafts.OperationColorDraft;
    public EffectComponentChoice[] OperationComponents => drafts.OperationComponents;
    public bool CanUseRelativeOperation => drafts.CanUseRelativeOperation;
    public bool HasOperationCurve => drafts.HasOperationCurve;
    public bool CanEditOperationExponent => drafts.CanEditOperationExponent;
    public string? OperationExponentHint => drafts.OperationExponentHint;
    public NumericValueDraft OperationAcceleration => drafts.OperationAcceleration;
    public NumericValueDraft OperationOrder => drafts.OperationOrder;
    public string[] ColorSpaces => drafts.ColorSpaces;
    public bool CanEditColorSpace => drafts.CanEditColorSpace;
    public ICommand DeleteOperationCommand { get; }

    public int Interpolation
    {
        get => drafts.Interpolation;
        set
        {
            if (CanEditKeyframes)
            {
                Mutate(() => drafts.Interpolation = value);
            }
        }
    }

    public EffectComponentChoice? SelectedKeyframeCurveComponent
    {
        get => drafts.SelectedKeyframeCurveComponent;
        set => Mutate(() => drafts.SelectedKeyframeCurveComponent = value);
    }

    public AnimationTransformChoice? SelectedOperation
    {
        get => drafts.SelectedOperation;
        set
        {
            if (IsChangingOperation || value?.Id == CapturedOperationId)
            {
                return;
            }
            Mutate(() =>
            {
                IsChangingOperation = true;
                try
                {
                    drafts.SelectedOperation = value;
                    CapturedOperationId = drafts.SelectedOperation?.Id;
                    OnPropertyChanged(nameof(CapturedOperationId));
                }
                finally
                {
                    IsChangingOperation = false;
                }
            });
        }
    }

    public EffectComponentChoice? SelectedOperationComponents
    {
        get => drafts.SelectedOperationComponents;
        set => Mutate(() => drafts.SelectedOperationComponents = value);
    }

    public bool OperationIsRelative
    {
        get => drafts.OperationIsRelative;
        set => Mutate(() => drafts.OperationIsRelative = value);
    }

    public int OperationCurveInterpolation
    {
        get => drafts.OperationCurveInterpolation;
        set => Mutate(() => drafts.OperationCurveInterpolation = value);
    }

    public bool OperationCurveReverse
    {
        get => drafts.OperationCurveReverse;
        set => Mutate(() => drafts.OperationCurveReverse = value);
    }

    public int ColorSpace
    {
        get => drafts.ColorSpace;
        set
        {
            if (CanEditColorSpace)
            {
                Mutate(() => drafts.ColorSpace = value);
            }
        }
    }

    internal void SetActive(bool value) => active = value;

    internal bool MatchesContext()
    {
        return active && !disposed && session.IsAnimationOptionsOwner(owner) &&
            session.AnimationOptionsTarget == CapturedTarget && session.SelectedLayerId == CapturedTarget.LayerId &&
            session.SceneEditing.Target == CapturedTarget.Target && session.SceneEditing.TransformOperationId == CapturedOperationId &&
            ReferenceEquals(sourceSnapshot, session.DocumentSnapshot);
    }

    internal bool TryCommitDrafts(bool focusInvalid = true)
    {
        if (!MatchesContext())
        {
            return false;
        }
        mutationDepth++;
        try
        {
            return session.TryCommitDrafts(focusInvalid);
        }
        finally
        {
            mutationDepth--;
        }
    }

    internal void CommitInterpolation(int value)
    {
        if (CanEditKeyframes)
        {
            Mutate(() => drafts.CommitInterpolation(value));
        }
    }

    internal void RestoreField(string fieldKey) => Mutate(() => drafts.RestoreField(fieldKey));

    internal bool RestoreOperationField(string? fieldKey)
    {
        var restored = false;
        Mutate(() => restored = drafts.RestoreOperationField(fieldKey));
        return restored;
    }

    private void Mutate(Action action)
    {
        if (!MatchesContext() || session.IsUpdating)
        {
            return;
        }
        mutationDepth++;
        try
        {
            action();
        }
        finally
        {
            mutationDepth--;
        }
    }

    private Task DeleteOperationAsync()
    {
        return ExecuteScopedAsync(async () =>
        {
            if (!TryCommitDrafts() || CapturedOperationId is not { } operationId)
            {
                return;
            }
            var target = CapturedTarget;
            var document = SourceSnapshot;
            await session.DeleteAnimationTransformAsync(target, operationId, document);
        });
    }

    private async Task ExecuteScopedAsync(Func<Task> action)
    {
        if (!MatchesContext())
        {
            return;
        }
        mutationDepth++;
        try
        {
            await action();
        }
        finally
        {
            mutationDepth--;
        }
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed)
        {
            return;
        }
        if (e.PropertyName == nameof(EffectsPanelViewModel.Document) && !ReferenceEquals(sourceSnapshot, session.DocumentSnapshot))
        {
            if (mutationDepth == 0)
            {
                ContextInvalidated?.Invoke(this, EventArgs.Empty);
                return;
            }
            sourceSnapshot = session.DocumentSnapshot;
            OnPropertyChanged(nameof(SourceSnapshot));
        }
        if (active && session.AnimationOptionsTarget == CapturedTarget && session.SceneEditing.Target == CapturedTarget.Target)
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    private void OnChoicesRefreshing(object? sender, EventArgs e) => ChoicesRefreshing?.Invoke(this, e);
    private void OnChoicesRefreshed(object? sender, EventArgs e) => ChoicesRefreshed?.Invoke(this, e);

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        active = false;
        drafts.PropertyChanged -= OnDraftPropertyChanged;
        drafts.ChoicesRefreshing -= OnChoicesRefreshing;
        drafts.ChoicesRefreshed -= OnChoicesRefreshed;
    }
}
