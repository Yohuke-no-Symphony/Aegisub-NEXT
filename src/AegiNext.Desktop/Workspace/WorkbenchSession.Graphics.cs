using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private GraphicCanvasCoordinator? graphics;
    internal GraphicCanvasCoordinator Graphics => graphics ??= new(this);
    private CanvasDrawingCommandBridge? drawingCommands;
    internal CanvasDrawingCommandBridge DrawingCommands => drawingCommands ??= new(this);

    internal bool CanSelectGraphicCanvasMode(CanvasEditMode mode) => !IsProjectBusy && !closing && mode switch
    {
        CanvasEditMode.CROP => SelectedLayer?.Image is not null,
        CanvasEditMode.SHAPE_PATH => SelectedLayer?.Shape?.Kind == ShapeKind.PATH,
        CanvasEditMode.POSITION or CanvasEditMode.CREATE_SHAPE or CanvasEditMode.CREATE_PATH => true,
        _ => false
    };

    internal bool SetGraphicCanvasMode(CanvasEditMode mode, ShapeKind? shapeKind = null)
    {
        if (!CanSelectGraphicCanvasMode(mode) || shapeKind is { } requested &&
            (mode == CanvasEditMode.CREATE_SHAPE ? requested is not (ShapeKind.RECTANGLE or ShapeKind.ROUNDED_RECTANGLE or
                ShapeKind.ELLIPSE or ShapeKind.LINE or ShapeKind.ARROW) : mode != CanvasEditMode.CREATE_PATH || requested != ShapeKind.PATH) ||
            !TryCommitDrafts() || !CanSelectGraphicCanvasMode(mode))
        {
            return false;
        }
        var kind = mode == CanvasEditMode.CREATE_SHAPE ? shapeKind ?? SceneEditing.CreationShapeKind : SceneEditing.CreationShapeKind;
        if (SceneEditing.Mode == mode && SceneEditing.CreationShapeKind == kind)
        {
            return true;
        }
        ViewModel.CancelGestures();
        CancelSceneGesture();
        using (BeginWorkbenchUpdate())
        {
            SceneEditing.CreationShapeKind = kind;
            ViewModel.Effects.EditMode = mode;
        }
        NotifyTaskInputChanged();
        RefreshEditingPreview();
        return true;
    }
}
