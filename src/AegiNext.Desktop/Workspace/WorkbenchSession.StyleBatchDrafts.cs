using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void RestoreStyleDraftIntent(Action restore)
    {
        using (BeginWorkbenchUpdate())
        {
            restore();
            if (!stylesDirty && !effectsDirty)
            {
                SceneEditing.DraftTarget = null;
            }
            draftRevision++;
        }
        QueueInspectorPreview(true);
    }

    internal bool CanEditStyleProperty(AnimationProperty property)
    {
        return SelectedLayer is { } primary && StyleLayerIds(primary.Kind).All(id =>
            !editor.Snapshot.Layers.Single(layer => layer.Id == id).Tracks.Any(track =>
                track.Property == property && !track.Transforms.IsEmpty));
    }

    private AnimationEditTarget StyleAnimationTarget(ProjectLayer layer)
    {
        var primary = SelectedLayer!;
        var target = AnimationTarget!;
        if (layer.Id == primary.Id)
        {
            return target;
        }
        var projectTime = primary.Start + target.LocalTime - primary.AnimationOffset;
        var identity = target.Target ?? SceneEditing.Target;
        return new(layer.Id, LayerAnimationTiming.ClampTime(layer, projectTime - layer.Start + layer.AnimationOffset),
            target.IsKeyframe, new(identity.Property, State: layer.Kind == LayerKind.SUBTITLE
                ? identity.State : SubtitleAnimationState.NORMAL));
    }

    private ProjectDocument PrepareStyleSelectionDrafts(ProjectDocument document, bool preview)
    {
        if (SelectedLayer is not { } primary)
        {
            return document;
        }
        var targets = StyleLayerIds(primary.Kind);
        if (targets.IsEmpty)
        {
            return document;
        }
        var prepared = ViewModel.Styles.Prepare(document, preview);
        if (targets.Length == 1)
        {
            return prepared;
        }
        prepared = primary.Kind == LayerKind.SUBTITLE
            ? PrepareSubtitleStyleBatchDrafts(document, prepared, primary, targets)
            : PrepareGraphicStyleBatchDrafts(document, prepared, primary, targets);
        foreach (var id in targets.Where(id => id != primary.Id))
        {
            prepared = PrepareBatchStylePosition(prepared, id);
        }
        if (prepared != document && primary.Kind == LayerKind.SUBTITLE)
        {
            var subtitles = prepared.Layers.Where(layer => targets.Contains(layer.Id)).Select(layer => layer.SubtitleId!.Value);
            foreach (var id in subtitles)
            {
                prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, id, line => line with { InlineSpans = [] });
            }
        }
        return prepared;
    }

    private ProjectDocument PrepareBatchStylePosition(ProjectDocument document, Guid id)
    {
        var layer = document.Layers.Single(layer => layer.Id == id);
        LayerPositionDraft source;
        LayerPositionDraft target;
        var subtitle = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        var transform = SceneEvaluator.EvaluateLayer(layer, subtitle, StyleAnimationTarget(layer).LocalTime, geometryEffectPlans).Transform;
        if (subtitle is not null)
        {
            source = ViewModel.Styles.Subtitle.Position;
            if (!source.HasEdits)
            {
                return document;
            }
            var placement = layerPlacement.Resolve(document, projectDirectory, layer, StyleAnimationTarget(layer).LocalTime);
            var draft = new SubtitlePositionDraft();
            draft.Load(subtitle.Style, placement.Position, placement.BasePosition is not null,
                placement.Geometry is { } geometry ? geometry with { Transform = transform } : null);
            target = draft;
        }
        else
        {
            var editor = (Panels.Styles.GraphicStyleEditorViewModel)ViewModel.Styles.ActiveEditor!;
            source = editor.Position;
            if (!source.HasEdits)
            {
                return document;
            }
            var size = layer.Image is { } image ? new ScenePoint(image.Width, image.Height) : new(layer.Shape!.Width, layer.Shape.Height);
            target = new();
            target.Load(layer.Position ?? new(), new(new(document.Width, document.Height), new(), size, transform));
        }
        source.ApplyEditsTo(target);
        if (target.Validate() is { } field)
        {
            ViewModel.InvalidFieldKey = field;
            throw new InvalidDataException(Localization.Get("Workbench.ExplicitPosition"));
        }
        if (subtitle is not null)
        {
            document = WorkspaceDraftOperations.UpdateSubtitle(document, subtitle.Id, line => line with
            {
                Style = line.Style with { Position = (SubtitlePosition?)target.CreatePosition() }
            });
        }
        return WorkspaceDraftOperations.UpdateLayer(document, id, current => current with
        {
            Position = subtitle is null ? target.CreatePosition() : current.Position,
            Transform = target.PrepareTransform(current.Transform)
        });
    }

    private static T StyleFieldValue<T>(T source, T requested, T target, IReadOnlySet<string> fields, params string[] names)
    {
        return !EqualityComparer<T>.Default.Equals(source, requested) || names.Any(fields.Contains) ? requested : target;
    }
}
