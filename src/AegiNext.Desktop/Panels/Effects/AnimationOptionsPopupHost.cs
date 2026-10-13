using System.ComponentModel;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed class AnimationOptionsPopupHost : IDisposable
{
    private readonly WorkbenchSession session;
    private readonly string panelId;
    private readonly Guid owner = Guid.NewGuid();
    private readonly Flyout flyout;
    private AnimationOptionsPopupView? view;
    private AnimationOptionsPopupViewModel? viewModel;
    private Button? anchor;
    private Guid layerId;
    private AnimationTrackTarget target;
    private bool disposed;

    internal AnimationOptionsPopupHost(WorkbenchSession session, string panelId)
    {
        this.session = session;
        this.panelId = panelId;
        flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedRight
        };
        flyout.Closed += OnClosed;
        session.AnimationOptionsChanged += OnOptionsChanged;
        session.SceneEditing.Changed += OnSelectionChanged;
        session.ViewModel.Effects.PropertyChanged += OnViewModelChanged;
    }

    internal bool IsOpen => flyout.IsOpen;

    internal bool Open(Button button, Guid selectedLayerId, AnimationTrackTarget selectedTarget, bool restoreDraft = false)
    {
        if (!restoreDraft && ReferenceEquals(anchor, button) && IsOpen && viewModel?.MatchesContext() == true)
        {
            return true;
        }
        if (IsOpen)
        {
            Close();
        }
        if (!session.TryOpenAnimationOptions(owner, selectedLayerId, selectedTarget, panelId, restoreDraft,
            restoreDraft ? viewModel?.CapturedOperationId : null))
        {
            return false;
        }
        DisposeView();
        viewModel = new(session, owner, session.AnimationOptionsTarget!);
        viewModel.ContextInvalidated += OnContextInvalidated;
        view = new(session, viewModel);
        flyout.Content = view;
        anchor = button;
        layerId = selectedLayerId;
        target = RotationTargetCompatibility.Canonicalize(selectedTarget);
        anchor.Flyout = flyout;
        var width = TopLevel.GetTopLevel(button)?.ClientSize.Width ?? 360;
        view.MaxWidth = Math.Max(260, Math.Min(380, width - 24));
        view.SetActive(true);
        flyout.ShowAt(anchor);
        return true;
    }

    internal bool FocusInvalidField(string? fieldKey)
    {
        if (!IsOptionsField(fieldKey) || anchor is null)
        {
            return false;
        }
        if (!IsOpen && !Open(anchor, layerId, target, true))
        {
            return false;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && IsOpen)
            {
                view?.FocusField(fieldKey);
            }
        }, DispatcherPriority.Loaded);
        return true;
    }

    internal static bool IsOptionsField(string? fieldKey) => fieldKey is "PowerExponentInput" or "InterpolationCombo" or
        "KeyframeCurveComponentCombo" or "ColorInterpolationSpaceCombo" || fieldKey?.StartsWith("Operation", StringComparison.Ordinal) == true;

    internal void Close()
    {
        view?.SetActive(false);
        flyout.Hide();
        session.CloseAnimationOptions(owner);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        view?.SetActive(false);
        session.CloseAnimationOptions(owner);
    }

    private void OnOptionsChanged(object? sender, EventArgs e)
    {
        if (!session.IsAnimationOptionsOwner(owner))
        {
            view?.SetActive(false);
            flyout.Hide();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (IsOpen && (session.SelectedLayerId != layerId || session.SceneEditing.Target != target ||
            session.AnimationOptionsTarget is { IsKeyframe: true } edit && session.SelectedKeyTime != edit.LocalTime ||
            viewModel is { IsChangingOperation: false } model && session.SceneEditing.TransformOperationId != model.CapturedOperationId))
        {
            Close();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsOpen && e.PropertyName == nameof(EffectsPanelViewModel.Document) &&
            session.AnimationOptionsTarget is { IsKeyframe: true } edit &&
            (session.SelectedLayer is not { } layer ||
            session.InspectorTrack(layer, target)?.Keyframes.Any(frame => frame.Time == edit.LocalTime) != true))
        {
            Close();
        }
    }

    private void OnContextInvalidated(object? sender, EventArgs e) => Close();

    private void DisposeView()
    {
        view?.Dispose();
        if (viewModel is not null)
        {
            viewModel.ContextInvalidated -= OnContextInvalidated;
            viewModel.Dispose();
        }
        view = null;
        viewModel = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Close();
        flyout.Closed -= OnClosed;
        session.AnimationOptionsChanged -= OnOptionsChanged;
        session.SceneEditing.Changed -= OnSelectionChanged;
        session.ViewModel.Effects.PropertyChanged -= OnViewModelChanged;
        DisposeView();
    }
}
