using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal ProjectDocument PrepareGraphicStyleBatchDrafts(ProjectDocument source, ProjectDocument prepared,
        ProjectLayer primary, IReadOnlyList<Guid> targets)
    {
        var originals = source.Layers.ToDictionary(layer => layer.Id);
        var edited = prepared.Layers.Single(layer => layer.Id == primary.Id);
        foreach (var id in targets)
        {
            if (id == primary.Id)
            {
                continue;
            }
            var original = originals[id];
            if (primary.Kind == LayerKind.IMAGE)
            {
                var image = MergeBatchImage(original.Image!, edited.Image!, ViewModel.Styles.Image);
                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, id, layer => layer with { Image = image });
            }
            else if (primary.Kind == LayerKind.SHAPE)
            {
                var vm = ViewModel.Styles.Shape;
                var shape = MergeBatchShape(original.Shape!, edited.Shape!, vm.DraftFields);
                if (vm.DraftFields.Contains(nameof(ShapeStyleViewModel.FillDraft)))
                {
                    prepared = ApplyGraphicBatchColor(prepared, original, vm.FillDraft, AnimationProperty.FILL, "FillPicker");
                }
                if (vm.DraftFields.Contains(nameof(ShapeStyleViewModel.StrokeDraft)))
                {
                    prepared = ApplyGraphicBatchColor(prepared, original, vm.StrokeDraft, AnimationProperty.STROKE, "StrokePicker");
                }
                if (vm.DraftFields.Contains(nameof(ShapeStyleViewModel.StrokeWidth)))
                {
                    prepared = ApplyGraphicBatchStrokeWidth(prepared, original, vm);
                }
                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, id, layer => layer with
                {
                    Shape = shape,
                    StrokeWidth = shape.Kind == ShapeKind.LINE && original.Shape!.Kind != ShapeKind.LINE && layer.StrokeWidth == 0
                        ? 4 : layer.StrokeWidth
                });
            }
        }
        return prepared;
    }

    private LayerImage MergeBatchImage(LayerImage original, LayerImage edited, ImageStyleViewModel vm)
    {
        var fields = vm.DraftFields;
        var width = fields.Contains(nameof(ImageStyleViewModel.Width)) ? edited.Width : original.Width;
        var height = fields.Contains(nameof(ImageStyleViewModel.Height)) ? edited.Height : original.Height;
        if (fields.Contains(nameof(ImageStyleViewModel.Width)) && fields.Contains(nameof(ImageStyleViewModel.Height)))
        {
            if (vm.ResizeDriver == nameof(ImageStyleViewModel.Width))
            {
                height = original.Height * width / original.Width;
            }
            else if (vm.ResizeDriver == nameof(ImageStyleViewModel.Height))
            {
                width = original.Width * height / original.Height;
            }
        }
        RequireGraphicField(double.IsFinite(width) && width is >= 0.001 and <= 1000000, "LayerWidthInput", "GraphicSize");
        RequireGraphicField(double.IsFinite(height) && height is >= 0.001 and <= 1000000, "LayerHeightInput", "GraphicSize");
        var crop = original.Crop with
        {
            X = fields.Contains(nameof(ImageStyleViewModel.CropX)) ? edited.Crop.X : original.Crop.X,
            Y = fields.Contains(nameof(ImageStyleViewModel.CropY)) ? edited.Crop.Y : original.Crop.Y,
            Width = fields.Contains(nameof(ImageStyleViewModel.CropWidth)) ? edited.Crop.Width : original.Crop.Width,
            Height = fields.Contains(nameof(ImageStyleViewModel.CropHeight)) ? edited.Crop.Height : original.Crop.Height
        };
        RequireGraphicField(crop.Width > 0 && crop.Width <= 1 - crop.X, "ImageCropWidthInput", "ImageCrop");
        RequireGraphicField(crop.Height > 0 && crop.Height <= 1 - crop.Y, "ImageCropHeightInput", "ImageCrop");
        return original with
        {
            Width = width,
            Height = height,
            Fit = fields.Contains(nameof(ImageStyleViewModel.Fit)) ? edited.Fit : original.Fit,
            Crop = crop,
            Playback = MergeBatchImagePlayback(original.Playback, edited.Playback, fields)
        };
    }

    private static ImagePlayback? MergeBatchImagePlayback(ImagePlayback? original, ImagePlayback? edited,
        IReadOnlySet<string> fields)
    {
        var toggled = fields.Contains(nameof(ImageStyleViewModel.AnimationEnabled));
        if (edited is null)
        {
            return toggled ? null : original;
        }
        if (original is null && !toggled)
        {
            return null;
        }
        var playback = original ?? new ImagePlayback();
        return playback with
        {
            SourceOffset = fields.Contains(nameof(ImageStyleViewModel.PlaybackOffset)) ? edited.SourceOffset : playback.SourceOffset,
            Speed = fields.Contains(nameof(ImageStyleViewModel.PlaybackSpeed)) ? edited.Speed : playback.Speed,
            Mode = fields.Contains(nameof(ImageStyleViewModel.PlaybackMode)) ? edited.Mode : playback.Mode
        };
    }

    private static LayerShape MergeBatchShape(LayerShape original, LayerShape edited, IReadOnlySet<string> fields)
    {
        var width = fields.Contains(nameof(ShapeStyleViewModel.Width)) ? edited.Width : original.Width;
        var height = fields.Contains(nameof(ShapeStyleViewModel.Height)) ? edited.Height : original.Height;
        var shape = ShapeGeometry.Resize(original, width, height);
        if (fields.Contains(nameof(ShapeStyleViewModel.ShapeType)) && original.Kind != ShapeKind.PATH && edited.Kind != ShapeKind.PATH)
        {
            var kind = original.Kind == ShapeKind.ROUNDED_RECTANGLE && edited.Kind == ShapeKind.RECTANGLE
                ? original.Kind : edited.Kind;
            shape = shape with { Kind = kind };
            if (original.Kind == ShapeKind.ROUNDED_RECTANGLE && kind != original.Kind && original.CornerRadii is null)
            {
                shape = shape with { CornerRadii = original.EffectiveCornerRadii };
            }
        }
        if (shape.Kind is ShapeKind.RECTANGLE or ShapeKind.ROUNDED_RECTANGLE)
        {
            var corners = shape.EffectiveCornerRadii;
            var requested = edited.EffectiveCornerRadii;
            var changed = corners with
            {
                TopLeft = fields.Contains(nameof(ShapeStyleViewModel.CornerTopLeft)) ? requested.TopLeft : corners.TopLeft,
                TopRight = fields.Contains(nameof(ShapeStyleViewModel.CornerTopRight)) ? requested.TopRight : corners.TopRight,
                BottomRight = fields.Contains(nameof(ShapeStyleViewModel.CornerBottomRight)) ? requested.BottomRight : corners.BottomRight,
                BottomLeft = fields.Contains(nameof(ShapeStyleViewModel.CornerBottomLeft)) ? requested.BottomLeft : corners.BottomLeft
            };
            if (changed != corners)
            {
                shape = shape with { CornerRadii = changed };
            }
        }
        if (shape.Kind == ShapeKind.ARROW)
        {
            shape = shape with
            {
                ArrowHeadRatio = fields.Contains(nameof(ShapeStyleViewModel.ArrowHead)) ? edited.ArrowHeadRatio : shape.ArrowHeadRatio,
                ArrowShaftRatio = fields.Contains(nameof(ShapeStyleViewModel.ArrowShaft)) ? edited.ArrowShaftRatio : shape.ArrowShaftRatio
            };
        }
        return shape;
    }

    private ProjectDocument ApplyGraphicBatchColor(ProjectDocument document, ProjectLayer layer, ColorDraft draft,
        AnimationProperty property, string field)
    {
        var original = property == AnimationProperty.FILL ? layer.Fill : layer.Stroke;
        var target = StyleAnimationTarget(layer);
        var displayed = AnimationEditOperations.Value(layer, property, target, original);
        var requested = draft.ApplyEditsTo(displayed);
        if (requested == displayed)
        {
            return document;
        }
        RequireGraphicField(!layer.Tracks.Any(track => track.Property == property && !track.Transforms.IsEmpty),
            field, "StyleOrderedTransformHint");
        if (target.IsKeyframe || layer.Tracks.Any(track => track.Property == property))
        {
            return AnimationEditOperations.SetValue(document, target, property, requested);
        }
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => property == AnimationProperty.FILL
            ? current with { Fill = requested } : current with { Stroke = requested });
    }

    private ProjectDocument ApplyGraphicBatchStrokeWidth(ProjectDocument document, ProjectLayer layer, ShapeStyleViewModel vm)
    {
        var property = AnimationProperty.STROKE_WIDTH;
        var target = StyleAnimationTarget(layer);
        var displayed = AnimationEditOperations.Value(layer, property, target, layer.StrokeWidth);
        var requested = ReadStyleNumber(vm.StrokeWidth.RawText, displayed, "StrokeWidth", "StrokeWidthInput");
        if (requested == displayed)
        {
            return document;
        }
        RequireGraphicField(!layer.Tracks.Any(track => track.Property == property && !track.Transforms.IsEmpty),
            "StrokeWidthInput", "StyleOrderedTransformHint");
        if (target.IsKeyframe || layer.Tracks.Any(track => track.Property == property))
        {
            return AnimationEditOperations.SetValue(document, target, property, requested);
        }
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => current with { StrokeWidth = requested });
    }
}
