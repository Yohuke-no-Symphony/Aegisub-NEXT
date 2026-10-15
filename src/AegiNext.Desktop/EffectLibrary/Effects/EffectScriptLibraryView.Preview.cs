using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.EffectLibrary.Effects;

public sealed partial class EffectScriptLibraryView
{
    private readonly EffectScriptPreviewController preview;
    private readonly VideoFramePresenter previewPresenter;
    private readonly PreviewViewportControl previewViewport;
    private readonly Slider previewSlider;
    private readonly ComboBox previewStylePicker;
    private readonly Action<Action> postPreviewDrain;
    private readonly Lock previewMailboxGate = new();
    private EffectScriptPreviewUpdate? pendingPreviewUpdate;
    private TopLevel? previewHost;
    private bool previewAttached;
    private bool previewVisible;
    private bool previewVisibilityInitialized;
    private bool previewDrainQueued;
    private bool previewDisposed;
    private bool consumingPreviewSpace;
    private bool previewScrubbing;
    private Task? previewDisposal;
    private Guid? submittedPreviewDraftId;
    private long submittedPreviewRevision = -1;
    private double submittedPreviewScaling;
    private ImmutableArray<SubtitleStylePreset> displayedPreviewStyles;
    private bool synchronizingPreviewStyles;

    internal Task PreviewCompletion => preview.Completion;
    internal Task DisposalCompletion => previewDisposal ?? Task.CompletedTask;
    internal int PendingPreviewCount
    {
        get
        {
            lock (previewMailboxGate)
            {
                return pendingPreviewUpdate is null ? 0 : 1;
            }
        }
    }

    /// <inheritdoc />
    public bool OwnsLocalKey(Key key, KeyModifiers modifiers, IInputElement focusedElement)
    {
        return !previewDisposed && key == Key.Space && modifiers == KeyModifiers.None &&
            focusedElement is Visual focused && focused.GetSelfAndVisualAncestors().Contains(this);
    }

    private void InitializePreviewEvents()
    {
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        previewSlider.AddHandler(PointerPressedEvent, OnPreviewScrubPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        previewSlider.AddHandler(PointerReleasedEvent, OnPreviewScrubReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        previewSlider.PointerCaptureLost += OnPreviewScrubCaptureLost;
        previewViewport.AddHandler(PointerPressedEvent, OnPreviewViewportPressed, RoutingStrategies.Tunnel);
        previewStylePicker.SelectionChanged += OnPreviewStyleSelectionChanged;
    }

    private void ChangePreviewModel()
    {
        preview.Invalidate();
        ClearPreviewMailbox();
        submittedPreviewDraftId = null;
        submittedPreviewRevision = -1;
        previewPresenter.Clear();
        previewViewport.ResetView();
        SynchronizePreviewStyles();
        RefreshPreviewVisibility();
    }

    private void PreviewModelChanged(PropertyChangedEventArgs args)
    {
        if (previewDisposed || model is null || model.IsUpdatingPreviewPlayback)
        {
            return;
        }
        if (args.PropertyName is nameof(EffectScriptLibraryViewModel.PreviewStyles) or
            nameof(EffectScriptLibraryViewModel.SelectedPreviewStyle))
        {
            SynchronizePreviewStyles();
        }
        if (args.PropertyName == nameof(EffectScriptLibraryViewModel.PreviewRevision))
        {
            SubmitPreview();
        }
        else if (args.PropertyName == nameof(EffectScriptLibraryViewModel.IsPreviewPlaying))
        {
            if (model.IsPreviewPlaying)
            {
                preview.Play();
            }
            else
            {
                preview.Pause();
            }
        }
        else if (args.PropertyName == nameof(EffectScriptLibraryViewModel.PreviewLoop))
        {
            preview.Loop = model.PreviewLoop;
        }
        else if (args.PropertyName == nameof(EffectScriptLibraryViewModel.PreviewPosition))
        {
            var position = MediaTime.FromTimeSpan(TimeSpan.FromSeconds(model.PreviewPosition));
            if (!previewScrubbing)
            {
                preview.BeginScrub();
            }
            preview.Seek(position);
            if (!previewScrubbing)
            {
                preview.EndScrub();
            }
        }
    }

    private void SubmitPreview()
    {
        if (previewDisposed)
        {
            return;
        }
        if (model is null || !model.HasPreview || model.Draft is not { } draft ||
            !model.TryGetPreviewDuration(out var duration))
        {
            preview.Invalidate();
            ClearPreviewMailbox();
            previewPresenter.Clear();
            model?.UpdatePreviewPlayback(MediaTime.Zero, false, false);
            submittedPreviewDraftId = null;
            submittedPreviewRevision = -1;
            return;
        }

        if (!previewAttached || !IsEffectivelyVisible)
        {
            return;
        }
        var scaling = previewHost?.RenderScaling ?? 1;
        if (submittedPreviewDraftId == draft.Id && submittedPreviewRevision == model.PreviewRevision &&
            submittedPreviewScaling.Equals(scaling))
        {
            return;
        }
        var selected = submittedPreviewDraftId != draft.Id;
        submittedPreviewDraftId = draft.Id;
        submittedPreviewRevision = model.PreviewRevision;
        submittedPreviewScaling = scaling;
        if (selected)
        {
            previewViewport.ResetView();
            previewPresenter.Clear();
        }
        if (!model.TryCapturePreviewParameters(out var parameters))
        {
            model.SetPreviewError(I18n.Localization.Get("Workbench.EffectParameterInvalid"));
            return;
        }
        var input = new EffectScriptPreviewInput(model.Source, new()
        {
            Text = model.PreviewText, Duration = duration
        }, model.SelectedPreviewStyle) { Parameters = parameters };
        preview.Loop = model.PreviewLoop;
        if (!preview.Submit(new(draft.Id, model.PreviewRevision, input, scaling, model.PreviewFonts),
                autoPlay: model.IsPreviewPlaying, debounce: !selected))
        {
            var snapshot = preview.Snapshot;
            model.SetPreviewError(snapshot.Error?.Message);
            model.UpdatePreviewPlayback(snapshot.Position, snapshot.IsPlaying, snapshot.IsBusy);
        }
    }

    private void RefreshPreviewVisibility()
    {
        if (previewDisposed)
        {
            return;
        }
        var visible = previewAttached && IsEffectivelyVisible;
        if (previewVisibilityInitialized && previewVisible == visible)
        {
            if (visible)
            {
                SubmitPreview();
            }
            return;
        }
        previewVisibilityInitialized = true;
        previewVisible = visible;
        if (!visible && previewScrubbing)
        {
            previewScrubbing = false;
            preview.EndScrub();
        }
        preview.SetVisible(visible);
        if (!visible)
        {
            previewViewport.CancelInteraction();
            var snapshot = preview.Snapshot;
            if (snapshot.Request is { } request && model?.Draft?.Id == request.DraftId)
            {
                model.UpdatePreviewPlayback(snapshot.Position, false, false);
            }
            ClearPreviewMailbox();
            return;
        }
        SubmitPreview();
    }

    private void QueuePreviewUpdate(EffectScriptPreviewUpdate update)
    {
        if (!preview.IsCurrent(update))
        {
            return;
        }
        lock (previewMailboxGate)
        {
            if (previewDisposed || !preview.IsCurrent(update))
            {
                return;
            }
            pendingPreviewUpdate = update;
            if (previewDrainQueued)
            {
                return;
            }
            previewDrainQueued = true;
        }
        postPreviewDrain(DrainPreviewUpdate);
    }

    private void DrainPreviewUpdate()
    {
        EffectScriptPreviewUpdate? update;
        lock (previewMailboxGate)
        {
            update = pendingPreviewUpdate;
            pendingPreviewUpdate = null;
            previewDrainQueued = false;
        }
        if (previewDisposed || update is null || !previewAttached || !IsEffectivelyVisible ||
            !preview.IsCurrent(update) || model is null)
        {
            return;
        }
        var snapshot = update.Snapshot;
        if (snapshot.Request is not { } request || request.DraftId != model.Draft?.Id)
        {
            return;
        }
        model.SetPreviewError(snapshot.Error?.Message);
        model.UpdatePreviewPlayback(snapshot.Position, snapshot.IsPlaying, snapshot.IsBusy);
        if (update.ClearFrame || snapshot.Error is not null)
        {
            previewPresenter.Clear();
        }
        if (update.Frame is { } frame)
        {
            previewPresenter.Present(frame);
            preview.AcknowledgePresentation(update);
        }
    }

    private void ClearPreviewMailbox()
    {
        lock (previewMailboxGate)
        {
            pendingPreviewUpdate = null;
        }
    }

    private void OnPreviewLayoutUpdated(object? sender, EventArgs args)
    {
        SynchronizePreviewStyles();
        RefreshPreviewVisibility();
    }

    private void SynchronizePreviewStyles()
    {
        if (previewDisposed || synchronizingPreviewStyles)
        {
            return;
        }
        var styles = model?.PreviewStyles ?? [];
        var selected = model?.SelectedPreviewStyle;
        if (displayedPreviewStyles == styles && ReferenceEquals(previewStylePicker.SelectedItem, selected))
        {
            return;
        }
        synchronizingPreviewStyles = true;
        try
        {
            if (displayedPreviewStyles != styles)
            {
                displayedPreviewStyles = styles;
                previewStylePicker.ItemsSource = styles;
            }
            previewStylePicker.SelectedItem = styles.FirstOrDefault(preset => preset.Id == selected?.Id);
        }
        finally
        {
            synchronizingPreviewStyles = false;
        }
    }

    private void OnPreviewStyleSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!previewDisposed && !synchronizingPreviewStyles && model is not null)
        {
            model.SelectedPreviewStyle = previewStylePicker.SelectedItem as SubtitleStylePreset;
            SynchronizePreviewStyles();
        }
    }
    private void OnPreviewScalingChanged(object? sender, EventArgs args) => SubmitPreview();

    private void OnPreviewViewportPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(previewViewport).Properties.IsLeftButtonPressed)
        {
            previewViewport.Focus();
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || previewDisposed || args.Key != Key.Space || args.KeyModifiers != KeyModifiers.None ||
            !IsEffectivelyVisible)
        {
            return;
        }
        if (consumingPreviewSpace)
        {
            args.Handled = true;
            return;
        }
        var focused = previewHost?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !focused.GetSelfAndVisualAncestors().Contains(this) ||
            focused.GetSelfAndVisualAncestors().Any(visual => visual is TextBox or EffectScriptEditor))
        {
            return;
        }
        consumingPreviewSpace = true;
        args.Handled = true;
        if (model?.PreviewPlayPauseCommand.CanExecute(null) == true)
        {
            model.PreviewPlayPauseCommand.Execute(null);
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Space && consumingPreviewSpace)
        {
            consumingPreviewSpace = false;
            args.Handled = true;
        }
    }

    private void OnPreviewScrubPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!previewDisposed && model?.HasPreview == true &&
            args.GetCurrentPoint(previewSlider).Properties.IsLeftButtonPressed)
        {
            previewScrubbing = true;
            preview.BeginScrub();
        }
    }

    private void OnPreviewScrubReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton == MouseButton.Left)
        {
            EndPreviewScrub();
        }
    }

    private void OnPreviewScrubCaptureLost(object? sender, PointerCaptureLostEventArgs args) => EndPreviewScrub();

    private void EndPreviewScrub()
    {
        if (previewScrubbing)
        {
            previewScrubbing = false;
            if (!previewDisposed)
            {
                preview.EndScrub();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (previewDisposed)
        {
            return;
        }
        previewAttached = true;
        previewHost = TopLevel.GetTopLevel(this);
        if (previewHost is not null)
        {
            previewHost.ScalingChanged += OnPreviewScalingChanged;
            previewHost.AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
            if (previewHost is Window window)
            {
                window.Deactivated += OnPreviewHostDeactivated;
            }
        }
        LayoutUpdated += OnPreviewLayoutUpdated;
        RefreshPreviewVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        previewAttached = false;
        UnsubscribePreviewHost();
        RefreshPreviewVisibility();
        consumingPreviewSpace = false;
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (preview is not null && change.Property == IsVisibleProperty)
        {
            RefreshPreviewVisibility();
        }
    }

    private void UnsubscribePreviewHost()
    {
        LayoutUpdated -= OnPreviewLayoutUpdated;
        if (previewHost is not null)
        {
            previewHost.ScalingChanged -= OnPreviewScalingChanged;
            previewHost.RemoveHandler(KeyUpEvent, OnPreviewKeyUp);
            if (previewHost is Window window)
            {
                window.Deactivated -= OnPreviewHostDeactivated;
            }
            previewHost = null;
        }
    }

    private void OnPreviewHostDeactivated(object? sender, EventArgs args) => consumingPreviewSpace = false;

    internal Task DisposePreviewAsync()
    {
        if (previewDisposal is not null)
        {
            return previewDisposal;
        }
        lock (previewMailboxGate)
        {
            previewDisposed = true;
            pendingPreviewUpdate = null;
        }
        previewAttached = false;
        UnsubscribePreviewHost();
        DataContextChanged -= OnDataContextChanged;
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
            model = null;
        }
        RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        RemoveHandler(KeyUpEvent, OnPreviewKeyUp);
        previewSlider.RemoveHandler(PointerPressedEvent, OnPreviewScrubPressed);
        previewSlider.RemoveHandler(PointerReleasedEvent, OnPreviewScrubReleased);
        previewSlider.PointerCaptureLost -= OnPreviewScrubCaptureLost;
        previewViewport.RemoveHandler(PointerPressedEvent, OnPreviewViewportPressed);
        previewStylePicker.SelectionChanged -= OnPreviewStyleSelectionChanged;
        previewViewport.CancelInteraction();
        previewPresenter.Dispose();
        preview.Dispose();
        previewDisposal = CompletePreviewDisposalAsync();
        return previewDisposal;
    }

    private async Task CompletePreviewDisposalAsync()
    {
        await preview.DisposeAsync();
    }

    /// <inheritdoc />
    public void Dispose() => _ = DisposePreviewAsync();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(DisposePreviewAsync());
}
