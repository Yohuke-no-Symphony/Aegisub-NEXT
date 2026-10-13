using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private readonly Dictionary<Guid, SubtitleTypographyProjection> typographyProjections = [];

    private SubtitleLine TypographySubtitle(EvaluatedLayer layer)
    {
        var subtitle = layer.Subtitle!;
        var values = layer.AnimationValues.Where(pair => pair.Key.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING)
            .ToImmutableDictionary();
        if (values.IsEmpty)
        {
            return subtitle;
        }
        if (typographyProjections.TryGetValue(layer.Source.Id, out var cached) && ReferenceEquals(cached.Source, subtitle) &&
            values.Count == cached.Values.Count && values.All(pair => cached.Values.TryGetValue(pair.Key, out var previous) && previous == pair.Value))
        {
            return cached.Subtitle;
        }
        var baseStyle = TypographyStyle(subtitle.Style, layer, 0, false);
        var boundaries = new SortedSet<int> { 0, subtitle.Text.Length };
        foreach (var span in subtitle.InlineSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var range in subtitle.AnimationRanges.Where(range => values.Keys.Any(target => target.TextRangeId == range.Id)))
        {
            boundaries.Add(range.Utf16Start);
            boundaries.Add(range.Utf16Start + range.Utf16Length);
        }
        var points = boundaries.ToArray();
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        for (var index = 0; index + 1 < points.Length; index++)
        {
            var start = points[index];
            var original = subtitle.InlineSpans.FirstOrDefault(span => start >= span.Utf16Start && start < span.Utf16Start + span.Utf16Length)?.Style ?? new();
            var style = TypographyStyle(original.ApplyTo(subtitle.Style), layer, start, true);
            var overlay = original with { FontSize = style.FontSize, LetterSpacing = style.LetterSpacing };
            if (spans.Count > 0 && spans[^1].Style == overlay)
            {
                spans[^1] = spans[^1] with { Utf16Length = points[index + 1] - spans[^1].Utf16Start };
            }
            else
            {
                spans.Add(new(start, points[index + 1] - start, overlay));
            }
        }
        var projected = subtitle with { Style = baseStyle, InlineSpans = spans.ToImmutable() };
        if (typographyProjections.Count >= 256 && !typographyProjections.ContainsKey(layer.Source.Id))
        {
            typographyProjections.Clear();
        }
        typographyProjections[layer.Source.Id] = new(subtitle, values, projected);
        return projected;
    }

    private static SubtitleStyle TypographyStyle(SubtitleStyle style, EvaluatedLayer layer, int offset, bool includeRanges)
    {
        Apply(null);
        if (includeRanges)
        {
            foreach (var range in layer.Subtitle!.AnimationRanges)
            {
                if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length)
                {
                    Apply(range.Id);
                }
            }
        }
        return style;

        void Apply(Guid? rangeId)
        {
            if (layer.AnimationValues.TryGetValue(new(AnimationProperty.FONT_SIZE, TextRangeId: rangeId), out var size))
            {
                style = style with { FontSize = size.Scalar };
            }
            if (layer.AnimationValues.TryGetValue(new(AnimationProperty.LETTER_SPACING, TextRangeId: rangeId), out var spacing))
            {
                style = style with { LetterSpacing = spacing.Scalar };
            }
        }
    }

    private static Dictionary<Guid, ProjectiveMapping> RangeMatrices(EvaluatedLayer layer, SubtitleTextLayout layout)
    {
        var result = new Dictionary<Guid, ProjectiveMapping>();
        foreach (var range in layer.AnimationRanges)
        {
            if (IsRangeIdentity(range))
            {
                continue;
            }
            var bounds = RangeBounds(range, layout);
            var pivot = RangePivot(range, layout, bounds);
            var matrix = SKMatrix.CreateTranslation(pivot.X + (float)range.Offset.X, pivot.Y + (float)range.Offset.Y);
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateRotationDegrees((float)range.Rotation));
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale((float)range.Scale.X, (float)range.Scale.Y));
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(-pivot.X, -pivot.Y));
            var transform = new LayerTransform
            {
                Scale = range.Scale, Rotation = range.Rotation, RotationX = range.RotationX, RotationY = range.RotationY,
                PerspectiveDistance = range.PerspectiveDistance, ProjectionTranslation = range.ProjectionTranslation
            };
            result.Add(range.Id, ProjectiveMapping.Create(transform, pivot,
                new(pivot.X + (float)range.Offset.X, pivot.Y + (float)range.Offset.Y), matrix));
        }
        return result;
    }

    private static ProjectiveMapping RangeMatrix(EvaluatedLayer layer, IReadOnlyDictionary<Guid, ProjectiveMapping> matrices, int offset)
    {
        var result = ProjectiveMapping.Identity;
        foreach (var range in layer.AnimationRanges)
        {
            if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length && matrices.TryGetValue(range.Id, out var matrix))
            {
                result = result.Then(matrix);
            }
        }
        return result;
    }

    private static SubtitleTextLayout VisibleLayout(EvaluatedLayer layer, SubtitleTextLayout layout)
    {
        if (layer.AnimationRanges.IsEmpty || layer.AnimationRanges.All(IsRangeIdentity))
        {
            return layout;
        }
        var matrices = RangeMatrices(layer, layout);
        var graphemes = layout.Graphemes.Select(glyph =>
        {
            var matrix = RangeMatrix(layer, matrices, glyph.Utf16Start);
            return glyph with
            {
                Bounds = matrix.MapRect(glyph.Bounds), LeadingCaret = matrix.MapRect(glyph.LeadingCaret),
                TrailingCaret = matrix.MapRect(glyph.TrailingCaret), LocalToVisible = matrix.CanvasMatrix,
                UntransformedBounds = glyph.Bounds, ProjectionMapping = matrix.IsProjective ? matrix : null
            };
        }).ToImmutableArray();
        var bounds = graphemes.Aggregate(SKRect.Empty, (current, glyph) => current.IsEmpty ? glyph.Bounds : SKRect.Union(current, glyph.Bounds));
        return new(layout.Text, bounds, layout.BasePosition, layout.Pivot, layout.HasInk, layout.Runs, graphemes, layout.GetCaretBounds(0));
    }

    private static bool IsRangeIdentity(SubtitleAnimationRange range) => range.Offset == default &&
        range.Scale == new ScenePoint(1, 1) && range.Rotation == 0 && !RangeRequiresProjection(range);

    private static bool RangeRequiresProjection(SubtitleAnimationRange range) => Math.IEEERemainder(range.RotationX, 360) != 0 ||
        Math.IEEERemainder(range.RotationY, 360) != 0 || range.ProjectionTranslation != default;

    private static SKRect RangeBounds(SubtitleAnimationRange range, SubtitleTextLayout layout) =>
        range.Utf16Start == 0 && range.Utf16Length == layout.Text.Length ? layout.Bounds :
            layout.GetSelectionRects(range.Utf16Start, range.Utf16Length).Aggregate(SKRect.Empty,
                (current, next) => current.IsEmpty ? next : SKRect.Union(current, next));

    private static SKPoint RangePivot(SubtitleAnimationRange range, SubtitleTextLayout layout, SKRect bounds) =>
        range.Pivot == SubtitleAnimationPivot.SUBTITLE_ANCHOR ? layout.Pivot : new(bounds.MidX, bounds.MidY);
}
