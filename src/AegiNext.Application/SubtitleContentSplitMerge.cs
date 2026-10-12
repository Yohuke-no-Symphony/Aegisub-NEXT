using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleContentSplitMerge
{
    internal static ImmutableArray<SubtitleInlineSpan> SplitSpans(ImmutableArray<SubtitleInlineSpan> spans, int offset, bool right)
    {
        var result = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        foreach (var span in spans)
        {
            var start = right ? Math.Max(offset, span.Utf16Start) : span.Utf16Start;
            var end = right ? span.Utf16Start + span.Utf16Length : Math.Min(offset, span.Utf16Start + span.Utf16Length);
            if (end > start)
            {
                result.Add(new(right ? start - offset : start, end - start, span.Style));
            }
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<SubtitleInlineSpan> MergeSpans(SubtitleLine first, SubtitleLine second, int offset)
    {
        var result = first.InlineSpans.ToBuilder();
        if (first.Style == second.Style)
        {
            foreach (var span in second.InlineSpans)
            {
                Append(result, span with { Utf16Start = span.Utf16Start + offset });
            }
            return result.ToImmutable();
        }
        var cursor = 0;
        foreach (var span in second.InlineSpans)
        {
            if (span.Utf16Start > cursor)
            {
                Append(result, new(offset + cursor, span.Utf16Start - cursor, SubtitleInlineStyleOverride.FromStyle(second.Style)));
            }
            Append(result, new(offset + span.Utf16Start, span.Utf16Length,
                SubtitleInlineStyleOverride.FromStyle(span.Style.ApplyTo(second.Style))));
            cursor = span.Utf16Start + span.Utf16Length;
        }
        if (cursor < second.Text.Length)
        {
            Append(result, new(offset + cursor, second.Text.Length - cursor, SubtitleInlineStyleOverride.FromStyle(second.Style)));
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<SubtitleKaraokeStyleSpan> SplitKaraokeStyleSpans(
        ImmutableArray<SubtitleKaraokeStyleSpan> spans, int offset, bool right)
    {
        var result = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        foreach (var span in spans)
        {
            var start = right ? Math.Max(offset, span.Utf16Start) : span.Utf16Start;
            var end = right ? span.Utf16Start + span.Utf16Length : Math.Min(offset, span.Utf16Start + span.Utf16Length);
            if (end > start)
            {
                SubtitleKaraokeStyleEditing.Append(result, span with
                {
                    Utf16Start = right ? start - offset : start, Utf16Length = end - start
                });
            }
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<SubtitleKaraokeStyleSpan> MergeKaraokeStyleSpans(SubtitleLine first,
        SubtitleLine second, int offset, KaraokeHighlightStyle? mergedHighlight)
    {
        var result = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        AppendKaraokeStyles(result, first, 0, mergedHighlight);
        AppendKaraokeStyles(result, second, offset, mergedHighlight);
        return result.ToImmutable();
    }

    private static void AppendKaraokeStyles(ImmutableArray<SubtitleKaraokeStyleSpan>.Builder result,
        SubtitleLine line, int offset, KaraokeHighlightStyle? mergedHighlight)
    {
        var boundaries = SubtitleTextEditMap.Boundaries(line.Text);
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var ordinary = SubtitleKaraokeStyleEditing.OrdinaryAt(line, start);
            var segment = SubtitleKaraokeStyleEditing.SegmentAt(line, start);
            var originalDefault = KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, segment);
            var mergedDefault = KaraokeVisualStyleResolver.ResolveActive(ordinary, mergedHighlight, segment);
            var inherited = VisualDifference(originalDefault, mergedDefault);
            var active = KaraokeVisualStyleResolver.RangeStyleAt(line, start, KaraokeVisualState.ACTIVE);
            active = active is null ? inherited : inherited.Merge(active);
            SubtitleKaraokeStyleEditing.Append(result, new(offset + start, boundaries[index + 1] - start,
                active, KaraokeVisualStyleResolver.RangeStyleAt(line, start, KaraokeVisualState.INACTIVE)));
        }
    }

    private static KaraokeVisualStyleOverride VisualDifference(SubtitleStyle source, SubtitleStyle target)
    {
        return new()
        {
            Fill = source.Fill != target.Fill ? source.Fill : null,
            Stroke = source.Stroke != target.Stroke ? source.Stroke : null,
            StrokeWidth = source.StrokeWidth != target.StrokeWidth ? source.StrokeWidth : null,
            FillBlur = source.FillBlur != target.FillBlur ? source.FillBlur : null,
            StrokeBlur = source.StrokeBlur != target.StrokeBlur ? source.StrokeBlur : null,
            ShadowOffset = source.ShadowOffset != target.ShadowOffset ? source.ShadowOffset : null,
            ShadowBlur = source.ShadowBlur != target.ShadowBlur ? source.ShadowBlur : null,
            ShadowColor = source.ShadowColor != target.ShadowColor ? source.ShadowColor : null
        };
    }

    private static void Append(ImmutableArray<SubtitleInlineSpan>.Builder result, SubtitleInlineSpan span)
    {
        if (result.Count > 0 && result[^1].Utf16Start + result[^1].Utf16Length == span.Utf16Start && result[^1].Style == span.Style)
        {
            result[^1] = result[^1] with { Utf16Length = result[^1].Utf16Length + span.Utf16Length };
        }
        else
        {
            result.Add(span);
        }
    }
}
