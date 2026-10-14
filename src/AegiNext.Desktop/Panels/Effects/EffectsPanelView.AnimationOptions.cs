using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelView : IWorkbenchFocusCommandTarget
{
    private readonly AnimationOptionsPopupHost animationOptions;

    /// <inheritdoc />
    public bool OwnsFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.DELETE_SUBTITLE && !animationOptions.IsOpen &&
            TryGetAnimationActionTarget(focusedElement, out _, out _);
    }

    /// <inheritdoc />
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return OwnsFocusCommand(command, focusedElement) &&
            TryGetAnimationActionTarget(focusedElement, out var layerId, out var target) &&
            session.CanDeleteSelectedAnimationKeyframe(layerId, target);
    }

    /// <inheritdoc />
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement) ||
            !TryGetAnimationActionTarget(focusedElement, out var layerId, out var target))
        {
            return false;
        }
        _ = session.DeleteSelectedAnimationKeyframeAsync(layerId, target);
        return true;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        animationOptions.Close();
        CancelGestures();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAnimationOptionsRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not AnimationPropertyRowControl control)
        {
            return;
        }
        if (control.DataContext is AnimationPropertyRowViewModel row)
        {
            animationOptions.Open(control.FindControl<Button>("AnimationOptionsButton")!, row.LayerId, row.Target);
            e.Handled = true;
        }
        else if (control.CommandParameter is AnimationProperty property &&
            session.SelectedLayerId is { } layerId)
        {
            animationOptions.Open(control.FindControl<Button>("AnimationOptionsButton")!, layerId,
                viewModel.Target with { Property = property, NodeId = null });
            e.Handled = true;
        }
    }

    private bool TryGetAnimationActionTarget(IInputElement focusedElement, out Guid layerId, out AnimationTrackTarget target)
    {
        layerId = default;
        target = default;
        if (focusedElement is not Control source || source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault() is not { } button ||
            !button.Classes.Contains("animation-property-action"))
        {
            return false;
        }
        var row = button.GetVisualAncestors().OfType<AnimationPropertyRowControl>().FirstOrDefault();
        if (row?.DataContext is AnimationPropertyRowViewModel propertyRow)
        {
            layerId = propertyRow.LayerId;
            target = propertyRow.Target;
            return true;
        }
        if ((button.Tag ?? button.CommandParameter) is AnimationProperty property && session.SelectedLayerId is { } selectedLayerId)
        {
            layerId = selectedLayerId;
            target = RotationTargetCompatibility.Canonicalize(viewModel.Target with { Property = property, NodeId = null });
            return true;
        }
        return false;
    }
}
