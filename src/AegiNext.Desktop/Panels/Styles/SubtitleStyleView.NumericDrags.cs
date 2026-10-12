using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class SubtitleStyleView
{
    private NumericDragEditSnapshot? numericDragSnapshot;

    private void BindNumericDrags()
    {
        AddHandler(NumericDragLabel.DragStartedEvent, (_, _) =>
        {
            ++focusCommitRevision;
            suppressFocusCommit = true;
            numericDragSnapshot = new(session);
        }, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragCompletedEvent, (_, args) =>
        {
            var snapshot = numericDragSnapshot;
            numericDragSnapshot = null;
            ++focusCommitRevision;
            suppressFocusCommit = false;
            if (args.Changed && snapshot?.Matches(session) == true)
            {
                viewModel.CommitDrafts();
            }
        }, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragCanceledEvent, (_, _) =>
        {
            numericDragSnapshot = null;
            ++focusCommitRevision;
            suppressFocusCommit = true;
        }, RoutingStrategies.Bubble);
        session.NumericGestureCancellationRequested += OnNumericGestureCancellation;
    }

    private void OnNumericGestureCancellation(object? sender, EventArgs args) => CancelNumericDrags();

    private void CancelNumericDrags()
    {
        CancelScalingGesture();
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
        numericDragSnapshot = null;
    }
}
