using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private void DrawKaraokeRun(SKCanvas canvas, SubtitleLayoutRun run, SubtitleLine subtitle,
        EvaluatedLayer layer, SubtitleStyle normal, SubtitlePreviewMode previewMode,
        IReadOnlyDictionary<Guid, ProjectiveMapping> matrices, ProjectiveMapping outerProjection)
    {
        var fragments = KaraokeFragments(run, subtitle, layer, normal, previewMode, matrices);
        if (fragments.Count == run.Shape.Clusters.Length && fragments.All(fragment => fragment.InkClip is null) &&
            fragments.All(fragment => fragment.Style == fragments[0].Style && fragment.Matrix == SKMatrix.Identity &&
                                      !fragment.Projection.IsProjective))
        {
            DrawSubtitleRun(canvas, run, fragments[0].Style);
            return;
        }
        for (var component = 0; component < 3; component++)
        {
            var first = 0;
            while (first < fragments.Count)
            {
                var key = InkPaint(fragments[first].Style, component);
                var end = first + 1;
                while (end < fragments.Count && InkPaint(fragments[end].Style, component) == key &&
                       fragments[end].EquivalentTransform(fragments[first]))
                {
                    end++;
                }
                if (key.Color.Alpha > 0 && (component != 1 || key.StrokeWidth > 0))
                {
                    var saved = canvas.Save();
                    try
                    {
                        var projection = fragments[first].Projection.Then(outerProjection);
                        if (projection.ApplyToCanvas(canvas))
                        {
                            DrawOwnedInk(canvas, run, key, fragments.GetRange(first, end - first));
                        }
                    }
                    finally
                    {
                        canvas.RestoreToCount(saved);
                    }
                }
                first = end;
            }
        }
    }

    private static List<SubtitleVisualFragment> KaraokeFragments(SubtitleLayoutRun run, SubtitleLine subtitle,
        EvaluatedLayer layer, SubtitleStyle normal, SubtitlePreviewMode previewMode, IReadOnlyDictionary<Guid, ProjectiveMapping> matrices)
    {
        var time = layer.LocalTime;
        var timing = new Dictionary<int, (SubtitleKaraokeSpan Span, float Advance)>();
        var segmentIndex = 0;
        SubtitleKaraokeSpan? previousSpan = null;
        var advance = 0f;
        foreach (var glyph in run.Graphemes)
        {
            while (segmentIndex < run.Karaoke.Length && glyph.Utf16Start >= run.Karaoke[segmentIndex].Segment.Utf16Start +
                   run.Karaoke[segmentIndex].Segment.Utf16Length)
            {
                segmentIndex++;
            }
            if (segmentIndex >= run.Karaoke.Length)
            {
                break;
            }
            var span = run.Karaoke[segmentIndex];
            if (glyph.Utf16Start < span.Segment.Utf16Start)
            {
                continue;
            }
            if (!ReferenceEquals(previousSpan, span))
            {
                advance = span.AdvanceBefore;
                previousSpan = span;
            }
            timing.Add(glyph.Utf16Start, (span, advance));
            advance += glyph.Bounds.Width;
        }
        var clusters = run.Shape.Clusters.ToArray().OrderBy(cluster => cluster.Utf16Start).ToArray();
        var fragments = new List<SubtitleVisualFragment>();
        var glyphCursor = 0;
        for (var index = 0; index < clusters.Length; index++)
        {
            var cluster = clusters[index];
            var end = index + 1 < clusters.Length ? clusters[index + 1].Utf16Start : run.Text.Length;
            var firstMember = glyphCursor;
            while (glyphCursor < run.Graphemes.Length && run.Graphemes[glyphCursor].Utf16Start < run.Utf16Offset + end)
            {
                glyphCursor++;
            }
            var pieces = new List<SubtitleVisualFragment>();
            foreach (var glyph in run.Graphemes.AsSpan(firstMember, glyphCursor - firstMember))
            {
                var ordinary = SubtitleAnimationEvaluation.ApplyStyleAnimations(layer, run.Style, glyph.Utf16Start, SubtitleAnimationState.NORMAL);
                var matrix = RangeMatrix(layer, matrices, glyph.Utf16Start);
                timing.TryGetValue(glyph.Utf16Start, out var clock);
                var segment = clock.Span?.Segment;
                var range = StyleRangeAt(subtitle, glyph.Utf16Start);
                if (previewMode == SubtitlePreviewMode.NORMAL || segment is null && previewMode == SubtitlePreviewMode.TIMED)
                {
                    pieces.Add(new(ordinary, cluster.Utf16Start, glyph.Bounds, glyph.Bounds) { Matrix = matrix.CanvasMatrix, Projection = matrix });
                    continue;
                }
                if (segment is null && previewMode != SubtitlePreviewMode.TIMED)
                {
                    segment = InactiveSegmentAt(subtitle, glyph.Utf16Start);
                }
                var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment, range?.InactiveStyle);
                var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, subtitle.KaraokeStyle, segment, range?.ActiveStyle);
                inactive = SubtitleAnimationEvaluation.ApplyStyleAnimations(layer, inactive, glyph.Utf16Start, SubtitleAnimationState.INACTIVE);
                active = SubtitleAnimationEvaluation.ApplyStyleAnimations(layer, active, glyph.Utf16Start, SubtitleAnimationState.ACTIVE);
                if (segment?.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP)
                {
                    inactive = inactive with { StrokeWidth = 0 };
                }
                var amount = previewMode switch
                {
                    SubtitlePreviewMode.HIGHLIGHTED => glyph.Bounds.Width,
                    SubtitlePreviewMode.INACTIVE => 0,
                    _ => ActiveAdvance(segment!, time, clock.Span!.TotalAdvance, clock.Advance, glyph.Bounds.Width)
                };
                if (amount <= 0 || inactive == active)
                {
                    pieces.Add(new(inactive, cluster.Utf16Start, glyph.Bounds, glyph.Bounds) { Matrix = matrix.CanvasMatrix, Projection = matrix });
                }
                else if (amount >= glyph.Bounds.Width)
                {
                    pieces.Add(new(active, cluster.Utf16Start, glyph.Bounds, glyph.Bounds) { Matrix = matrix.CanvasMatrix, Projection = matrix });
                }
                else
                {
                    var activeBounds = glyph.Bounds;
                    var inactiveBounds = glyph.Bounds;
                    if (run.Direction == TextDirection.RIGHT_TO_LEFT)
                    {
                        activeBounds.Left = glyph.Bounds.Right - amount;
                        inactiveBounds.Right = activeBounds.Left;
                    }
                    else
                    {
                        activeBounds.Right = glyph.Bounds.Left + amount;
                        inactiveBounds.Left = activeBounds.Right;
                    }
                    pieces.Add(new(active, cluster.Utf16Start, activeBounds, activeBounds) { Matrix = matrix.CanvasMatrix, Projection = matrix });
                    pieces.Add(new(inactive, cluster.Utf16Start, inactiveBounds, inactiveBounds) { Matrix = matrix.CanvasMatrix, Projection = matrix });
                }
            }
            if (pieces.Count == 0)
            {
                continue;
            }
            var decorationBounds = pieces.Select(piece => piece.DecorationClip).Aggregate(SKRect.Union);
            if (pieces.All(piece => piece.Style == pieces[0].Style && piece.EquivalentTransform(pieces[0])))
            {
                fragments.Add(new(pieces[0].Style, cluster.Utf16Start, null, decorationBounds)
                {
                    Matrix = pieces[0].Matrix, Projection = pieces[0].Projection
                });
                continue;
            }
            var ink = cluster.InkBounds;
            ink.Offset(run.Position);
            var stroke = (float)pieces.Max(piece => piece.Style.StrokeWidth) + 1;
            var full = SKRect.Union(ink, decorationBounds);
            full.Inflate(stroke, stroke);
            foreach (var piece in pieces.OrderBy(piece => piece.InkClip!.Value.Left))
            {
                var clip = piece.InkClip!.Value;
                clip.Top = full.Top;
                clip.Bottom = full.Bottom;
                if (clip.Left <= decorationBounds.Left)
                {
                    clip.Left = full.Left;
                }
                if (clip.Right >= decorationBounds.Right)
                {
                    clip.Right = full.Right;
                }
                var previous = fragments.Count > 0 ? fragments[^1] : null;
                if (previous is { InkClip: { } previousClip } && previous.Utf16Cluster == piece.Utf16Cluster &&
                    previous.Style == piece.Style && previous.EquivalentTransform(piece) && previousClip.Right >= clip.Left)
                {
                    fragments[^1] = previous with
                    {
                        InkClip = SKRect.Union(previousClip, clip),
                        DecorationClip = SKRect.Union(previous.DecorationClip, piece.DecorationClip)
                    };
                }
                else
                {
                    fragments.Add(piece with { InkClip = clip });
                }
            }
        }
        var clusterOrder = run.Shape.Clusters.ToArray().Select((cluster, index) => (cluster.Utf16Start, index))
            .ToDictionary(value => value.Utf16Start, value => value.index);
        fragments.Sort((first, second) =>
        {
            var order = clusterOrder[first.Utf16Cluster].CompareTo(clusterOrder[second.Utf16Cluster]);
            return order != 0 ? order : (first.InkClip?.Left ?? 0).CompareTo(second.InkClip?.Left ?? 0);
        });
        return fragments;
    }

    private static SubtitleKaraokeStyleSpan? StyleRangeAt(SubtitleLine subtitle, int utf16Offset)
    {
        var low = 0;
        var high = subtitle.KaraokeStyleSpans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = subtitle.KaraokeStyleSpans[middle];
            if (utf16Offset < span.Utf16Start)
            {
                high = middle - 1;
            }
            else if (utf16Offset >= span.Utf16Start + span.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return span;
            }
        }
        return null;
    }

    private static KaraokeSegment? InactiveSegmentAt(SubtitleLine subtitle, int utf16Offset)
    {
        var low = 0;
        var high = subtitle.InactiveKaraoke.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var segment = subtitle.InactiveKaraoke[middle];
            if (utf16Offset < segment.Utf16Start)
            {
                high = middle - 1;
            }
            else if (utf16Offset >= segment.Utf16Start + segment.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return segment;
            }
        }
        return null;
    }

    private static float ActiveAdvance(KaraokeSegment segment, MediaTime time, float total, float before, float width)
    {
        if (time < segment.Start || segment.HighlightKind == KaraokeHighlightKind.SWEEP && time == segment.Start)
        {
            return 0;
        }
        if (segment.HighlightKind != KaraokeHighlightKind.SWEEP || time >= segment.End)
        {
            return width;
        }
        var elapsed = time - segment.Start;
        var duration = segment.End - segment.Start;
        var progress = Math.Clamp(((double)elapsed.Numerator / elapsed.Denominator) /
            ((double)duration.Numerator / duration.Denominator), 0, 1);
        return Math.Clamp(total * (float)progress - before, 0, width);
    }

    private static SubtitleInkPaintKey InkPaint(SubtitleStyle style, int component)
    {
        return component switch
        {
            0 => new(style.ShadowColor, style.ShadowBlur, 0, style.ShadowOffset),
            1 => new(style.Stroke, style.StrokeBlur, style.StrokeWidth, new()),
            _ => new(style.Fill, style.FillBlur, 0, new())
        };
    }

    private void DrawOwnedInk(SKCanvas canvas, SubtitleLayoutRun run, SubtitleInkPaintKey key,
        IEnumerable<SubtitleVisualFragment> fragments)
    {
        var owned = MergeInkFragments(run, key, fragments);
        var clippedSource = owned.Any(fragment => fragment.InkClip is not null) || run.Style.Underline || run.Style.Strikethrough;
        var leftCaret = run.Graphemes.Min(glyph => glyph.Bounds.Left);
        var rightCaret = run.Graphemes.Max(glyph => glyph.Bounds.Right);
        var saved = canvas.Save();
        using var paint = Paint(key.Color);
        using var maskFilter = key.Blur > 0 && !clippedSource ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)key.Blur) : null;
        paint.MaskFilter = maskFilter;
        using var filter = key.Blur > 0 && clippedSource ? SKImageFilter.CreateBlur((float)key.Blur, (float)key.Blur) : null;
        using var layerPaint = filter is null ? null : new SKPaint { ImageFilter = filter };
        try
        {
            canvas.Translate((float)key.Offset.X, (float)key.Offset.Y);
            if (layerPaint is not null)
            {
                canvas.SaveLayer(layerPaint);
            }
            if (key.StrokeWidth > 0)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = (float)key.StrokeWidth * 2;
                paint.StrokeJoin = SKStrokeJoin.Round;
            }
            foreach (var fragment in owned)
            {
                var clipped = canvas.Save();
                if (fragment.InkClip is { } clip)
                {
                    canvas.ClipRect(clip);
                }
                canvas.DrawText(run.Shape.GetClusterBlob(fragment.Utf16Cluster), run.Position.X, run.Position.Y, paint);
                canvas.RestoreToCount(clipped);
                clipped = canvas.Save();
                var decorationClip = fragment.DecorationClip;
                if (decorationClip.Left <= leftCaret)
                {
                    decorationClip.Left -= (float)key.StrokeWidth + 1;
                }
                if (decorationClip.Right >= rightCaret)
                {
                    decorationClip.Right += (float)key.StrokeWidth + 1;
                }
                decorationClip.Top = run.Position.Y + run.Shape.FontMetrics.Top - (float)key.StrokeWidth - 1;
                decorationClip.Bottom = run.Position.Y + run.Shape.FontMetrics.Bottom + (float)key.StrokeWidth + 1;
                canvas.ClipRect(decorationClip);
                foreach (var decoration in Decorations(run))
                {
                    var bounds = decoration;
                    bounds.Offset(run.Position);
                    canvas.DrawRect(bounds, paint);
                }
                canvas.RestoreToCount(clipped);
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }

    private static List<SubtitleVisualFragment> MergeInkFragments(SubtitleLayoutRun run, SubtitleInkPaintKey key,
        IEnumerable<SubtitleVisualFragment> fragments)
    {
        var result = new List<SubtitleVisualFragment>();
        foreach (var cluster in fragments.GroupBy(fragment => fragment.Utf16Cluster))
        {
            foreach (var fragment in cluster.OrderBy(fragment => fragment.InkClip?.Left ?? float.NegativeInfinity))
            {
                var previous = result.Count > 0 ? result[^1] : null;
                if (previous is not null && previous.Utf16Cluster == fragment.Utf16Cluster &&
                    (previous.InkClip is null || fragment.InkClip is null || previous.InkClip.Value.Right >= fragment.InkClip.Value.Left))
                {
                    result[^1] = previous with
                    {
                        InkClip = previous.InkClip is { } first && fragment.InkClip is { } second ? SKRect.Union(first, second) : null,
                        DecorationClip = SKRect.Union(previous.DecorationClip, fragment.DecorationClip)
                    };
                }
                else
                {
                    result.Add(fragment);
                }
            }
            var bounds = run.Shape.GetClusterInkBounds(cluster.Key);
            bounds.Offset(run.Position);
            bounds.Inflate((float)key.StrokeWidth, (float)key.StrokeWidth);
            var last = result[^1];
            if (last.InkClip is { } clip && clip.Contains(bounds))
            {
                result[^1] = last with { InkClip = null };
            }
        }
        return result;
    }
}
