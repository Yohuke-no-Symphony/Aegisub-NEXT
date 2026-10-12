using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleContentEditing
{
    private static readonly SubtitleInlineStyleOverride emptyStyle = new();

    internal static SubtitleLine ReplaceText(SubtitleLine line, int start, int length, string replacement)
    {
        var map = new SubtitleTextEditMap(line.Text, start, length, replacement);
        if (map.Text == line.Text)
        {
            return line;
        }
        var inlineSpans = line.InlineSpans;
        if (!inlineSpans.IsEmpty)
        {
            var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
            for (var index = 0; index < map.NewBoundaries.Length - 1; index++)
            {
                var offset = map.NewBoundaries[index];
                AppendSpan(spans, offset, map.NewBoundaries[index + 1] - offset,
                    StyleAt(line.InlineSpans, map.StyleSourceOffset(offset)));
            }
            inlineSpans = Reuse(line.InlineSpans, spans.ToImmutable());
        }
        var (karaoke, inactiveKaraoke) = SubtitleKaraokeRemapping.Remap(line, map);
        return line with
        {
            Text = map.Text,
            InlineSpans = inlineSpans,
            AnimationRanges = SubtitleAnimationRangeEditing.Remap(line.AnimationRanges, map),
            KaraokeStyleSpans = SubtitleKaraokeStyleEditing.Remap(line.KaraokeStyleSpans, map),
            Karaoke = karaoke,
            InactiveKaraoke = inactiveKaraoke
        };
    }

    internal static SubtitleLine ApplyInlineStyle(SubtitleLine line, int start, int length,
        SubtitleInlineStyleOverride? overlay)
    {
        var boundaries = SubtitleTextEditMap.Boundaries(line.Text);
        SubtitleTextEditMap.ValidateRange(line.Text, boundaries, start, length);
        if (length == 0 || overlay is { HasOverrides: false })
        {
            return line;
        }
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var style = StyleAt(line.InlineSpans, offset);
            if (offset >= start && offset < start + length)
            {
                style = overlay is null ? emptyStyle : style.Merge(overlay);
            }
            AppendSpan(spans, offset, boundaries[index + 1] - offset, style);
        }
        var next = Reuse(line.InlineSpans, spans.ToImmutable());
        return next == line.InlineSpans ? line : line with { InlineSpans = next };
    }

    private static SubtitleInlineStyleOverride StyleAt(ImmutableArray<SubtitleInlineSpan> spans, int offset)
    {
        var low = 0;
        var high = spans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = spans[middle];
            if (offset < span.Utf16Start)
            {
                high = middle - 1;
            }
            else if (offset >= span.Utf16Start + span.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return span.Style;
            }
        }
        return emptyStyle;
    }

    private static void AppendSpan(ImmutableArray<SubtitleInlineSpan>.Builder spans, int start, int length,
        SubtitleInlineStyleOverride style)
    {
        if (!style.HasOverrides)
        {
            return;
        }
        if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == start && spans[^1].Style == style)
        {
            spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
        }
        else
        {
            spans.Add(new(start, length, style));
        }
    }

    private static ImmutableArray<T> Reuse<T>(ImmutableArray<T> original, ImmutableArray<T> next)
    {
        return original.SequenceEqual(next) ? original : next;
    }
}
