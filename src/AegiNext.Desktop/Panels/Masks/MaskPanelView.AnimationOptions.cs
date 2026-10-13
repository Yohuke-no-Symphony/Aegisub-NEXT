using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed partial class MaskPanelView
{
    /// <inheritdoc />
    public bool OwnsFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.DELETE_SUBTITLE
            ? !animationOptions.IsOpen && TryGetAnimationActionRow(focusedElement) is not null
            : CanExecuteFocusCommand(command, focusedElement);
    }

    private void OnAnimationOptionsRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is AnimationPropertyRowControl control && GetSharedRow(control) is { } row)
        {
            animationOptions.Open(control.FindControl<Button>("AnimationOptionsButton")!, row.LayerId, row.Target);
            e.Handled = true;
        }
    }

    private static AnimationPropertyRowViewModel? TryGetAnimationActionRow(IInputElement focusedElement)
    {
        if (focusedElement is not Control source || source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault() is not { } button ||
            !button.Classes.Contains("animation-property-action"))
        {
            return null;
        }
        var control = button.GetVisualAncestors().OfType<AnimationPropertyRowControl>().FirstOrDefault();
        return control is null ? null : GetSharedRow(control);
    }
}
