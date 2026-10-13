using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private ProjectLayerGeometry RangeGeometry(ProjectDocument document, EvaluatedLayer layer, SubtitleAnimationRange range)
    {
        var layout = Layout(document, layer).Snapshot;
        var bounds = RangeBounds(range, layout);
        var pivot = RangePivot(range, layout, bounds);
        var matrices = RangeMatrices(layer, layout);
        var outerGeometry = Geometry(document, layer, SKMatrix.Identity);
        var outer = outerGeometry.ProjectionMapping ?? ProjectiveMapping.Affine(outerGeometry.LocalToWorld);
        var parent = ProjectiveMapping.Identity;
        var afterTarget = false;
        foreach (var sibling in layer.AnimationRanges)
        {
            if (sibling.Id == range.Id)
            {
                afterTarget = true;
                continue;
            }
            if (afterTarget && range.Utf16Start >= sibling.Utf16Start &&
                range.Utf16Start < sibling.Utf16Start + sibling.Utf16Length && matrices.TryGetValue(sibling.Id, out var matrix))
            {
                parent = parent.Then(matrix);
            }
        }
        parent = parent.Then(outer);
        var projection = matrices.TryGetValue(range.Id, out var own) ? own.Then(parent) : parent;
        var graphemes = layout.Graphemes.Where(glyph => glyph.Utf16Start >= range.Utf16Start &&
                glyph.Utf16Start < range.Utf16Start + range.Utf16Length)
            .Select(glyph => new ProjectedSubtitleGrapheme(glyph.Bounds,
                RangeMatrix(layer, matrices, glyph.Utf16Start).Then(outer))).ToArray();
        var outline = ProjectiveMapping.ConvexHull(graphemes.SelectMany(glyph => glyph.Projection.MapBounds(glyph.OriginalBounds)));
        return new(bounds, pivot, pivot, projection.CanvasMatrix, parent.CanvasMatrix, layout.HasInk && graphemes.Any(HasProjectedArea))
        {
            AnimationRangeId = range.Id,
            AnimationRange = range,
            ProjectionMapping = projection,
            ParentProjectionMapping = parent,
            WorldOutline = outline,
            WorldGraphemes = graphemes
        };
    }

    private static bool HasProjectedArea(ProjectedSubtitleGrapheme glyph) => glyph.OriginalBounds.Width > 0 &&
        glyph.OriginalBounds.Height > 0 && glyph.Projection.Matrix.TryInvert(out _) &&
        ProjectiveMapping.HasArea(glyph.Projection.MapBounds(glyph.OriginalBounds));
}
