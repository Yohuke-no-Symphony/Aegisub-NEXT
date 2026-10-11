using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class DrawingToolsPanelViewModelTests
{
    [Fact]
    public async Task InvalidDraftBlocksBothToolModeAndCreationKindWithoutWritingHistory()
    {
        var line = new SubtitleLine { Text = "Caption" };
        var document = new ProjectDocument { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_SHAPE, ShapeKind.RECTANGLE));
        var style = context.Session.ViewModel.Styles.Subtitle;
        style.FontSize = null;
        style.FontSizeText = "invalid";

        Assert.False(tools.SelectTool(CanvasEditMode.CREATE_SHAPE, ShapeKind.ELLIPSE));

        Assert.Equal(CanvasEditMode.CREATE_SHAPE, tools.ActiveMode);
        Assert.Equal(ShapeKind.RECTANGLE, tools.ActiveShapeKind);
        Assert.Equal(ShapeKind.RECTANGLE, context.Session.SceneEditing.CreationShapeKind);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task KindOnlySwitchRefreshesTheSceneAndRepeatedSelectionKeepsCreationAlive()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_SHAPE, ShapeKind.RECTANGLE));
        var scene = context.Session.ViewModel.Preview.Scene;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_SHAPE, ShapeKind.ELLIPSE));
        Assert.NotEqual(scene, context.Session.ViewModel.Preview.Scene);
        Assert.Equal(ShapeKind.ELLIPSE, context.Session.ViewModel.Preview.Scene.CreationShapeKind);
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_PATH, ShapeKind.PATH));
        Assert.True(context.Session.Graphics.BeginCreation());

        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_PATH, ShapeKind.PATH));
        context.Session.Graphics.CreateShape(new(ShapeKind.PATH, new(), 10, 10,
            new(new(), [new(new(1, 1), new(2, 2), new(10, 10))])));

        Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(CanvasEditMode.SHAPE_PATH, tools.ActiveMode);
    }

    [Fact]
    public async Task ExistingStyleEntryAndToolboxShareModeAndSelectionAvailability()
    {
        var path = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.PATH, 20, 20,
            new(new(), [new(new(3, 3), new(7, 7), new(10, 10))])) };
        await using var context = new WorkspaceSessionTestContext(new() { Layers = [path] });
        await context.InitializeAsync();
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.False(tools.CanSelectTool(CanvasEditMode.CROP));
        context.Session.SelectLayer(path.Id, [path.Id]);

        Assert.True(context.Session.SetGraphicCanvasMode(CanvasEditMode.SHAPE_PATH));

        Assert.Equal(CanvasEditMode.SHAPE_PATH, tools.ActiveMode);
        Assert.True(tools.IsShapePathEditing);
        Assert.Null(tools.ActiveShapeKind);
        Assert.False(tools.CanCreateSubtitleBackground);
        Assert.Equal(Localization.Get("Preview.EditShapePath"), tools.ActiveToolLabel);
    }

    [Fact]
    public async Task PathCommandsQueryTheBridgeAndDoNothingWithoutAnAttachedTarget()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_PATH, ShapeKind.PATH));
        Assert.False(tools.FinishPathCommand.CanExecute(null));
        tools.FinishPathCommand.Execute(null);
        Assert.False(context.Editor.CanUndo);
        var executionCount = 0;
        context.Session.DrawingCommands.Requested += (_, request) =>
        {
            Assert.Same(context.Editor.Snapshot, request.Source);
            Assert.Equal(CanvasEditMode.CREATE_PATH, request.Mode);
            Assert.Equal(CanvasDrawingCommand.FINISH_PATH, request.Command);
            if (!request.IsQuery)
            {
                executionCount++;
            }
            request.Handled = true;
        };

        Assert.True(tools.FinishPathCommand.CanExecute(null));
        tools.FinishPathCommand.Execute(null);

        Assert.Equal(1, executionCount);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task RejectedCanvasCommandDoesNotCommitOtherwiseValidDrafts()
    {
        var line = new SubtitleLine { Text = "Caption" };
        var document = new ProjectDocument { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_PATH, ShapeKind.PATH));
        context.Session.ViewModel.Styles.Subtitle.FontSize = 72;

        tools.FinishPathCommand.Execute(null);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.HasProjectDrafts);
    }

    [Fact]
    public async Task BusyStateDisablesEveryEntryAndNeverDispatchesCanvasCommands()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var tools = context.Session.ViewModel.DrawingTools;
        Assert.True(tools.SelectTool(CanvasEditMode.CREATE_PATH, ShapeKind.PATH));
        var requests = 0;
        context.Session.DrawingCommands.Requested += (_, request) => { requests++; request.Handled = true; };
        using var lease = context.Session.AcquireEditingLease();

        Assert.False(tools.CanSelectTool(CanvasEditMode.POSITION));
        Assert.False(tools.FinishPathCommand.CanExecute(null));
        tools.FinishPathCommand.Execute(null);
        Assert.Equal(0, requests);
        Assert.False(tools.SelectTool(CanvasEditMode.CREATE_SHAPE, ShapeKind.ARROW));
        Assert.Equal(CanvasEditMode.CREATE_PATH, tools.ActiveMode);
    }
}
