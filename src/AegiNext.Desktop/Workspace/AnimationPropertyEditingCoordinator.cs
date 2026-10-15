using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnimationPropertyEditingCoordinator(WorkbenchSession session)
{
    private readonly Dictionary<(Guid LayerId, AnimationTrackTarget Target), AnimationPropertyRowViewModel> rows = [];
    private ProjectDocument? overlaySource;
    private ProjectDocument? overlayResult;
    private long overlayRevision;

    internal bool HasDrafts => rows.Values.Any(row => row.HasDraft);

    internal AnimationPropertyRowViewModel GetRow(Guid layerId, AnimationTrackTarget target)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        var layer = session.DocumentSnapshot.Layers.Single(layer => layer.Id == layerId);
        if (!rows.TryGetValue((layerId, target), out var row))
        {
            row = new(session, layerId, target);
            rows.Add((layerId, target), row);
        }
        var subtitle = layer.SubtitleId is { } id ? session.DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        row.Load(session.EffectPropertyValue(layer, target), session.InspectorTrack(layer, target),
            !SubtitleAnimationEvaluation.IsBaseValueUniform(layer, subtitle, target));
        return row;
    }

    internal AnimationPropertyRowViewModel? FindRow(string? field) => rows.Values.FirstOrDefault(row => row.OwnsField(field));

    internal AnimationPropertyRowViewModel[] GetMaskRows(Guid layerId, Guid? nodeId)
    {
        var layer = session.DocumentSnapshot.Layers.Single(layer => layer.Id == layerId);
        if (layer.Mask is null)
        {
            return [];
        }
        var targets = new List<AnimationTrackTarget>();
        if (layer.Mask is RectangleClipMask)
        {
            targets.Add(new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT));
            targets.Add(new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT));
        }
        targets.Add(new(AnimationProperty.MASK_POSITION));
        targets.Add(new(AnimationProperty.MASK_SCALE));
        targets.Add(new(AnimationProperty.MASK_ROTATION));
        if (layer.Mask is VectorClipMask vector && nodeId is { } selected &&
            vector.Contours.SelectMany(contour => contour.Nodes).Any(node => node.Id == selected))
        {
            targets.Add(new(AnimationProperty.MASK_NODE_POSITION, selected));
            targets.Add(new(AnimationProperty.MASK_NODE_IN_HANDLE, selected));
            targets.Add(new(AnimationProperty.MASK_NODE_OUT_HANDLE, selected));
        }
        return targets.Select(target => GetRow(layerId, target)).ToArray();
    }

    internal void Refresh()
    {
        foreach (var key in rows.Keys.Where(key =>
        {
            var layer = session.DocumentSnapshot.Layers.FirstOrDefault(layer => layer.Id == key.LayerId);
            return layer is null || key.Target.TextRangeId is { } rangeId &&
                !session.DocumentSnapshot.Subtitles.Any(line => line.Id == layer.SubtitleId && line.AnimationRanges.Any(range => range.Id == rangeId)) ||
                AnimationPropertyMetadata.IsMaskProperty(key.Target.Property) && (layer.Mask is null ||
                    key.Target.Property is (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT) && layer.Mask is not RectangleClipMask ||
                    key.Target.NodeId is { } nodeId && (layer.Mask is not VectorClipMask vector ||
                        !vector.Contours.SelectMany(contour => contour.Nodes).Any(node => node.Id == nodeId)));
        }).ToArray())
        {
            rows.Remove(key);
        }
    }

    internal ProjectDocument Prepare(ProjectDocument document)
    {
        foreach (var row in rows.Values.Where(row => row.HasDraft))
        {
            document = row.Prepare(document);
        }
        return document;
    }

    internal ProjectDocument Overlay(ProjectDocument document)
    {
        if (!HasDrafts)
        {
            overlaySource = null;
            overlayResult = null;
            return document;
        }
        if (ReferenceEquals(overlaySource, document) && overlayRevision == session.EffectPropertyDraftRevision && overlayResult is { } cached)
        {
            return cached;
        }
        var source = document;
        var invalidPanel = session.ViewModel.InvalidPanelId;
        var invalidField = session.ViewModel.InvalidFieldKey;
        try
        {
            foreach (var row in rows.Values.Where(row => row.HasDraft))
            {
                document = row.Prepare(document);
            }
            ProjectValidator.Validate(document);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            document = ReferenceEquals(overlaySource, source) ? overlayResult ?? source : source;
        }
        finally
        {
            session.ViewModel.InvalidPanelId = invalidPanel;
            session.ViewModel.InvalidFieldKey = invalidField;
        }
        overlaySource = source;
        overlayRevision = session.EffectPropertyDraftRevision;
        overlayResult = document;
        return document;
    }

    internal void Accept()
    {
        overlaySource = null;
        overlayResult = null;
        foreach (var row in rows.Values.Where(row => row.HasDraft))
        {
            row.Accept();
        }
        Refresh();
    }

    internal bool Restore(string? field)
    {
        if (field is null || FindRow(field) is not { } row)
        {
            return false;
        }
        row.Restore(field);
        return true;
    }
}
