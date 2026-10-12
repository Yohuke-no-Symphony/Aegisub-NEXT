using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Styles;

public sealed partial class StyleSettingsView
{
    private StyleSettingsViewModel? scalingGestureModel;
    private Guid? scalingGesturePresetId;
    private int scalingGestureDraftVersion;
    private double scalingGestureOrigin;
    private IPointer? scalingGesturePointer;
    private Key? scalingGestureKey;
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
                if (scalingGestureModel is not null)
                {
                    CancelScalingGesture();
                    args.Handled = true;
                }
                else if (model?.RestoreAppearanceField("MatchWidthOrHeightInput") == true)
                {
                    args.Handled = true;
                }
            }
            else if (IsScalingAdjustmentKey(args.Key) && scalingGestureModel is null)
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
        AttachedToVisualTree += (_, _) => ObserveScalingVisibility();
        DetachedFromVisualTree += (_, _) =>
        {
            ReleaseScalingVisibility();
        };
    }

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

    private void BeginScalingGesture(IPointer? pointer = null)
    {
        if (disposed || model?.CanEdit != true || scalingGestureModel is not null)
        {
            return;
        }
        scalingGestureModel = model;
        scalingGesturePresetId = model.Draft!.Id;
        scalingGestureDraftVersion = model.DraftVersion;
        scalingGestureOrigin = model.Scaling.MatchWidthOrHeight;
        scalingGesturePointer = pointer;
    }

    private void CancelScalingGesture() => EndScalingGesture(false);

    private void EndScalingGesture(bool commit)
    {
        var source = scalingGestureModel;
        if (source is null)
        {
            return;
        }
        var pointer = scalingGesturePointer;
        var presetId = scalingGesturePresetId;
        var version = scalingGestureDraftVersion;
        var original = scalingGestureOrigin;
        scalingGestureModel = null;
        scalingGesturePresetId = null;
        scalingGesturePointer = null;
        scalingGestureKey = null;
        if (!commit)
        {
            if (ReferenceEquals(source, model) && source.Draft?.Id == presetId && source.DraftVersion == version)
            {
                source.Scaling.MatchWidthOrHeight = original;
            }
            ReleaseScalingPointer(pointer);
        }
    }

    private static bool IsScalingAdjustmentKey(Key key) => key is
        Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;

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
