using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private GraphicCanvasCoordinator? graphics;
    internal GraphicCanvasCoordinator Graphics => graphics ??= new(this);

    internal bool SetGraphicCanvasMode(CanvasEditMode mode)
    {
        if (IsProjectBusy || closing || !TryCommitDrafts() || mode switch
        {
            CanvasEditMode.CROP => SelectedLayer?.Image is null,
            CanvasEditMode.SHAPE_PATH => SelectedLayer?.Shape?.Kind != ShapeKind.PATH,
            CanvasEditMode.POSITION or CanvasEditMode.CREATE_SHAPE or CanvasEditMode.CREATE_PATH => false,
            _ => true
        })
        {
            return false;
        }
        ViewModel.CancelGestures();
        CancelSceneGesture();
        ViewModel.Effects.EditMode = mode;
        RefreshEditingPreview();
        return true;
    }
}
