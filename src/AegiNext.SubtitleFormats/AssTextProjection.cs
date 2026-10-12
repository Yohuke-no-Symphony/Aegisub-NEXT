using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>高级 ASS 标签页的临时投影；不保存量化来源，不量化未编辑的工程时间或外观。</summary>
public sealed record AssTextProjection(string Source, ImmutableArray<AssSourceMapEntry> SourceMap,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics)
{
    /// <summary>判断动画轨道是否属于高级 ASS 文字投影编辑的范围，供提交时替换文字动画并保留其他轨道。</summary>
    public static bool IsTextAnimationTrack(AnimationTrack track)
    {
        return AssTextAnimationProjection.IsTextTrack(track);
    }

    /// <summary>生成不含位置标签的工程来源与文字映射；时间相对内容原点零，保留 contentOrigin 可见窗口外的旧时间。</summary>
    public static AssTextProjection Create(SubtitleLine line, MediaTime? contentOrigin = null, int canvasWidth = 1920, int canvasHeight = 1080, ProjectLayer? layer = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        var animationDiagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var animation = layer is null ? null : new AssTextAnimationExport(line,
            layer with { Tracks = layer.Tracks.Where(AssTextAnimationProjection.IsTextTrack).ToImmutableArray() }, animationDiagnostics, true);
        var written = AssTextWriter.Write(line, MediaTime.Zero, true, textAnimation: animation);
        var diagnostics = written.Diagnostics.ToBuilder();
        diagnostics.AddRange(animationDiagnostics);
        var mask = layer is null ? string.Empty : AssMaskWriter.WriteTags(layer, MediaTime.Zero, diagnostics);
        if (mask is null)
        {
            mask = AssMaskWriter.StaticTags(layer!.Mask!, line.Id, diagnostics);
            diagnostics.Add(new("Ass.NativeMaskAnimation", "高级 ASS 代码显示蒙版基础几何；不能原生表达的蒙版动画在裁切标签未改写时保留。", SubtitleId: line.Id));
        }
        var source = "{\\an" + AssFormatValues.Alignment(line.Style.Alignment).ToString(CultureInfo.InvariantCulture) + mask + "}" + written.Text;
        var parsed = Parser(line, canvasWidth, canvasHeight).Parse(source);
        return new(source, parsed.SourceMap, diagnostics.ToImmutable());
    }

    /// <summary>解析工程来源，保留原行身份、句时间与原生位置；未改写的字时间、字体资源及精确颜色保持原值。</summary>
    public static AssTextEditResult Apply(SubtitleLine original, string source, MediaTime? contentOrigin = null, int canvasWidth = 1920, int canvasHeight = 1080, ProjectLayer? layer = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        var projection = Create(original, contentOrigin, canvasWidth, canvasHeight, layer);
        if (source == projection.Source)
        {
            return new(original, [], projection.SourceMap)
            { Mask = layer?.Mask, MaskTracks = MaskTracks(layer) };
        }
        var baselineResult = Parser(original, canvasWidth, canvasHeight).Parse(projection.Source);
        var baseline = baselineResult.Line;
        var parsed = Parser(original, canvasWidth, canvasHeight).Parse(source);
        var line = parsed.Line with
        {
            Style = parsed.Line.Style with
            {
                Alignment = parsed.Line.Style.Alignment == baseline.Style.Alignment ? original.Style.Alignment : parsed.Line.Style.Alignment,
                TextAlign = parsed.Line.Style.Alignment == baseline.Style.Alignment ? original.Style.TextAlign : null,
                Position = original.Style.Position
            }
        };
        line = line with { InlineSpans = RestorePrecision(original, baseline, line) };
        var previousIndices = MatchClipSources(projection.Source, baselineResult, source, parsed);
        var clips = line.Karaoke.Select((clip, index) => RestoreClip(original, baseline, clip, previousIndices[index])).ToImmutableArray();
        var inactiveKaraoke = RemapInactiveKaraoke(original, line.Text, clips);
        line = line with
        {
            Karaoke = original.Karaoke.SequenceEqual(clips) ? original.Karaoke : clips,
            InactiveKaraoke = original.InactiveKaraoke.SequenceEqual(inactiveKaraoke) ? original.InactiveKaraoke : inactiveKaraoke,
            KaraokeStyleSpans = RestoreKaraokeStylePrecision(original, baseline, parsed.Line),
            KaraokeStyle = original.KaraokeStyle
        };
        var animation = AssTextAnimationProjection.Restore(original, baselineResult, parsed, line, layer);
        line = animation.Line;
        AssTextParser.ValidateLine(line);
        var unchangedMask = AssOverrideTags.MaskIdentity(projection.Source) == AssOverrideTags.MaskIdentity(source);
        var mask = unchangedMask ? layer?.Mask : PreserveMaskIdentity(layer?.Mask, parsed.Mask);
        if (!unchangedMask && layer is not null && ClipMaskAnimation.IsTopologyLocked(layer) && !ClipMaskAnimation.HasSameTopology(layer.Mask, mask))
        {
            throw new InvalidOperationException("存在节点形变动画时不能从高级代码改变蒙版拓扑；请先清除节点形变轨道。");
        }
        var parsedMaskTracks = unchangedMask || layer?.Mask is null ? parsed.MaskTracks : RebaseMaskTracks(parsed.MaskTracks, mask, layer.Mask.Transform);
        var maskTracks = unchangedMask ? MaskTracks(layer) : mask is null ? [] : parsedMaskTracks.AddRange(MaskTracks(layer)
            .Where(track => track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)));
        var diagnostics = RestoreProjectionDiagnostics(original, line, projection.Source, baselineResult, source, parsed);
        return new(line == original ? original : line, diagnostics, parsed.SourceMap)
        {
            Mask = mask, MaskTracks = maskTracks, TextAnimationTracks = animation.Tracks
        };
    }

    private static ImmutableArray<SubtitleFormatDiagnostic> RestoreProjectionDiagnostics(SubtitleLine original, SubtitleLine restored,
        string baselineSource, AssTextEditResult baseline, string source, AssTextEditResult parsed)
    {
        var unchangedBlurTags = baseline.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.ShadowBlur" && diagnostic.SourceLength > 0)
            .Select(diagnostic => baselineSource.Substring(diagnostic.SourceStart, diagnostic.SourceLength)).ToHashSet(StringComparer.Ordinal);
        if (unchangedBlurTags.Count == 0)
        {
            return parsed.Diagnostics;
        }
        var textMap = original.Text == restored.Text ? null : MapTextChange(original.Text, restored.Text);
        return parsed.Diagnostics.Where(diagnostic =>
        {
            if (diagnostic.Code != "Ass.ShadowBlur" || diagnostic.SourceLength <= 0 ||
                !unchangedBlurTags.Contains(source.Substring(diagnostic.SourceStart, diagnostic.SourceLength)))
            {
                return true;
            }
            var mapping = parsed.SourceMap.FirstOrDefault(entry => entry.Utf16Length == 0 && entry.SourceStart <= diagnostic.SourceStart &&
                entry.SourceStart + entry.SourceLength >= diagnostic.SourceStart + diagnostic.SourceLength);
            if (mapping is null)
            {
                return true;
            }
            var offset = mapping.Utf16Start;
            var originalOffset = textMap?.StyleSourceOffset(offset) ?? offset;
            return !StyleAt(original, originalOffset).ShadowBlur.Equals(StyleAt(restored, offset).ShadowBlur);
        }).ToImmutableArray();
    }

    private static ImmutableArray<AnimationTrack> MaskTracks(ProjectLayer? layer)
    {
        if (layer is null)
        {
            return [];
        }
        return layer.Tracks.All(track => AnimationPropertyMetadata.IsMaskProperty(track.Property)) ? layer.Tracks :
            layer.Tracks.Where(track => AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToImmutableArray();
    }

    private static ClipMask? PreserveMaskIdentity(ClipMask? original, ClipMask? parsed)
    {
        if (original is not null && parsed is not null)
        {
            parsed = AssMaskGeometry.Rebase(parsed, original.Transform);
        }
        if (original is not VectorClipMask previous || parsed is not VectorClipMask next || previous.Contours.Length != next.Contours.Length)
        {
            return parsed;
        }
        if (previous.Contours.Where((contour, index) => contour.Nodes.Length != next.Contours[index].Nodes.Length).Any())
        {
            return parsed;
        }
        return next with
        {
            Contours = next.Contours.Select((contour, index) => contour with
            {
                Id = previous.Contours[index].Id,
                Nodes = contour.Nodes.Select((node, nodeIndex) => node with { Id = previous.Contours[index].Nodes[nodeIndex].Id }).ToImmutableArray()
            }).ToImmutableArray()
        };
    }

    private static ImmutableArray<AnimationTrack> RebaseMaskTracks(ImmutableArray<AnimationTrack> tracks, ClipMask? mask, MaskTransform transform)
    {
        if (tracks.IsEmpty)
        {
            return tracks;
        }
        if (mask is not RectangleClipMask || transform.Rotation % 360 != 0 || transform.Scale.X <= 0 || transform.Scale.Y <= 0)
        {
            throw new InvalidOperationException("带旋转或镜像的蒙版不能从 ASS 矩形变换反推原生轨道；请在蒙版面板编辑动画。");
        }
        return tracks.Select(track => track with
        {
            InitialValue = track.InitialValue is { } initial ? AnimationValue.FromVector(AssMaskGeometry.Inverse(initial.Vector, transform)) : null,
            Keyframes = track.Keyframes.Select(key => key with { Value = AnimationValue.FromVector(AssMaskGeometry.Inverse(key.Value.Vector, transform)) }).ToImmutableArray(),
            Transforms = track.Transforms.Select(operation => operation with { Value = AnimationValue.FromVector(AssMaskGeometry.Inverse(operation.Value.Vector, transform)) }).ToImmutableArray()
        }).ToImmutableArray();
    }

    private static AssTextParser Parser(SubtitleLine line, int canvasWidth, int canvasHeight) => new(line,
        new Dictionary<string, AssStyleDefinition>(StringComparer.Ordinal), line.Style.Fill, projectSource: true,
        canvasWidth: canvasWidth, canvasHeight: canvasHeight, projectAnimations: true);

    private static ImmutableArray<KaraokeSegment> RemapInactiveKaraoke(SubtitleLine original, string text,
        ImmutableArray<KaraokeSegment> active)
    {
        var inactive = original.InactiveKaraoke;
        if (!inactive.IsEmpty && original.Text != text)
        {
            var map = MapTextChange(original.Text, text);
            if (map.OldStart < map.OldEnd && map.NewStart < map.NewEnd && CoversTextRange(active, map.NewStart, map.NewEnd))
            {
                inactive = inactive.Where(clip => clip.Utf16Start + clip.Utf16Length <= map.OldStart ||
                    clip.Utf16Start >= map.OldEnd).ToImmutableArray();
            }
            if (!inactive.IsEmpty)
            {
                var saved = original with { Karaoke = [], InactiveKaraoke = inactive };
                inactive = SubtitleKaraokeRemapping.Remap(saved, map).InactiveKaraoke;
            }
        }
        return PreserveInactiveKaraoke(inactive, active);
    }

    private static bool CoversTextRange(ImmutableArray<KaraokeSegment> active, int start, int end)
    {
        var cursor = start;
        foreach (var clip in active)
        {
            if (clip.Utf16Start > cursor)
            {
                return false;
            }
            cursor = Math.Max(cursor, clip.Utf16Start + clip.Utf16Length);
            if (cursor >= end)
            {
                return true;
            }
        }
        return false;
    }

    private static ImmutableArray<KaraokeSegment> PreserveInactiveKaraoke(ImmutableArray<KaraokeSegment> inactive,
        ImmutableArray<KaraokeSegment> active)
    {
        if (inactive.IsEmpty || active.IsEmpty)
        {
            return inactive;
        }
        var result = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var activeIndex = 0;
        foreach (var clip in inactive)
        {
            while (activeIndex < active.Length && active[activeIndex].Utf16Start + active[activeIndex].Utf16Length <= clip.Utf16Start)
            {
                activeIndex++;
            }
            if (activeIndex == active.Length || active[activeIndex].Utf16Start >= clip.Utf16Start + clip.Utf16Length)
            {
                result.Add(clip);
            }
        }
        return result.Count == inactive.Length ? inactive : result.ToImmutable();
    }

    private static int[] MatchClipSources(string previousSource, AssTextEditResult previous, string source, AssTextEditResult edited)
    {
        var indices = Enumerable.Repeat(-1, edited.Line.Karaoke.Length).ToArray();
        if (previous.KaraokeSourceMap.Length == edited.KaraokeSourceMap.Length)
        {
            for (var index = 0; index < edited.KaraokeSourceMap.Length; index++)
            {
                indices[edited.KaraokeSourceMap[index].SegmentIndex] = previous.KaraokeSourceMap[index].SegmentIndex;
            }
            return indices;
        }
        var tokens = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        foreach (var mapping in previous.KaraokeSourceMap)
        {
            var token = previousSource.Substring(mapping.SourceStart, mapping.SourceLength);
            if (!tokens.TryGetValue(token, out var queue))
            {
                queue = new();
                tokens.Add(token, queue);
            }
            queue.Enqueue(mapping.SegmentIndex);
        }
        var last = -1;
        foreach (var mapping in edited.KaraokeSourceMap)
        {
            var token = source.Substring(mapping.SourceStart, mapping.SourceLength);
            if (!tokens.TryGetValue(token, out var queue))
            {
                continue;
            }
            while (queue.TryPeek(out var candidate) && candidate <= last)
            {
                queue.Dequeue();
            }
            if (queue.TryDequeue(out var matched))
            {
                indices[mapping.SegmentIndex] = matched;
                last = matched;
            }
        }
        return indices;
    }

    private static KaraokeSegment RestoreClip(SubtitleLine original, SubtitleLine baseline, KaraokeSegment clip, int previousIndex)
    {
        if (previousIndex < 0)
        {
            return clip;
        }
        var previous = original.Karaoke[previousIndex];
        var serialized = baseline.Karaoke[previousIndex];
        return clip with
        {
            Id = previous.Id,
            Start = clip.Start == serialized.Start ? previous.Start : clip.Start,
            End = clip.End == serialized.End ? previous.End : clip.End,
            HighlightColor = previous.HighlightColor
        };
    }

    private static ImmutableArray<SubtitleKaraokeStyleSpan> RestoreKaraokeStylePrecision(SubtitleLine original,
        SubtitleLine baseline, SubtitleLine edited)
    {
        var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        var boundaries = SubtitleTextEditMap.Boundaries(edited.Text);
        var textMap = original.Text == edited.Text ? null : MapTextChange(original.Text, edited.Text);
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var oldOffset = textMap?.StyleSourceOffset(offset) ?? offset;
            var previousActive = KaraokeVisualStyleResolver.StyleAt(original.KaraokeStyleSpans, oldOffset, KaraokeVisualState.ACTIVE);
            var previousInactive = KaraokeVisualStyleResolver.StyleAt(original.KaraokeStyleSpans, oldOffset, KaraokeVisualState.INACTIVE);
            var active = previousActive;
            var inactive = previousInactive;
            if (edited.Karaoke.Any(clip => clip.Utf16Start <= offset && offset < clip.Utf16Start + clip.Utf16Length))
            {
                active = RestoreVisualOverride(previousActive, VisualStyleAt(original, oldOffset, KaraokeVisualState.ACTIVE),
                    VisualStyleAt(baseline, oldOffset, KaraokeVisualState.ACTIVE), VisualStyleAt(edited, offset, KaraokeVisualState.ACTIVE));
                inactive = RestoreVisualOverride(previousInactive, VisualStyleAt(original, oldOffset, KaraokeVisualState.INACTIVE),
                    VisualStyleAt(baseline, oldOffset, KaraokeVisualState.INACTIVE), VisualStyleAt(edited, offset, KaraokeVisualState.INACTIVE));
            }
            if (active is null && inactive is null)
            {
                continue;
            }
            var length = boundaries[index + 1] - offset;
            if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == offset &&
                spans[^1].ActiveStyle == active && spans[^1].InactiveStyle == inactive)
            {
                spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
            }
            else
            {
                spans.Add(new(offset, length, active, inactive));
            }
        }
        return original.KaraokeStyleSpans.SequenceEqual(spans) ? original.KaraokeStyleSpans : spans.ToImmutable();
    }

    private static SubtitleStyle VisualStyleAt(SubtitleLine line, int offset, KaraokeVisualState state)
    {
        var ordinary = StyleAt(line, offset);
        var rangeStyle = KaraokeVisualStyleResolver.StyleAt(line.KaraokeStyleSpans, offset, state);
        var segment = line.Karaoke.FirstOrDefault(clip => clip.Utf16Start <= offset && offset < clip.Utf16Start + clip.Utf16Length);
        if (segment is null)
        {
            return rangeStyle?.ApplyTo(ordinary) ?? ordinary;
        }
        return state == KaraokeVisualState.ACTIVE
            ? KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, segment, rangeStyle)
            : KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment, rangeStyle);
    }

    private static KaraokeVisualStyleOverride? RestoreVisualOverride(KaraokeVisualStyleOverride? previous,
        SubtitleStyle native, SubtitleStyle serialized, SubtitleStyle next)
    {
        var restored = (previous ?? new()) with
        {
            Fill = next.Fill == serialized.Fill ? previous?.Fill : RestoreColor(native.Fill, serialized.Fill, next.Fill),
            FillBlur = next.FillBlur.Equals(serialized.FillBlur) ? previous?.FillBlur : next.FillBlur,
            Stroke = next.Stroke == serialized.Stroke ? previous?.Stroke : RestoreColor(native.Stroke, serialized.Stroke, next.Stroke),
            StrokeBlur = next.StrokeBlur.Equals(serialized.StrokeBlur) ? previous?.StrokeBlur : next.StrokeBlur,
            StrokeWidth = next.StrokeWidth.Equals(serialized.StrokeWidth) ? previous?.StrokeWidth : next.StrokeWidth,
            ShadowColor = next.ShadowColor == serialized.ShadowColor ? previous?.ShadowColor : RestoreColor(native.ShadowColor, serialized.ShadowColor, next.ShadowColor),
            ShadowOffset = next.ShadowOffset == serialized.ShadowOffset ? previous?.ShadowOffset : RestorePoint(native.ShadowOffset, serialized.ShadowOffset, next.ShadowOffset),
            ShadowBlur = next.ShadowBlur.Equals(serialized.ShadowBlur) ? previous?.ShadowBlur : next.ShadowBlur
        };
        return restored.HasOverrides ? restored : null;
    }

    private static SceneColor RestoreColor(SceneColor native, SceneColor serialized, SceneColor next)
    {
        return new(
            next.Red.Equals(serialized.Red) ? native.Red : next.Red,
            next.Green.Equals(serialized.Green) ? native.Green : next.Green,
            next.Blue.Equals(serialized.Blue) ? native.Blue : next.Blue,
            next.Alpha.Equals(serialized.Alpha) ? native.Alpha : next.Alpha);
    }

    private static ScenePoint RestorePoint(ScenePoint native, ScenePoint serialized, ScenePoint next)
    {
        return new(next.X.Equals(serialized.X) ? native.X : next.X, next.Y.Equals(serialized.Y) ? native.Y : next.Y);
    }

    private static ImmutableArray<SubtitleInlineSpan> RestorePrecision(SubtitleLine original, SubtitleLine baseline, SubtitleLine edited)
    {
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        var boundaries = StringInfo.ParseCombiningCharacters(edited.Text).Append(edited.Text.Length).ToArray();
        var textMap = original.Text == edited.Text ? null : MapTextChange(original.Text, edited.Text);
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var oldOffset = textMap?.StyleSourceOffset(offset) ?? offset;
            var previous = StyleAt(original, oldOffset);
            var previousOverride = OverrideAt(original, oldOffset);
            var serialized = StyleAt(baseline, oldOffset);
            var next = StyleAt(edited, offset);
            var preservesFont = next.FontFamily == serialized.FontFamily && next.Bold == serialized.Bold &&
                next.Italic == serialized.Italic;
            var visual = RestoreVisualOverride(previousOverride is null ? null : new()
            {
                Fill = previousOverride.Fill, FillBlur = previousOverride.FillBlur,
                Stroke = previousOverride.Stroke, StrokeBlur = previousOverride.StrokeBlur, StrokeWidth = previousOverride.StrokeWidth,
                ShadowColor = previousOverride.ShadowColor, ShadowOffset = previousOverride.ShadowOffset, ShadowBlur = previousOverride.ShadowBlur
            }, previous, serialized, next);
            var style = (previousOverride ?? new()) with
            {
                Fill = visual?.Fill, FillBlur = visual?.FillBlur,
                Stroke = visual?.Stroke, StrokeBlur = visual?.StrokeBlur, StrokeWidth = visual?.StrokeWidth,
                ShadowColor = visual?.ShadowColor, ShadowOffset = visual?.ShadowOffset, ShadowBlur = visual?.ShadowBlur,
                FontFamily = next.FontFamily == serialized.FontFamily ? previousOverride?.FontFamily : next.FontFamily,
                FontAssetId = next.FontFamily == serialized.FontFamily ? previousOverride?.FontAssetId : null,
                ClearFontAsset = next.FontFamily == serialized.FontFamily ? previousOverride?.ClearFontAsset ?? false : true,
                FontVariant = preservesFont ? previousOverride?.FontVariant : null,
                ClearFontVariant = preservesFont ? previousOverride?.ClearFontVariant ?? false : true,
                FontSize = next.FontSize.Equals(serialized.FontSize) ? previousOverride?.FontSize : next.FontSize,
                LetterSpacing = next.LetterSpacing.Equals(serialized.LetterSpacing) ? previousOverride?.LetterSpacing : next.LetterSpacing,
                Bold = next.Bold == serialized.Bold ? previousOverride?.Bold : next.Bold,
                Italic = next.Italic == serialized.Italic ? previousOverride?.Italic : next.Italic,
                Underline = next.Underline == serialized.Underline ? previousOverride?.Underline : next.Underline,
                Strikethrough = next.Strikethrough == serialized.Strikethrough ? previousOverride?.Strikethrough : next.Strikethrough
            };
            if (!style.HasOverrides)
            {
                continue;
            }
            var length = boundaries[index + 1] - offset;
            if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == offset && spans[^1].Style == style)
            {
                spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
            }
            else
            {
                spans.Add(new(offset, length, style));
            }
        }
        var result = spans.ToImmutable();
        return original.InlineSpans.SequenceEqual(result) ? original.InlineSpans : result;
    }

    private static SubtitleTextEditMap MapTextChange(string original, string edited)
    {
        return SubtitleTextEditMap.Between(original, edited);
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset)
    {
        return OverrideAt(line, offset)?.ApplyTo(line.Style) ?? line.Style;
    }

    private static SubtitleInlineStyleOverride? OverrideAt(SubtitleLine line, int offset)
    {
        var start = 0;
        var end = line.InlineSpans.Length - 1;
        while (start <= end)
        {
            var middle = start + (end - start) / 2;
            var span = line.InlineSpans[middle];
            if (offset < span.Utf16Start)
            {
                end = middle - 1;
            }
            else if (offset >= span.Utf16Start + span.Utf16Length)
            {
                start = middle + 1;
            }
            else
            {
                return span.Style;
            }
        }
        return null;
    }
}
