using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed class CanvasDrawingCommandBridge(WorkbenchSession session)
{
    internal event EventHandler<CanvasDrawingCommandEventArgs>? Requested;
    internal event EventHandler? AvailabilityChanged;

    internal bool CanExecute(CanvasDrawingCommand command) => MatchesContext(command) && Dispatch(command, true);

    internal bool TryExecute(CanvasDrawingCommand command)
    {
        return CanExecute(command) && session.TryCommitDrafts() && MatchesContext(command) && Dispatch(command, false);
    }

    internal void RefreshAvailability() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    private bool MatchesContext(CanvasDrawingCommand command)
    {
        if (session.IsClosing || session.IsProjectBusy)
        {
            return false;
        }
        return command switch
        {
            CanvasDrawingCommand.FINISH_PATH => session.SceneEditing.Mode == CanvasEditMode.CREATE_PATH,
            CanvasDrawingCommand.STRAIGHT_SEGMENT or CanvasDrawingCommand.CURVE_SEGMENT or CanvasDrawingCommand.TOGGLE_CLOSED_PATH =>
                session.SceneEditing.Mode == CanvasEditMode.SHAPE_PATH && session.SelectedLayer?.Shape?.Kind == ShapeKind.PATH,
            _ => false
        };
    }

    private bool Dispatch(CanvasDrawingCommand command, bool query)
    {
        var request = new CanvasDrawingCommandEventArgs(command, session.DocumentSnapshot,
            session.SelectedLayerId, session.SceneEditing.Mode, query);
        Requested?.Invoke(this, request);
        return request.Handled;
    }
}
