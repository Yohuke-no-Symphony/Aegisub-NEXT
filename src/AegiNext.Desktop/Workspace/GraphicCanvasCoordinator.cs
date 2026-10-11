using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

/// <summary>固定图形编辑手势的文档与目标，将创建、裁剪和轮廓结果合并为单次事务。</summary>
internal sealed class GraphicCanvasCoordinator
{
    private readonly WorkbenchSession session;
    private ProjectDocument? gestureSource;
    private ProjectLayer? gestureLayer;
    private AnimationEditTarget? gestureTarget;
    private CanvasEditMode gestureMode;
    private ProjectDocument? creationSource;
    private GraphicClipPlacement? creationPlacement;
    private Guid? creationSelection;
    private ShapeKind creationKind;
    private CanvasEditMode creationMode;

    internal GraphicCanvasCoordinator(WorkbenchSession session)
    {
        this.session = session;
        session.SceneGestureCancellationRequested += (_, _) =>
        {
            CancelGesture();
            CancelCreation();
        };
    }

    internal bool CanMatchSubtitleTiming => session.SelectedLayer?.Kind is LayerKind.IMAGE or LayerKind.SHAPE && SelectedReferenceSubtitle() is not null;

    internal bool BeginGesture()
    {
        if (session.IsClosing || session.IsProjectBusy || !session.TryCommitDrafts() || session.SelectedLayer is not { } layer ||
            session.AnimationTarget is not { } target || !CanEdit(layer, session.SceneEditing.Mode))
        {
            return false;
        }
        CancelCreation();
        session.NotifyTaskInputChanged();
        gestureSource = session.DocumentSnapshot;
        gestureLayer = layer;
        gestureTarget = target;
        gestureMode = session.SceneEditing.Mode;
        session.SceneEditing.GestureTarget = target;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        return true;
    }

    internal void CancelGesture()
    {
        if (gestureTarget is not null && session.SceneEditing.GestureTarget == gestureTarget)
        {
            session.SceneEditing.GestureTarget = null;
        }
        gestureSource = null;
        gestureLayer = null;
        gestureTarget = null;
    }

    internal void CommitGesture(CanvasGraphicEditEventArgs value)
    {
        var source = gestureSource;
        var layer = gestureLayer;
        var mode = gestureMode;
        CancelGesture();
        if (source is null || layer is null || value.LayerId != layer.Id || session.SelectedLayerId != layer.Id ||
            session.SceneEditing.Mode != mode || !ReferenceEquals(source, session.DocumentSnapshot))
        {
            return;
        }
        _ = session.RunCommandAsync(() => session.EditAsync(() =>
        {
            if (!ReferenceEquals(source, session.DocumentSnapshot) || session.SelectedLayerId != layer.Id)
            {
                return;
            }
            var prepared = mode switch
            {
                CanvasEditMode.CROP when value.Image is { } image => ProjectEditingOperations.SetImageCrop(source, layer.Id, image.Crop),
                CanvasEditMode.SHAPE_PATH when value.Shape is { Kind: ShapeKind.PATH } shape => ProjectEditingOperations.UpdateShapeGeometry(source, layer.Id, shape),
                _ => source
            };
            if (prepared != source)
            {
                session.Editor.Apply("Edit graphic in video preview", _ => prepared);
            }
        }));
    }

    internal bool BeginCreation()
    {
        if (session.IsClosing || session.IsProjectBusy || !session.TryCommitDrafts() ||
            session.SceneEditing.Mode is not (CanvasEditMode.CREATE_SHAPE or CanvasEditMode.CREATE_PATH))
        {
            return false;
        }
        CancelGesture();
        session.NotifyTaskInputChanged();
        creationSource = session.DocumentSnapshot;
        creationPlacement = session.Assets.CapturePlacement();
        creationSelection = session.SelectedLayerId;
        creationMode = session.SceneEditing.Mode;
        creationKind = creationMode == CanvasEditMode.CREATE_PATH ? ShapeKind.PATH : session.SceneEditing.CreationShapeKind;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        return true;
    }

    internal void CancelCreation()
    {
        creationSource = null;
        creationPlacement = null;
        creationSelection = null;
    }

    internal void CreateShape(CanvasShapeCreatedEventArgs value)
    {
        var source = creationSource;
        var placement = creationPlacement;
        var selection = creationSelection;
        var mode = creationMode;
        var kind = creationKind;
        CancelCreation();
        if (source is null || placement is null || kind != value.Kind || session.SelectedLayerId != selection ||
            session.SceneEditing.Mode != mode || !ReferenceEquals(source, session.DocumentSnapshot))
        {
            return;
        }
        _ = session.RunCommandAsync(() => session.EditAsync(() =>
        {
            if (!ReferenceEquals(source, session.DocumentSnapshot) || session.SelectedLayerId != selection)
            {
                return;
            }
            var shape = new LayerShape(kind, value.Width, value.Height, value.Path);
            var layer = new ProjectLayer
            {
                Kind = LayerKind.SHAPE, Name = Localization.Get("Workbench.Shape" + kind),
                Start = placement.Start, End = placement.End, Shape = shape,
                Transform = new() { Position = value.Position },
                Fill = kind == ShapeKind.LINE || value.Path is { Closed: false } ? new(0, 0, 0, 0) : SceneColor.White,
                Stroke = SceneColor.White, StrokeWidth = kind == ShapeKind.LINE || value.Path is { Closed: false } ? 4 : 0
            };
            var result = ProjectEditingOperations.CreateGraphicClip(source, layer, placement.TrackId);
            session.Editor.Apply("Create shape", _ => result.Document);
            session.SelectLayer(result.ClipId, [result.ClipId]);
            session.SetGraphicCanvasMode(kind == ShapeKind.PATH ? CanvasEditMode.SHAPE_PATH : CanvasEditMode.POSITION);
        }));
    }

    internal Task ConvertSelectedShapeToPathAsync() => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (session.SelectedLayer is not { Shape: { } } layer)
        {
            return;
        }
        session.Editor.Apply("Convert shape to path", document => ProjectEditingOperations.ConvertShapeToPath(document, layer.Id));
        session.SetGraphicCanvasMode(CanvasEditMode.SHAPE_PATH);
    }));

    internal Task MatchSelectedSubtitleTimingAsync() => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (session.SelectedLayer is not { Kind: LayerKind.IMAGE or LayerKind.SHAPE } layer || SelectedReferenceSubtitle() is not { } subtitle)
        {
            return;
        }
        session.Editor.Apply("Match graphic to subtitle timing", document => ProjectEditingOperations.MatchGraphicClipTiming(document, layer.Id, subtitle.Id));
    }));

    internal Task CreateSubtitleBackgroundAsync() => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (session.SelectedLayer is not { Kind: LayerKind.SUBTITLE } subtitle)
        {
            return;
        }
        var source = session.DocumentSnapshot;
        var width = source.Width * 0.6;
        var height = source.Height * 0.12;
        var left = (source.Width - width) / 2;
        var top = source.Height - height - source.Height * 0.05;
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Name = Localization.Get("Preview.CreateSubtitleBackground"),
            Start = subtitle.Start, End = subtitle.End,
            Shape = new(ShapeKind.RECTANGLE, width, height) { CornerRadii = ShapeCornerRadii.Uniform(8) },
            Transform = new(X: left, Y: top), Fill = new(0, 0, 0, 0.65)
        };
        var result = ProjectEditingOperations.CreateGraphicClip(source, layer, subtitle.TrackId);
        var prepared = result.Document;
        if (result.CreatedTrackId is { } trackId)
        {
            var subtitleTrack = prepared.Tracks.Single(track => track.Id == subtitle.TrackId);
            prepared = ProjectEditingOperations.MoveTrack(prepared, trackId, prepared.Tracks.IndexOf(subtitleTrack));
        }
        session.Editor.Apply("Create subtitle background", _ => prepared);
        session.SelectLayer(result.ClipId, [result.ClipId]);
    }));

    private ProjectLayer? SelectedReferenceSubtitle()
    {
        var selected = session.SceneEditing.SelectedLayerIds;
        var subtitles = session.DocumentSnapshot.Layers.Where(layer => layer.Kind == LayerKind.SUBTITLE && selected.Contains(layer.Id)).Take(2).ToArray();
        return subtitles.Length == 1 ? subtitles[0] : null;
    }

    private static bool CanEdit(ProjectLayer layer, CanvasEditMode mode) => mode switch
    {
        CanvasEditMode.CROP => layer.Image is not null,
        CanvasEditMode.SHAPE_PATH => layer.Shape?.Kind == ShapeKind.PATH,
        _ => false
    };
}
