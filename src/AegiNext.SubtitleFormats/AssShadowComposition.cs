using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssShadowComposition
{
    internal static void AddDiagnostics(SubtitleLine line, MediaTime visibleStart,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var boundaries = new SortedSet<int> { 0, line.Text.Length };
        foreach (var span in line.InlineSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var span in line.KaraokeStyleSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var group in line.Karaoke)
        {
            boundaries.Add(group.Utf16Start);
            boundaries.Add(group.Utf16Start + group.Utf16Length);
        }
        var visibleEnd = visibleStart + line.End - line.Start;
        var offsets = boundaries.ToArray();
        var inlineIndex = 0;
        var clipIndex = 0;
        for (var index = 0; index < offsets.Length - 1; index++)
        {
            var offset = offsets[index];
            while (inlineIndex < line.InlineSpans.Length &&
                   line.InlineSpans[inlineIndex].Utf16Start + line.InlineSpans[inlineIndex].Utf16Length <= offset)
            {
                inlineIndex++;
            }
            var ordinary = inlineIndex < line.InlineSpans.Length && line.InlineSpans[inlineIndex].Utf16Start <= offset
                ? line.InlineSpans[inlineIndex].Style.ApplyTo(line.Style) : line.Style;
            while (clipIndex < line.Karaoke.Length &&
                   line.Karaoke[clipIndex].Utf16Start + line.Karaoke[clipIndex].Utf16Length <= offset)
            {
                clipIndex++;
            }
            var clip = clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start <= offset
                ? line.Karaoke[clipIndex] : null;
            if (clip is null)
            {
                if (Differs(ordinary))
                {
                    Report(line.Id, diagnostics);
                    return;
                }
                continue;
            }
            var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, clip,
                KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.ACTIVE));
            var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, clip,
                KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE));
            var inactiveEnd = clip.HighlightKind == KaraokeHighlightKind.SWEEP ? clip.End : clip.Start;
            if (clip.Start < visibleEnd && Differs(active) || visibleStart < inactiveEnd && Differs(inactive))
            {
                Report(line.Id, diagnostics);
                return;
            }
        }
    }

    private static bool Differs(SubtitleStyle style)
    {
        if (style.ShadowColor.Alpha <= 0)
        {
            return false;
        }
        return style.ShadowOffset == default(ScenePoint)
            ? style.ShadowBlur == 0
            : style.StrokeWidth > 0;
    }

    private static void Report(Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        diagnostics.Add(new("Ass.ShadowComposition",
            "ASS 阴影包含描边轮廓，原生阴影由填充字形生成；零位移且无模糊的 ASS 阴影不绘制，原生仍参与合成。当前外观条件可能改变阴影边界或透明填充的合成结果，参数已保留但画面不能无损互换。",
            SubtitleId: id));
    }
}
