using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssTextWriter
{
    internal static AssBodyWriteResult Write(SubtitleLine line, MediaTime origin, bool projection = false,
        AssEventConversionContext? conversion = null, MediaTime? eventOrigin = null,
        AssTextAnimationExport? textAnimation = null, bool canvasUnits = false)
    {
        AssTextParser.ValidateLine(line, canvasUnits);
        var result = new StringBuilder();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        if (!projection)
        {
            SubtitleFormatLossAnalysis.AddAssKaraokeLoss(line, diagnostics);
            AssShadowComposition.AddDiagnostics(line, origin, diagnostics);
        }
        for (var index = 0; index < line.Text.Length - 1; index++)
        {
            if (line.Text[index] == '\\' && line.Text[index + 1] is 'N' or 'n' or 'h')
            {
                throw new InvalidDataException("ASS 无法无损表达字面反斜杠与 N、n 或 h 的相邻组合。");
            }
        }
        if (line.Text.IndexOfAny(['{', '}']) >= 0)
        {
            diagnostics.Add(new("Ass.LiteralBraces", "字面花括号使用 libass 扩展转义，其他 ASS 播放器可能改变文字。", SubtitleId: line.Id));
        }
        var boundaries = new SortedSet<int> { 0, line.Text.Length };
        foreach (var span in line.InlineSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var segment in line.Karaoke)
        {
            boundaries.Add(segment.Utf16Start);
            boundaries.Add(segment.Utf16Start + segment.Utf16Length);
        }
        foreach (var span in line.KaraokeStyleSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var range in line.AnimationRanges)
        {
            boundaries.Add(range.Utf16Start);
            boundaries.Add(range.Utf16Start + range.Utf16Length);
        }
        var offsets = boundaries.ToArray();
        long time = 0;
        long clipStartCount = 0;
        SubtitleStyle? previous = null;
        KaraokeSegment? previousClip = null;
        SubtitleStyle? previousInactive = null;
        SubtitleStyle? previousActive = null;
        var spanIndex = 0;
        var clipIndex = 0;
        var clockOrigin = eventOrigin ?? origin;
        for (var index = 0; index < offsets.Length - 1; index++)
        {
            var offset = offsets[index];
            while (spanIndex < line.InlineSpans.Length && line.InlineSpans[spanIndex].Utf16Start + line.InlineSpans[spanIndex].Utf16Length <= offset)
            {
                spanIndex++;
            }
            var span = spanIndex < line.InlineSpans.Length && line.InlineSpans[spanIndex].Utf16Start <= offset ? line.InlineSpans[spanIndex] : null;
            var style = span?.Style.ApplyTo(line.Style) ?? line.Style;
            style = conversion?.ApplyTypographyAnimations(style) ?? style;
            var styleChanged = style != previous;
            while (clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start + line.Karaoke[clipIndex].Utf16Length <= offset)
            {
                clipIndex++;
            }
            var clip = clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start <= offset ? line.Karaoke[clipIndex] : null;
            var nativeInactive = clip is null ? null : KaraokeVisualStyleResolver.ResolveInactive(style, clip,
                KaraokeVisualStyleResolver.StyleAt(line.KaraokeStyleSpans, offset, KaraokeVisualState.INACTIVE));
            var nativeActive = clip is null ? null : KaraokeVisualStyleResolver.ResolveActive(style, line.KaraokeStyle, clip,
                KaraokeVisualStyleResolver.StyleAt(line.KaraokeStyleSpans, offset, KaraokeVisualState.ACTIVE));
            var karaokeStyleChanged = nativeInactive != previousInactive || nativeActive != previousActive;
            if (!projection && clip is not null && clip == previousClip && karaokeStyleChanged)
            {
                diagnostics.Add(new("Ass.KaraokeStyleRuns", "ASS 同一演唱组内的样式变化可能形成独立渲染片段；已保留完整组时间和各范围标签，播放器的激活或扫色时序可能不同。", SubtitleId: line.Id));
            }
            var endCount = time;
            if (clip != previousClip)
            {
                if (clip is not null)
                {
                    var start = clip.Start - clockOrigin;
                    var end = clip.End - clockOrigin;
                    clipStartCount = AssKaraokeTiming.Quantize(start);
                    endCount = AssKaraokeTiming.Quantize(end);
                    if (endCount <= clipStartCount)
                    {
                        endCount = checked(clipStartCount + 1);
                    }
                    AssKaraokeTiming.ValidateCount(endCount);
                    AssKaraokeTiming.ValidateCount(endCount - clipStartCount);
                    if (new MediaTime(clipStartCount, 100) != start || new MediaTime(endCount, 100) != end)
                    {
                        diagnostics.Add(new("Ass.KaraokeQuantization", "卡拉 OK 起止时间已独立量化为厘秒；量化后折叠的正时长保留至少一厘秒，组间可能因此重叠。", SubtitleId: line.Id));
                    }
                }
            }
            if (styleChanged || karaokeStyleChanged || clip != previousClip || !line.AnimationRanges.IsEmpty)
            {
                CheckTagBoundary(result);
                var outputStyle = conversion?.ConvertStyle(style) ?? style;
                result.Append("{\\r").Append(StyleTags(outputStyle, projection, line.Id, diagnostics)).Append(conversion?.GeometryTags).Append('}');
                AddStyleDiagnostics(outputStyle, line.Id, diagnostics, projection);
                previous = style;
                if (clip is not null)
                {
                    var inactive = nativeInactive!;
                    var active = nativeActive!;
                    inactive = conversion?.ConvertStyle(inactive) ?? inactive;
                    active = conversion?.ConvertStyle(active) ?? active;
                    AddStyleDiagnostics(inactive, line.Id, diagnostics, projection);
                    AddStyleDiagnostics(active, line.Id, diagnostics, projection);
                    result.Append("{\\2c").Append(AssFormatValues.Color(inactive.Fill, false)).Append("\\2a").Append(AssFormatValues.Alpha(inactive.Fill));
                    result.Append("\\1c").Append(AssFormatValues.Color(active.Fill, false)).Append("\\1a").Append(AssFormatValues.Alpha(active.Fill));
                    if (clip.HighlightKind == KaraokeHighlightKind.SWEEP)
                    {
                        result.Append(VisualTags(inactive, projection, line.Id, diagnostics));
                        if (VisualsDiffer(inactive, active))
                        {
                            diagnostics.Add(new("Ass.KaraokeVisual", "ASS 的逐字扫过不能完整保留前后的独立描边和阴影，已采用未激活外观。", SubtitleId: line.Id));
                        }
                    }
                    else if (clipStartCount == 0)
                    {
                        result.Append(VisualTags(active, projection, line.Id, diagnostics));
                        var representedInactive = clip.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP ? active with { StrokeWidth = 0 } : active;
                        if (VisualsDiffer(inactive, representedInactive))
                        {
                            diagnostics.Add(new("Ass.KaraokeVisual", "演唱组在 ASS 对白起点已激活，采用激活外观；零起点瞬时变换在 ASS 中表示整行渐变，无法保存隐藏的未激活边缘和阴影。", SubtitleId: line.Id));
                        }
                    }
                    else
                    {
                        result.Append(VisualTags(inactive, projection, line.Id, diagnostics));
                        var changes = ChangedVisualTags(inactive, active, projection, line.Id, diagnostics);
                        if (changes.Length > 0)
                        {
                            var startMs = checked(clipStartCount * 10).ToString(CultureInfo.InvariantCulture);
                            result.Append("\\t(").Append(startMs).Append(',').Append(startMs).Append(',').Append(changes).Append(')');
                        }
                    }
                    result.Append('}');
                }
                else
                {
                    result.Append("{\\2c").Append(AssFormatValues.Color(style.Fill, false)).Append("\\2a").Append(AssFormatValues.Alpha(style.Fill)).Append('}');
                }
                var animationTags = conversion?.AnimationTags(style, eventOrigin ?? origin);
                animationTags += conversion?.TextAnimationTags(offset, style, clip is not null, eventOrigin ?? origin);
                animationTags += textAnimation?.Write(offset, style, clip is not null, eventOrigin ?? origin, new(1, 1), 0);
                if (!string.IsNullOrEmpty(animationTags))
                {
                    result.Append('{').Append(animationTags).Append('}');
                }
            }
            if (clip != previousClip)
            {
                if (clip is not null)
                {
                    if (clipStartCount < time || clipStartCount - time > AssKaraokeTiming.MAX_CENTISECONDS)
                    {
                        result.Append("{\\kt").Append(clipStartCount.ToString(CultureInfo.InvariantCulture)).Append('}');
                        diagnostics.Add(new("Ass.KaraokeClockCompatibility", "重叠、倒序或负相对时间的演唱组使用 kt 保留独立起点；部分 ASS 工具和播放器可能不支持此标签。", SubtitleId: line.Id));
                    }
                    else if (clipStartCount > time)
                    {
                        result.Append("{\\k").Append((clipStartCount - time).ToString(CultureInfo.InvariantCulture)).Append('}');
                    }
                    var tag = clip.HighlightKind == KaraokeHighlightKind.STEP ? "k" : clip.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP ? "ko" : "kf";
                    result.Append("{\\").Append(tag).Append((endCount - clipStartCount).ToString(CultureInfo.InvariantCulture)).Append('}');
                    time = endCount;
                }
                else
                {
                    result.Append("{\\k0}");
                }
                previousClip = clip;
            }
            previousInactive = nativeInactive;
            previousActive = nativeActive;
            result.Append(Escape(line.Text[offset..offsets[index + 1]]));
            if (OutOfGamut(style.Fill) || OutOfGamut(style.Stroke) || OutOfGamut(style.ShadowColor))
            {
                diagnostics.Add(new("Ass.ColorRange", "ASS 8 位 sRGB 颜色会限制线性 HDR 或负颜色。", SubtitleId: line.Id));
            }
        }
        return new(result.ToString(), diagnostics.Distinct().ToImmutableArray(), new(time, 100));
    }

    private static string StyleTags(SubtitleStyle style, bool projection, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (style.FontFamily.IndexOfAny(['\\', '{', '}', '\r', '\n']) >= 0)
        {
            throw new InvalidDataException("ASS 字体名包含标签控制字符。");
        }
        var weight = !projection && !style.FontAssetId.HasValue && style.FontVariant is { Weight: >= 100 and <= 1000 } variant
            ? variant.Weight : style.Bold ? 1 : 0;
        var italic = style.Italic || !projection && !style.FontAssetId.HasValue && style.FontVariant is { Italic: true };
        return string.Create(CultureInfo.InvariantCulture,
            $"\\fn{style.FontFamily}\\fs{AssFormatValues.Number(style.FontSize)}\\fsp{AssFormatValues.Number(style.LetterSpacing)}\\b{weight}\\i{(italic ? 1 : 0)}\\u{(style.Underline ? 1 : 0)}\\s{(style.Strikethrough ? 1 : 0)}\\1c{AssFormatValues.Color(style.Fill, false)}\\1a{AssFormatValues.Alpha(style.Fill)}\\3c{AssFormatValues.Color(style.Stroke, false)}\\3a{AssFormatValues.Alpha(style.Stroke)}\\4c{AssFormatValues.Color(style.ShadowColor, false)}\\4a{AssFormatValues.Alpha(style.ShadowColor)}\\bord{AssFormatValues.Number(style.StrokeWidth)}\\xshad{AssFormatValues.Number(style.ShadowOffset.X)}\\yshad{AssFormatValues.Number(style.ShadowOffset.Y)}") + (projection ? string.Empty : style.WrapMode == SubtitleWrapMode.NO_WRAP ? "\\q2" : "\\q1") + "\\blur" + BlurValue(style, projection, id, diagnostics);
    }

    private static bool OutOfGamut(SceneColor color) => color.Red is < 0 or > 1 || color.Green is < 0 or > 1 || color.Blue is < 0 or > 1;

    private static string VisualTags(SubtitleStyle style, bool projection, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"\\3c{AssFormatValues.Color(style.Stroke, false)}\\3a{AssFormatValues.Alpha(style.Stroke)}\\4c{AssFormatValues.Color(style.ShadowColor, false)}\\4a{AssFormatValues.Alpha(style.ShadowColor)}\\bord{AssFormatValues.Number(style.StrokeWidth)}\\xshad{AssFormatValues.Number(style.ShadowOffset.X)}\\yshad{AssFormatValues.Number(style.ShadowOffset.Y)}") + "\\blur" + BlurValue(style, projection, id, diagnostics);
    }

    private static string ChangedVisualTags(SubtitleStyle inactive, SubtitleStyle active, bool projection, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var tags = new StringBuilder();
        if (inactive.Stroke != active.Stroke)
        {
            tags.Append("\\3c").Append(AssFormatValues.Color(active.Stroke, false)).Append("\\3a").Append(AssFormatValues.Alpha(active.Stroke));
        }
        if (!inactive.StrokeWidth.Equals(active.StrokeWidth))
        {
            tags.Append("\\bord").Append(AssFormatValues.Number(active.StrokeWidth));
        }
        if (inactive.ShadowColor != active.ShadowColor)
        {
            tags.Append("\\4c").Append(AssFormatValues.Color(active.ShadowColor, false)).Append("\\4a").Append(AssFormatValues.Alpha(active.ShadowColor));
        }
        if (!inactive.ShadowOffset.X.Equals(active.ShadowOffset.X))
        {
            tags.Append("\\xshad").Append(AssFormatValues.Number(active.ShadowOffset.X));
        }
        if (!inactive.ShadowOffset.Y.Equals(active.ShadowOffset.Y))
        {
            tags.Append("\\yshad").Append(AssFormatValues.Number(active.ShadowOffset.Y));
        }
        if (!AssBlurConversion.Value(inactive, projection).Equals(AssBlurConversion.Value(active, projection)))
        {
            tags.Append("\\blur").Append(BlurValue(active, projection, id, diagnostics));
        }
        return tags.ToString();
    }

    private static string BlurValue(SubtitleStyle style, bool projection, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var amount = AssBlurConversion.Value(style, projection);
        if (!projection)
        {
            AssExportPrecision.AddBlurRange(amount, id, diagnostics);
        }
        return AssFormatValues.Number(amount);
    }

    private static bool VisualsDiffer(SubtitleStyle inactive, SubtitleStyle active)
    {
        var strokeVisible = inactive.StrokeWidth > 0 && inactive.Stroke.Alpha > 0 || active.StrokeWidth > 0 && active.Stroke.Alpha > 0;
        var shadowVisible = inactive.ShadowColor.Alpha > 0 || active.ShadowColor.Alpha > 0;
        return strokeVisible && (inactive.Stroke != active.Stroke || !inactive.StrokeWidth.Equals(active.StrokeWidth) || !inactive.StrokeBlur.Equals(active.StrokeBlur)) ||
            !inactive.FillBlur.Equals(active.FillBlur) ||
            shadowVisible && (inactive.ShadowColor != active.ShadowColor || inactive.ShadowOffset != active.ShadowOffset || !inactive.ShadowBlur.Equals(active.ShadowBlur));
    }

    private static void AddStyleDiagnostics(SubtitleStyle style, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool projection)
    {
        if (!projection)
        {
            AssExportPrecision.AddStyle(style, id, diagnostics);
            if (style.WrapMode == SubtitleWrapMode.GRAPHEME)
            {
                diagnostics.Add(new("Ass.WrapMode", "ASS 不支持项目的逐字素自动换行，已改为自然换行，断行位置可能改变。", SubtitleId: id));
            }
            if (style.StrokeWidth > 0 && style.FillBlur > 0)
            {
                diagnostics.Add(new("Ass.FillBlur", "ASS 有描边时只模糊描边及阴影，无法保留项目的独立填充模糊，已省略填充模糊。", SubtitleId: id));
            }
            if (style.StrokeWidth <= 0 && style.StrokeBlur > 0)
            {
                diagnostics.Add(new("Ass.StrokeBlur", "ASS 无描边时的模糊作用于填充，无法保留项目的独立描边模糊数值。", SubtitleId: id));
            }
        }
        if (!projection && style.FontVariant is not null)
        {
            diagnostics.Add(new("Ass.FontVariant", "ASS 的字体家族、字重及斜体标签不能完整保留命名变体的名称、PostScript 身份和设计宽度；播放器可能选择其他字形或合成粗体、斜体。", SubtitleId: id));
            if (!style.FontAssetId.HasValue && style.FontVariant is { Weight: < 100 })
            {
                diagnostics.Add(new("Ass.FontWeight", "原生字体变体的字重低于 ASS 数值字重交换范围 100 至 1000，已使用普通或粗体标签。", SubtitleId: id));
            }
        }
        if (OutOfGamut(style.Fill) || OutOfGamut(style.Stroke) || OutOfGamut(style.ShadowColor))
        {
            diagnostics.Add(new("Ass.ColorRange", "ASS 8 位 sRGB 颜色会限制线性 HDR 或负颜色。", SubtitleId: id));
        }
        if (projection && style.ShadowBlur > 0 && (style.ShadowColor.Alpha > 0 || style.Fill.Alpha > 0 ||
                style.StrokeWidth > 0 && style.Stroke.Alpha > 0))
        {
            diagnostics.Add(new("Ass.ShadowBlur", "项目的阴影模糊与 ASS 的文字边缘模糊语义不同，保留数值会改变文字或描边边缘。", SubtitleId: id));
        }
        if (!projection && style.ShadowColor.Alpha > 0 && !style.ShadowBlur.Equals(AssBlurConversion.Sigma(style)))
        {
            diagnostics.Add(new("Ass.ShadowBlur", "ASS 阴影模糊与当前填充或描边模糊共用数值，无法独立保留项目的阴影模糊，已采用文字通道的模糊。", SubtitleId: id));
        }
    }

    private static void CheckTagBoundary(StringBuilder source)
    {
        if (source.Length > 0 && source[^1] == '\\')
        {
            throw new InvalidDataException("ASS 字面反斜杠紧邻样式或卡拉 OK 标签，无法无损导出。");
        }
    }

    private static string Escape(string text) => text.Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\r\n", "\\N", StringComparison.Ordinal).Replace("\r", "\\N", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal).Replace("\u00a0", "\\h", StringComparison.Ordinal);
}
