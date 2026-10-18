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
        AssTextAnimationExport? textAnimation = null, bool canvasUnits = false, AssExportStyle? exportStyle = null)
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
                throw new InvalidDataException("ASS cannot losslessly represent a literal backslash immediately followed by N, n, or h.");
            }
        }
        if (line.Text.IndexOfAny(['{', '}']) >= 0)
        {
            diagnostics.Add(new("Ass.LiteralBraces", "Literal braces use libass extension escaping; other ASS players may alter the text.", SubtitleId: line.Id));
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
                diagnostics.Add(new("Ass.KaraokeStyleRuns", "Style changes within the same ASS karaoke group may create separate rendering segments. Full group timing and the tags for each range were preserved; player activation or sweep timing may differ.", SubtitleId: line.Id));
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
                        diagnostics.Add(new("Ass.KaraokeQuantization", "Karaoke start and end times were quantized independently to centiseconds. Positive durations that collapse after quantization retain at least one centisecond, which may cause groups to overlap.", SubtitleId: line.Id));
                    }
                }
            }
            if (styleChanged || karaokeStyleChanged || clip != previousClip || !line.AnimationRanges.IsEmpty)
            {
                var sharedVisual = conversion?.SharedVisualProperties(offset, clip is not null) ??
                    textAnimation?.SharedVisualProperties(offset, clip is not null) ?? ImmutableHashSet<AnimationProperty>.Empty;
                CheckTagBoundary(result);
                var outputStyle = conversion?.ConvertStyle(style) ?? style;
                var delta = !projection && exportStyle is not null ? new AssStyleDeltaWriter(exportStyle) : null;
                if (delta is not null)
                {
                    ValidateFontFamily(outputStyle);
                }
                var staticTags = delta?.StyleTags(outputStyle, BlurValue(outputStyle, projection, line.Id, diagnostics),
                    clip is null, clip is null) ?? StyleTags(outputStyle, projection, line.Id, diagnostics);
                AppendTags(result, (delta is null || index > 0 ? "\\r" : string.Empty) + staticTags + conversion?.GeometryTags);
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
                    var visualTags = new StringBuilder();
                    visualTags.Append(delta?.ColorTags("2", inactive.Fill) ??
                        "\\2c" + AssFormatValues.Color(inactive.Fill, false) + "\\2a" + AssFormatValues.Alpha(inactive.Fill));
                    visualTags.Append(delta?.ColorTags("1", active.Fill) ??
                        "\\1c" + AssFormatValues.Color(active.Fill, false) + "\\1a" + AssFormatValues.Alpha(active.Fill));
                    if (clip.HighlightKind == KaraokeHighlightKind.SWEEP)
                    {
                        visualTags.Append(delta?.VisualTags(inactive, BlurValue(inactive, projection, line.Id, diagnostics)) ??
                            VisualTags(inactive, projection, line.Id, diagnostics));
                        if (VisualsDiffer(inactive, active))
                        {
                            diagnostics.Add(new("Ass.KaraokeVisual", "ASS per-word sweeps cannot fully preserve independent stroke and shadow before and after activation. The inactive appearance was used.", SubtitleId: line.Id));
                        }
                    }
                    else if (clipStartCount == 0)
                    {
                        visualTags.Append(delta?.VisualTags(active, BlurValue(active, projection, line.Id, diagnostics)) ??
                            VisualTags(active, projection, line.Id, diagnostics));
                        var representedInactive = clip.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP ? active with { StrokeWidth = 0 } : active;
                        if (VisualsDiffer(inactive, representedInactive))
                        {
                            diagnostics.Add(new("Ass.KaraokeVisual", "The karaoke group is already active at the ASS dialogue start, so the active appearance was used. An instantaneous transform at time zero represents a whole-line transition in ASS and cannot preserve hidden inactive edges and shadows.", SubtitleId: line.Id));
                        }
                    }
                    else
                    {
                        visualTags.Append(delta?.VisualTags(inactive, BlurValue(inactive, projection, line.Id, diagnostics)) ??
                            VisualTags(inactive, projection, line.Id, diagnostics));
                        var changes = ChangedVisualTags(inactive, active, projection, line.Id, diagnostics, sharedVisual);
                        if (changes.Length > 0)
                        {
                            var startMs = checked(clipStartCount * 10).ToString(CultureInfo.InvariantCulture);
                            visualTags.Append("\\t(").Append(startMs).Append(',').Append(startMs).Append(',').Append(changes).Append(')');
                        }
                    }
                    AppendTags(result, visualTags.ToString());
                }
                else
                {
                    AppendTags(result, delta?.ColorTags("2", outputStyle.Fill) ??
                        "\\2c" + AssFormatValues.Color(style.Fill, false) + "\\2a" + AssFormatValues.Alpha(style.Fill));
                }
                var animationTags = conversion?.AnimationTags(style, eventOrigin ?? origin, sharedVisual);
                animationTags += conversion?.TextAnimationTags(offset, style, clip is not null, eventOrigin ?? origin, nativeInactive);
                animationTags += textAnimation?.Write(offset, style, clip is not null, eventOrigin ?? origin, new(1, 1), 0, nativeInactive);
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
                        diagnostics.Add(new("Ass.KaraokeClockCompatibility", "Karaoke groups with overlapping, reversed, or negative relative times use kt to preserve independent start times. Some ASS tools and players may not support this tag.", SubtitleId: line.Id));
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
                diagnostics.Add(new("Ass.ColorRange", "ASS 8-bit sRGB colors limit linear HDR or negative colors.", SubtitleId: line.Id));
            }
        }
        return new(result.ToString(), diagnostics.Distinct().ToImmutableArray(), new(time, 100));
    }

    private static string StyleTags(SubtitleStyle style, bool projection, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        ValidateFontFamily(style);
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
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, ImmutableHashSet<AnimationProperty> sharedVisual)
    {
        var tags = new StringBuilder();
        if (!sharedVisual.Contains(AnimationProperty.STROKE) && inactive.Stroke != active.Stroke)
        {
            tags.Append("\\3c").Append(AssFormatValues.Color(active.Stroke, false)).Append("\\3a").Append(AssFormatValues.Alpha(active.Stroke));
        }
        if (!sharedVisual.Contains(AnimationProperty.STROKE_WIDTH) && !inactive.StrokeWidth.Equals(active.StrokeWidth))
        {
            tags.Append("\\bord").Append(AssFormatValues.Number(active.StrokeWidth));
        }
        if (!sharedVisual.Contains(AnimationProperty.SHADOW_COLOR) && inactive.ShadowColor != active.ShadowColor)
        {
            tags.Append("\\4c").Append(AssFormatValues.Color(active.ShadowColor, false)).Append("\\4a").Append(AssFormatValues.Alpha(active.ShadowColor));
        }
        if (!sharedVisual.Contains(AnimationProperty.SHADOW_OFFSET) && !inactive.ShadowOffset.X.Equals(active.ShadowOffset.X))
        {
            tags.Append("\\xshad").Append(AssFormatValues.Number(active.ShadowOffset.X));
        }
        if (!sharedVisual.Contains(AnimationProperty.SHADOW_OFFSET) && !inactive.ShadowOffset.Y.Equals(active.ShadowOffset.Y))
        {
            tags.Append("\\yshad").Append(AssFormatValues.Number(active.ShadowOffset.Y));
        }
        if (!sharedVisual.Contains(AnimationProperty.FILL_BLUR) && !sharedVisual.Contains(AnimationProperty.STROKE_BLUR) &&
            !sharedVisual.Contains(AnimationProperty.SHADOW_BLUR) && !AssBlurConversion.Value(inactive, projection).Equals(AssBlurConversion.Value(active, projection)))
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
            if (style.FontAssetId.HasValue)
            {
                diagnostics.Add(new("Ass.FontResource", "ASS files do not include project-embedded fonts. Install the corresponding fonts in the playback environment.", SubtitleId: id));
            }
            if (style.WrapMode == SubtitleWrapMode.GRAPHEME)
            {
                diagnostics.Add(new("Ass.WrapMode", "ASS does not support project automatic wrapping by grapheme. Natural wrapping was used; line breaks may change.", SubtitleId: id));
            }
            if (style.StrokeWidth > 0 && style.FillBlur > 0)
            {
                diagnostics.Add(new("Ass.FillBlur", "When a stroke exists, ASS only blurs stroke and shadow and cannot preserve independent project fill blur. Fill blur was omitted.", SubtitleId: id));
            }
            if (style.StrokeWidth <= 0 && style.StrokeBlur > 0)
            {
                diagnostics.Add(new("Ass.StrokeBlur", "ASS blur without a stroke affects the fill and cannot preserve independent project stroke blur values.", SubtitleId: id));
            }
        }
        if (!projection && style.FontVariant is not null)
        {
            diagnostics.Add(new("Ass.FontVariant", "ASS font family, weight, and italic tags cannot fully preserve the name, PostScript identity, or design width of a named variant. Players may select different glyphs or synthesize bold or italic.", SubtitleId: id));
            if (!style.FontAssetId.HasValue && style.FontVariant is { Weight: < 100 })
            {
                diagnostics.Add(new("Ass.FontWeight", "The weight of the native font variant is below the ASS numeric weight exchange range of 100 to 1000. Regular or bold tags were used.", SubtitleId: id));
            }
        }
        if (OutOfGamut(style.Fill) || OutOfGamut(style.Stroke) || OutOfGamut(style.ShadowColor))
        {
            diagnostics.Add(new("Ass.ColorRange", "ASS 8-bit sRGB colors limit linear HDR or negative colors.", SubtitleId: id));
        }
        if (projection && style.ShadowBlur > 0 && (style.ShadowColor.Alpha > 0 || style.Fill.Alpha > 0 ||
                style.StrokeWidth > 0 && style.Stroke.Alpha > 0))
        {
            diagnostics.Add(new("Ass.ShadowBlur", "Project shadow blur differs in meaning from ASS text-edge blur. Preserving the value changes text or stroke edges.", SubtitleId: id));
        }
        if (!projection && style.ShadowColor.Alpha > 0 && !style.ShadowBlur.Equals(AssBlurConversion.Sigma(style)))
        {
            diagnostics.Add(new("Ass.ShadowBlur", "ASS shadow blur shares its value with the current fill or stroke blur and cannot independently preserve project shadow blur. Text-channel blur was used.", SubtitleId: id));
        }
    }

    private static void CheckTagBoundary(StringBuilder source)
    {
        if (source.Length > 0 && source[^1] == '\\')
        {
            throw new InvalidDataException("An ASS literal backslash is adjacent to a style or karaoke tag and cannot be exported losslessly.");
        }
    }

    private static void ValidateFontFamily(SubtitleStyle style)
    {
        if (style.FontFamily.IndexOfAny(['\\', '{', '}', '\r', '\n']) >= 0)
        {
            throw new InvalidDataException("ASS font names contain tag control characters.");
        }
    }

    private static void AppendTags(StringBuilder output, string tags)
    {
        if (tags.Length > 0)
        {
            output.Append('{').Append(tags).Append('}');
        }
    }

    private static string Escape(string text) => text.Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\r\n", "\\N", StringComparison.Ordinal).Replace("\r", "\\N", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal).Replace("\u00a0", "\\h", StringComparison.Ordinal);
}
