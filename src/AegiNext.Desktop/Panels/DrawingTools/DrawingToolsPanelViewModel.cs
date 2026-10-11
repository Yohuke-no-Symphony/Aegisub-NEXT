using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.DrawingTools;

internal sealed class DrawingToolsPanelViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchSession session;
    private readonly RelayCommand[] pathCommands;
    private readonly AsyncRelayCommand createSubtitleBackgroundCommand;

    internal DrawingToolsPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        StraightSegmentCommand = CreatePathCommand(CanvasDrawingCommand.STRAIGHT_SEGMENT);
        CurveSegmentCommand = CreatePathCommand(CanvasDrawingCommand.CURVE_SEGMENT);
        ToggleClosedPathCommand = CreatePathCommand(CanvasDrawingCommand.TOGGLE_CLOSED_PATH);
        FinishPathCommand = CreatePathCommand(CanvasDrawingCommand.FINISH_PATH);
        pathCommands = [(RelayCommand)StraightSegmentCommand, (RelayCommand)CurveSegmentCommand,
            (RelayCommand)ToggleClosedPathCommand, (RelayCommand)FinishPathCommand];
        createSubtitleBackgroundCommand = new(() => CanCreateSubtitleBackground
            ? session.RunCommandAsync(session.Graphics.CreateSubtitleBackgroundAsync) : Task.CompletedTask, () => CanCreateSubtitleBackground);
        session.DrawingCommands.AvailabilityChanged += OnAvailabilityChanged;
    }

    public CanvasEditMode ActiveMode => session.SceneEditing.Mode;
    public ShapeKind? ActiveShapeKind => ActiveMode switch
    {
        CanvasEditMode.CREATE_PATH => ShapeKind.PATH,
        CanvasEditMode.CREATE_SHAPE => session.SceneEditing.CreationShapeKind,
        _ => null
    };
    public bool IsShapePathEditing => ActiveMode == CanvasEditMode.SHAPE_PATH;
    public bool IsShapePathCreation => ActiveMode == CanvasEditMode.CREATE_PATH;
    public bool CanCreateSubtitleBackground => !session.IsClosing && !session.IsProjectBusy && session.SelectedLayer?.SubtitleId is not null;
    public string ActiveToolLabel => Localization.Get(ActiveMode switch
    {
        CanvasEditMode.CREATE_PATH => "Preview.CreatePath",
        CanvasEditMode.CROP => "Preview.CropImage",
        CanvasEditMode.SHAPE_PATH => "Preview.EditShapePath",
        CanvasEditMode.PATH => "Workbench.Path",
        CanvasEditMode.MASK_RECTANGLE or CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR => "Workbench.MaskEdit",
        CanvasEditMode.CREATE_SHAPE => ActiveShapeKind switch
        {
            ShapeKind.ROUNDED_RECTANGLE => "Preview.CreateRoundedRectangle",
            ShapeKind.ELLIPSE => "Preview.CreateEllipse",
            ShapeKind.LINE => "Preview.CreateLine",
            ShapeKind.ARROW => "Preview.CreateArrow",
            _ => "Preview.CreateRectangle"
        },
        _ => "Preview.SelectGraphic"
    });
    public ICommand CreateSubtitleBackgroundCommand => createSubtitleBackgroundCommand;
    public ICommand StraightSegmentCommand { get; }
    public ICommand CurveSegmentCommand { get; }
    public ICommand ToggleClosedPathCommand { get; }
    public ICommand FinishPathCommand { get; }

    public bool CanSelectTool(CanvasEditMode mode) => session.CanSelectGraphicCanvasMode(mode);

    public bool SelectTool(CanvasEditMode mode, ShapeKind? shapeKind = null)
    {
        var changed = session.SetGraphicCanvasMode(mode, shapeKind);
        RefreshState();
        return changed;
    }

    internal void RefreshState()
    {
        OnPropertyChanged(nameof(ActiveMode));
        OnPropertyChanged(nameof(ActiveShapeKind));
        OnPropertyChanged(nameof(IsShapePathEditing));
        OnPropertyChanged(nameof(IsShapePathCreation));
        OnPropertyChanged(nameof(CanCreateSubtitleBackground));
        RefreshLanguage();
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ActiveToolLabel));
        RefreshCommands();
    }

    private RelayCommand CreatePathCommand(CanvasDrawingCommand command) => new(
        () => session.DrawingCommands.TryExecute(command), () => session.DrawingCommands.CanExecute(command));

    private void RefreshCommands()
    {
        foreach (var command in pathCommands)
        {
            command.NotifyCanExecuteChanged();
        }
        createSubtitleBackgroundCommand.NotifyCanExecuteChanged();
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e) => RefreshCommands();

    public void Dispose() => session.DrawingCommands.AvailabilityChanged -= OnAvailabilityChanged;
}
