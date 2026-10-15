using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private EffectInstance? selectedEffectInstance;
    private EffectScriptParameterDraftSet? instanceParameters;
    private bool loadingInstance;
    private bool refreshingInstanceChoices;
    private string originalInstanceStart = string.Empty;
    private string originalInstanceEnd = string.Empty;
    private EffectInstance[] effectInstances = [];

    internal event EventHandler? InstanceChoicesRefreshing;
    internal event EventHandler? InstanceChoicesRefreshed;

    public IReadOnlyList<EffectInstance> EffectInstances => effectInstances;
    public bool HasEffectInstances => EffectInstances.Count > 0;
    public bool HasSelectedEffectInstance => selectedEffectInstance is not null;
    public EffectScriptParameterDraftSet? InstanceParameters => instanceParameters;
    public NumericValueDraft InstanceStart { get; } = new();
    public NumericValueDraft InstanceEnd { get; } = new();
    public bool IsInstanceEnabled => selectedEffectInstance?.IsEnabled == true;
    public decimal InstanceTimeMinimum => SelectedLayer is { } layer ? DecimalSeconds(LayerAnimationTiming.GetRange(layer).Minimum) : 0;
    public decimal InstanceTimeMaximum => SelectedLayer is { } layer ? DecimalSeconds(LayerAnimationTiming.GetRange(layer).Maximum) : 0;
    public bool CanMoveInstanceUp => SelectedLayer is { } layer && selectedEffectInstance is { } instance && layer.Effects.IndexOf(instance) > 0;
    public bool CanMoveInstanceDown => SelectedLayer is { } layer && selectedEffectInstance is { } instance &&
        layer.Effects.IndexOf(instance) is var index && index >= 0 && index < layer.Effects.Length - 1;
    public RelayCommand ReturnToParentCommand { get; private set; } = null!;
    public AsyncRelayCommand RemoveInstanceCommand { get; private set; } = null!;
    public AsyncRelayCommand MoveInstanceUpCommand { get; private set; } = null!;
    public AsyncRelayCommand MoveInstanceDownCommand { get; private set; } = null!;
    public RelayCommand ResetInstanceParametersCommand { get; private set; } = null!;

    public EffectInstance? SelectedEffectInstance
    {
        get => selectedEffectInstance;
        set
        {
            if (loadingInstance || refreshingInstanceChoices || session.IsUpdating || value?.Id == selectedEffectInstance?.Id || !session.TryCommitDrafts())
            {
                return;
            }
            if (value is not null && SelectedLayer is { } layer)
            {
                session.ViewModel.Timeline.SelectEffectClip(new(new(layer.Id, value.TargetContext ?? new(AnimationProperty.OPACITY), value.Id)));
            }
            else
            {
                SelectInstance(null);
                session.ViewModel.Timeline.ResetEffectSelection();
            }
        }
    }

    internal bool HasInstanceDrafts => HasSelectedEffectInstance && (instanceParameters?.IsDirty == true ||
        InstanceStart.RawText != originalInstanceStart || InstanceEnd.RawText != originalInstanceEnd);

    private void InitializeEffectInstances()
    {
        ReturnToParentCommand = new(() =>
        {
            if (session.TryCommitDrafts())
            {
                SelectInstance(null);
                session.ViewModel.Timeline.ResetEffectSelection();
            }
        });
        RemoveInstanceCommand = new(() => EditInstanceAsync((layerId, instanceId) => session.Editor.RemoveEffectInstance(layerId, instanceId)));
        MoveInstanceUpCommand = new(() => MoveInstanceAsync(-1));
        MoveInstanceDownCommand = new(() => MoveInstanceAsync(1));
        ResetInstanceParametersCommand = new(() =>
        {
            instanceParameters?.ResetDefaults();
            session.TryCommitDrafts();
        });
        InstanceStart.PropertyChanged += (_, args) =>
        {
            if (!loadingInstance && args.PropertyName == nameof(NumericValueDraft.RawText))
            {
                session.NotifyEffectPropertyDraftChanged();
            }
        };
        InstanceEnd.PropertyChanged += (_, args) =>
        {
            if (!loadingInstance && args.PropertyName == nameof(NumericValueDraft.RawText))
            {
                session.NotifyEffectPropertyDraftChanged();
            }
        };
    }

    internal void SelectInstance(Guid? instanceId)
    {
        var instance = SelectedLayer?.Effects.FirstOrDefault(value => value.Id == instanceId);
        loadingInstance = true;
        try
        {
            if (instanceParameters is not null)
            {
                instanceParameters.Changed -= OnInstanceParametersChanged;
            }
            selectedEffectInstance = instance;
            instanceParameters = instance is null ? null : new(instance.Source, instance.Parameters);
            if (instanceParameters is not null)
            {
                instanceParameters.Changed += OnInstanceParametersChanged;
            }
            if (instance is not null)
            {
                InstanceStart.Load((double)instance.Start.Numerator / instance.Start.Denominator);
                InstanceEnd.Load((double)instance.End.Numerator / instance.End.Denominator);
            }
            originalInstanceStart = InstanceStart.RawText;
            originalInstanceEnd = InstanceEnd.RawText;
        }
        finally
        {
            loadingInstance = false;
        }
        RefreshInstanceProperties();
    }

    private void RefreshEffectInstances()
    {
        if (selectedEffectInstance is { } selected &&
            !ReferenceEquals(SelectedLayer?.Effects.FirstOrDefault(value => value.Id == selected.Id), selected))
        {
            SelectInstance(selected.Id);
            return;
        }
        RefreshInstanceProperties();
    }

    private void RefreshInstanceProperties()
    {
        refreshingInstanceChoices = true;
        try
        {
            InstanceChoicesRefreshing?.Invoke(this, EventArgs.Empty);
            var next = SelectedLayer?.Effects.ToArray() ?? [];
            if (!effectInstances.SequenceEqual(next))
            {
                effectInstances = next;
                OnPropertyChanged(nameof(EffectInstances));
            }
            OnPropertyChanged(nameof(HasEffectInstances));
            OnPropertyChanged(nameof(SelectedEffectInstance));
            OnPropertyChanged(nameof(HasSelectedEffectInstance));
            OnPropertyChanged(nameof(InstanceParameters));
            OnPropertyChanged(nameof(IsInstanceEnabled));
            OnPropertyChanged(nameof(InstanceTimeMinimum));
            OnPropertyChanged(nameof(InstanceTimeMaximum));
            OnPropertyChanged(nameof(CanMoveInstanceUp));
            OnPropertyChanged(nameof(CanMoveInstanceDown));
        }
        finally
        {
            try
            {
                InstanceChoicesRefreshed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                refreshingInstanceChoices = false;
            }
        }
    }

    private void OnInstanceParametersChanged(object? sender, EventArgs args)
    {
        if (!loadingInstance)
        {
            session.NotifyEffectPropertyDraftChanged();
        }
    }

    internal ProjectDocument PrepareInstanceDraft(ProjectDocument project)
    {
        if (!HasInstanceDrafts || SelectedLayer is not { } layer || selectedEffectInstance is not { } instance)
        {
            return project;
        }
        session.ViewModel.InvalidPanelId = "effects";
        var current = project.Layers.Single(value => value.Id == layer.Id).Effects.Single(value => value.Id == instance.Id);
        var parameters = current.Parameters;
        if (instanceParameters is not null && !instanceParameters.TryCapture(out parameters))
        {
            session.ViewModel.InvalidFieldKey = instanceParameters.InvalidFieldKey;
            throw new InvalidDataException(Localization.Get("Workbench.EffectParameterInvalid"));
        }
        var startTime = current.Start;
        var endTime = current.End;
        if (InstanceStart.RawText != originalInstanceStart || InstanceEnd.RawText != originalInstanceEnd)
        {
            session.ViewModel.InvalidFieldKey = InstanceStart.Parse() is null ? "InstanceStartInput" : "InstanceEndInput";
            if (InstanceStart.Parse() is not { } start || InstanceEnd.Parse() is not { } end)
            {
                throw new InvalidDataException(Localization.Get("Workbench.EffectInstanceTimingInvalid"));
            }
            startTime = InstanceStart.RawText == originalInstanceStart ? startTime :
                new(decimal.ToInt64(decimal.Round(start * TimeSpan.TicksPerSecond)), TimeSpan.TicksPerSecond);
            endTime = InstanceEnd.RawText == originalInstanceEnd ? endTime :
                new(decimal.ToInt64(decimal.Round(end * TimeSpan.TicksPerSecond)), TimeSpan.TicksPerSecond);
        }
        return ProjectEditingOperations.UpdateEffectInstances(project, [new(layer.Id, instance.Id)], value => value with
        {
            Parameters = parameters, Start = startTime, End = endTime
        });
    }

    internal void AcceptInstanceDraft(ProjectDocument project)
    {
        if (selectedEffectInstance is { } instance && SelectedLayer is { } layer &&
            project.Layers.FirstOrDefault(value => value.Id == layer.Id)?.Effects.FirstOrDefault(value => value.Id == instance.Id) is { } accepted)
        {
            loadingInstance = true;
            try
            {
                instanceParameters?.Accept(accepted.Parameters);
                originalInstanceStart = InstanceStart.RawText;
                originalInstanceEnd = InstanceEnd.RawText;
            }
            finally
            {
                loadingInstance = false;
            }
        }
    }

    internal Task CommitInstanceEnabledAsync(bool enabled) => session.IsUpdating || loadingInstance || refreshingInstanceChoices
        ? Task.CompletedTask : EditInstanceAsync((layerId, instanceId) => session.Editor.SetEffectInstanceEnabled(layerId, instanceId, enabled));

    private Task MoveInstanceAsync(int direction) => EditInstanceAsync((layerId, instanceId) =>
    {
        var layer = session.Editor.Snapshot.Layers.Single(value => value.Id == layerId);
        var index = Array.FindIndex(layer.Effects.ToArray(), value => value.Id == instanceId);
        if (index >= 0 && index + direction >= 0 && index + direction < layer.Effects.Length)
        {
            session.Editor.MoveEffectInstance(layerId, instanceId, index + direction);
        }
    });

    private Task EditInstanceAsync(Action<Guid, Guid> edit)
    {
        if (SelectedLayer is not { } layer || selectedEffectInstance is not { } instance)
        {
            return Task.CompletedTask;
        }
        return session.RunCommandAsync(() => session.EditAsync(() =>
        {
            if (session.Editor.Snapshot.Layers.FirstOrDefault(value => value.Id == layer.Id)?.Effects.Any(value => value.Id == instance.Id) == true)
            {
                edit(layer.Id, instance.Id);
            }
        }));
    }

    private static decimal DecimalSeconds(AegiNext.Core.Timing.MediaTime value) => (decimal)value.Numerator / value.Denominator;
}
