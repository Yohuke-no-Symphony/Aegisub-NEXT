using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class SubtitleStyleView
{
    private NumericDragEditSnapshot? scalingGestureSnapshot;
    private Action? restoreScalingDraftIntent;
    private IPointer? scalingGesturePointer;
    private double scalingGestureOrigin;
    private Key? scalingGestureKey;
    private bool scalingWheelGesture;
    private Visual[] scalingVisibilitySources = [];
    private Slider? scalingSlider;

    private void BindScalingSlider()
    {
        var slider = this.FindControl<Slider>("MatchWidthOrHeightInput")!;
        scalingSlider = slider;
        slider.TemplateApplied += OnScalingSliderTemplateApplied;
        slider.PropertyChanged += OnScalingSliderPropertyChanged;
        slider.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (slider.IsEffectivelyEnabled && args.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            {
                BeginScalingGesture(args.Pointer);
            }
        }, RoutingStrategies.Tunnel, true);
        slider.AddHandler(PointerReleasedEvent, (_, _) => EndScalingGesture(true), RoutingStrategies.Tunnel, true);
        slider.AddHandler(PointerCaptureLostEvent, OnScalingPointerCaptureLost, RoutingStrategies.Direct, true);
        slider.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                if (scalingGestureSnapshot is not null)
                {
                    CancelScalingGesture();
                    args.Handled = true;
                }
                else if (viewModel.RestoreNumberField("MatchWidthOrHeightInput"))
                {
                    ++focusCommitRevision;
                    args.Handled = true;
                }
            }
            else if (IsScalingAdjustmentKey(args.Key) && scalingGestureSnapshot is null)
            {
                BeginScalingGesture();
                scalingGestureKey = args.Key;
            }
        }, RoutingStrategies.Tunnel, true);
        slider.AddHandler(KeyUpEvent, (_, args) =>
        {
            if (scalingGestureKey == args.Key)
            {
                EndScalingGesture(true);
            }
        }, RoutingStrategies.Bubble, true);
        slider.AddHandler(LostFocusEvent, (_, _) =>
        {
            if (scalingGestureKey is not null)
            {
                CancelScalingGesture();
            }
        }, RoutingStrategies.Bubble, true);
        slider.AddHandler(PointerWheelChangedEvent, (_, _) =>
        {
            scalingWheelGesture = scalingGestureSnapshot is null;
            if (scalingWheelGesture)
            {
                BeginScalingGesture();
            }
        }, RoutingStrategies.Tunnel, true);
        slider.AddHandler(PointerWheelChangedEvent, (_, _) =>
        {
            if (scalingWheelGesture)
            {
                scalingWheelGesture = false;
                EndScalingGesture(true);
            }
        }, RoutingStrategies.Bubble, true);
        AttachedToVisualTree += (_, _) => ObserveScalingVisibility();
        DetachedFromVisualTree += (_, _) =>
        {
            CancelGestures();
            ReleaseScalingVisibility();
        };
    }

    private void BeginScalingGesture(IPointer? pointer = null)
    {
        if (disposed || session.IsUpdating || !viewModel.IsActive || !viewModel.HasCue || scalingGestureSnapshot is not null)
        {
            return;
        }
        ++focusCommitRevision;
        suppressFocusCommit = true;
        scalingGestureSnapshot = new(session);
        restoreScalingDraftIntent = viewModel.CaptureDraftIntent();
        scalingGestureOrigin = viewModel.Scaling.MatchWidthOrHeight;
        scalingGesturePointer = pointer;
    }

    private void CancelScalingGesture() => EndScalingGesture(false);

    private void EndScalingGesture(bool commit)
    {
        var snapshot = scalingGestureSnapshot;
        if (snapshot is null)
        {
            return;
        }
        var pointer = scalingGesturePointer;
        var original = scalingGestureOrigin;
        var restoreIntent = restoreScalingDraftIntent;
        scalingGestureSnapshot = null;
        restoreScalingDraftIntent = null;
        scalingGesturePointer = null;
        scalingGestureKey = null;
        ++focusCommitRevision;
        suppressFocusCommit = !commit;
        var matches = snapshot.Matches(session);
        if (!commit)
        {
            if (matches)
            {
                session.RestoreStyleDraftIntent(() =>
                {
                    viewModel.Scaling.MatchWidthOrHeight = original;
                    restoreIntent?.Invoke();
                });
            }
            ReleaseScalingPointer(pointer);
            return;
        }
        if (matches && !original.Equals(viewModel.Scaling.MatchWidthOrHeight))
        {
            viewModel.CommitDrafts();
        }
        else if (matches && restoreIntent is not null)
        {
            session.RestoreStyleDraftIntent(restoreIntent);
        }
    }

    private static bool IsScalingAdjustmentKey(Key key) => key is
        Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;

    private void ObserveScalingVisibility()
    {
        if (disposed)
        {
            return;
        }
        ReleaseScalingVisibility();
        scalingVisibilitySources = this.GetSelfAndVisualAncestors().ToArray();
        foreach (var source in scalingVisibilitySources)
        {
            source.PropertyChanged += OnScalingAncestorVisibilityChanged;
        }
    }

    private void OnScalingAncestorVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty && args.NewValue is false)
        {
            CancelNumericDrags();
        }
    }

    private void ReleaseScalingVisibility()
    {
        foreach (var source in scalingVisibilitySources)
        {
            source.PropertyChanged -= OnScalingAncestorVisibilityChanged;
        }
        scalingVisibilitySources = [];
    }

    private void OnScalingSliderTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        CancelScalingGesture();
    }

    private void OnScalingSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TemplatedControl.TemplateProperty)
        {
            CancelScalingGesture();
        }
    }

    private void OnScalingPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (ReferenceEquals(args.Pointer, scalingGesturePointer))
        {
            CancelScalingGesture();
        }
    }

    private void ReleaseScalingPointer(IPointer? pointer)
    {
        if (pointer?.Captured is Visual captured && scalingSlider is { } slider &&
            captured.GetSelfAndVisualAncestors().Contains(slider))
        {
            pointer.Capture(null);
        }
    }

    private void ReleaseScalingBindings()
    {
        ReleaseScalingVisibility();
        if (scalingSlider is { } slider)
        {
            slider.TemplateApplied -= OnScalingSliderTemplateApplied;
            slider.PropertyChanged -= OnScalingSliderPropertyChanged;
            slider.RemoveHandler(PointerCaptureLostEvent, OnScalingPointerCaptureLost);
            scalingSlider = null;
        }
    }
}
