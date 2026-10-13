using System.Collections.Immutable;
using System.Text;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssTextAnimationExport(SubtitleLine line, ProjectLayer layer,
    ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool projection = false)
{
    private readonly HashSet<string> reported = [];
    private readonly ImmutableHashSet<AnimationProperty> sharedChannels = projection ? [] : layer.Tracks.Where(track =>
        track.Target.State != SubtitleAnimationState.NORMAL && IsSharedVisual(track.Property) &&
        line.Karaoke.Any(segment => track.Target.TextRangeId is not { } rangeId ||
            line.AnimationRanges.Any(range => range.Id == rangeId && range.Utf16Start < segment.Utf16Start + segment.Utf16Length &&
                segment.Utf16Start < range.Utf16Start + range.Utf16Length))).Select(track => SharedChannel(track.Property)).ToImmutableHashSet();

    internal static bool Handles(AnimationTrack track) => track.Target.TextRangeId is not null ||
        track.Target.State != SubtitleAnimationState.NORMAL || track.Property is AnimationProperty.FONT_SIZE or
        AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or AnimationProperty.SHADOW_COLOR or
        AnimationProperty.FILL or AnimationProperty.STROKE ||
        track.Property is AnimationProperty.LETTER_SPACING or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR &&
            AssNumericAnimation.FromTrack(track).Approximate ||
        track.Property == AnimationProperty.SCALE && track.IsOrdered && track.Transforms.Any(operation => operation.ComponentMask != 0);

    internal bool HasMatchingShadowBlur(AnimationProperty blurProperty)
    {
        var blur = layer.Tracks.FirstOrDefault(track => track.Property == blurProperty &&
            track.Target.TextRangeId is null && track.Target.State == SubtitleAnimationState.NORMAL);
        return blur is not null && layer.Tracks.Any(track =>
            track.Target == blur.Target with { Property = AnimationProperty.SHADOW_BLUR } && Equivalent(blur, track));
    }

    internal bool HandlesForExport(AnimationTrack track)
    {
        return Handles(track) || UsesSharedAppearance(track.Property);
    }

    internal bool UsesSharedAppearance(AnimationProperty property)
    {
        return IsSharedVisual(property) && sharedChannels.Contains(SharedChannel(property));
    }

    internal ImmutableHashSet<AnimationProperty> SharedVisualProperties(int offset, bool karaoke)
    {
        return SharedVisualProperties(SelectTracks(offset), karaoke);
    }

    internal string Write(int offset, SubtitleStyle style, bool karaoke, MediaTime origin, ScenePoint scale, double rotation,
        SubtitleStyle? sharedStyle = null, double rotationX = 0, double rotationY = 0)
    {
        var selected = SelectTracks(offset);
        var matching = line.AnimationRanges.Where(range => range.Utf16Start <= offset && offset < range.Utf16Start + range.Utf16Length).ToArray();
        if (matching.Any(range => range.Offset != default || layer.Tracks.Any(track =>
            track.Target.TextRangeId == range.Id && track.Property == AnimationProperty.POSITION)))
        {
            Report("Ass.RangeTranslation", "ASS cannot represent independent text-range translation after layout. Range translation was omitted; native ranges and translation animation are preserved in the project.");
        }
        if (karaoke && (selected.ContainsKey((AnimationProperty.FILL, SubtitleAnimationState.INACTIVE)) ||
            KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE)?.Fill is not null))
        {
            selected.Remove((AnimationProperty.FILL, SubtitleAnimationState.NORMAL));
        }
        var shared = SharedVisualProperties(selected, karaoke);
        SelectSharedAppearance(selected, shared, sharedStyle ?? style, origin, offset);
        if (matching.Length > 1)
        {
            Report("Ass.RangeOverlap", "Overlapping text-range styles were converted in range order. Combined geometry and pivots cannot be represented exactly in ASS, so overlapping range geometry was omitted.");
            foreach (var property in new[] { AnimationProperty.SCALE, AnimationProperty.ROTATION, AnimationProperty.ROTATION_X,
                AnimationProperty.ROTATION_Y, AnimationProperty.PERSPECTIVE_DISTANCE })
            {
                if (selected.GetValueOrDefault((property, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null)
                {
                    selected.Remove((property, SubtitleAnimationState.NORMAL));
                }
            }
        }
        var result = new StringBuilder();
        if (matching.Length == 1)
        {
            var range = matching[0];
            if (!projection && RemoveIdentityGeometryTrack(selected, AnimationProperty.SCALE, new ScenePoint(1, 1)))
            {
                range = range with { Scale = new(1, 1) };
            }
            if (!projection && RemoveIdentityGeometryTrack(selected, AnimationProperty.ROTATION, 0))
            {
                range = range with { Rotation = 0 };
            }
            if (!projection && RemoveIdentityGeometryTrack(selected, AnimationProperty.ROTATION_X, 0))
            {
                range = range with { RotationX = 0 };
            }
            if (!projection && RemoveIdentityGeometryTrack(selected, AnimationProperty.ROTATION_Y, 0))
            {
                range = range with { RotationY = 0 };
            }
            if (range.PerspectiveDistance != SceneProjection.DEFAULT_DISTANCE || selected.Values.Any(track =>
                track.Target.TextRangeId == range.Id && track.Property == AnimationProperty.PERSPECTIVE_DISTANCE &&
                (!AssMoveConversion.TryConstant(track, out var distance) || distance.Scalar != SceneProjection.DEFAULT_DISTANCE)))
            {
                Report("Ass.PerspectiveDistance", "ASS uses a fixed perspective distance and cannot preserve native text-range distance values or their animation. Export used 312.5 project pixels; native parameters remain in the project.");
            }
            if (range.ProjectionTranslation != default)
            {
                Report("Ass.ProjectionTranslation", "ASS cannot preserve native text-range translation in three-dimensional projection space. Projection translation was omitted; native pivot compensation remains in the project.");
            }
            matching[0] = range;
            var hasScale = range.Scale != new ScenePoint(1, 1) ||
                selected.GetValueOrDefault((AnimationProperty.SCALE, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null;
            var hasRotation = range.Rotation != 0 ||
                selected.GetValueOrDefault((AnimationProperty.ROTATION, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null;
            var hasRotationX = range.RotationX != 0 ||
                selected.GetValueOrDefault((AnimationProperty.ROTATION_X, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null;
            var hasRotationY = range.RotationY != 0 ||
                selected.GetValueOrDefault((AnimationProperty.ROTATION_Y, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null;
            var rangePerspective = hasRotationX || hasRotationY;
            var wholePerspective = rotationX != 0 || rotationY != 0 || HasWholeGeometryTrack(AnimationProperty.ROTATION_X, 0) ||
                HasWholeGeometryTrack(AnimationProperty.ROTATION_Y, 0);
            var wholeGeometry = wholePerspective || scale != new ScenePoint(1, 1) || rotation != 0 ||
                HasWholeGeometryTrack(AnimationProperty.SCALE, new ScenePoint(1, 1)) || HasWholeGeometryTrack(AnimationProperty.ROTATION, 0);
            if (!projection && (rangePerspective && wholeGeometry || wholePerspective && hasRotation))
            {
                Report("Ass.PerspectiveComposition", "ASS cannot preserve composed whole-layer and text-range perspective or rotation as independent Euler angles. Conflicting range geometry was omitted; whole-layer geometry and native range parameters were preserved.");
                foreach (var property in new[] { AnimationProperty.SCALE, AnimationProperty.ROTATION,
                    AnimationProperty.ROTATION_X, AnimationProperty.ROTATION_Y })
                {
                    if (selected.GetValueOrDefault((property, SubtitleAnimationState.NORMAL))?.Target.TextRangeId is not null)
                    {
                        selected.Remove((property, SubtitleAnimationState.NORMAL));
                    }
                }
                range = range with { Scale = new(1, 1), Rotation = 0, RotationX = 0, RotationY = 0 };
                matching[0] = range;
                hasScale = hasRotation = hasRotationX = hasRotationY = false;
            }
            if (hasScale || hasRotation || hasRotationX || hasRotationY)
            {
                Report("Ass.TextRangeGeometry", "Native text ranges transform after layout, while ASS applies scaling and rotation during glyph layout. Edges, shadows, and clip positions may differ.");
            }
            if (range.Scale.X < 0 || range.Scale.Y < 0)
            {
                Report("Ass.TransformScale", "ASS cannot preserve mirrored text ranges. Negative scaling components were omitted and convertible components were preserved.");
            }
            if (range.Pivot != SubtitleAnimationPivot.SUBTITLE_ANCHOR && (hasScale || hasRotation || hasRotationX || hasRotationY))
            {
                Report("Ass.RangePivot", "Text ranges use independent center pivots, while ASS can only use a shared subtitle pivot. Range geometry positions may differ.");
            }
            if (projection || hasScale)
            {
                result.Append("\\fscx").Append(Number((range.Scale.X < 0 ? 1 : range.Scale.X) * scale.X * 100))
                    .Append("\\fscy").Append(Number((range.Scale.Y < 0 ? 1 : range.Scale.Y) * scale.Y * 100));
            }
            if (projection || hasRotation)
            {
                result.Append("\\frz").Append(Number(-(range.Rotation + rotation)));
            }
            if (projection || hasRotationX)
            {
                result.Append("\\frx").Append(Number(-range.RotationX));
            }
            if (projection || hasRotationY)
            {
                result.Append("\\fry").Append(Number(-range.RotationY));
            }
        }
        foreach (var pair in selected)
        {
            var track = pair.Value;
            if (track.Target.TextRangeId is not null && track.Property == AnimationProperty.POSITION)
            {
                continue;
            }
            if (track.Property == AnimationProperty.PERSPECTIVE_DISTANCE)
            {
                continue;
            }
            var values = track.Keyframes.Select(frame => frame.Value).Concat(track.Transforms.Select(operation => operation.Value));
            if (track.InitialValue is { } initial)
            {
                values = values.Prepend(initial);
            }
            if (values.Any(value => value.IsColor && (value.Color.Red is < 0 or > 1 || value.Color.Green is < 0 or > 1 || value.Color.Blue is < 0 or > 1)))
            {
                Report("Ass.ColorRange", "ASS 8-bit sRGB colors limit HDR or negative animation colors. Output colors were clipped and quantized.");
            }
            if (track.Property == AnimationProperty.SCALE && values.Any(value => value.Vector.X < 0 || value.Vector.Y < 0))
            {
                Report("Ass.TransformScale", "ASS cannot preserve mirrored phases of scaling animation. Negative scaling components were omitted; other convertible components are still exported.");
            }
            if (track.Target.State != SubtitleAnimationState.NORMAL && !karaoke)
            {
                if (!projection)
                {
                    Report("Ass.DormantKaraokeAnimation", "Stored ACTIVE/INACTIVE animations for untimed text cannot be written to ASS. The text was exported as ordinary text; state animations in the project are unchanged.");
                }
                continue;
            }
            var channel = track.Target.State == SubtitleAnimationState.INACTIVE ||
                karaoke && track.Property == AnimationProperty.FILL && track.Target.State == SubtitleAnimationState.NORMAL ? "2" : "1";
            if (track.Target.State != SubtitleAnimationState.NORMAL && track.Property != AnimationProperty.FILL && !shared.Contains(track.Property))
            {
                Report("Ass.KaraokeAnimation", "ASS stroke, shadow, and layout only have shared channels and cannot preserve independent continuous animations for active and inactive states. This state track was omitted.");
                continue;
            }
            if (track.Property == AnimationProperty.SHADOW_BLUR && !shared.Contains(track.Property))
            {
                var blurProperty = style.StrokeWidth > 0 ? AnimationProperty.STROKE_BLUR : AnimationProperty.FILL_BLUR;
                var blur = selected.GetValueOrDefault((blurProperty, track.Target.State)) ?? layer.Tracks.FirstOrDefault(candidate => candidate.Property == blurProperty && candidate.Target == track.Target with { Property = blurProperty });
                if (blur is null || !Equivalent(blur, track))
                {
                    Report("Ass.ShadowBlur", "ASS blur affects text or stroke and shadow together. Independent shadow blur animation cannot be represented exactly, so the shadow blur track was omitted.");
                }
                continue;
            }
            if (!shared.Contains(track.Property) && track.Property is AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR &&
                (track.Property == AnimationProperty.FILL_BLUR ? style.StrokeWidth > 0 : style.StrokeWidth <= 0))
            {
                Report("Ass.BlurAnimation", "ASS has only one edge-blur channel. Independent blur animation that is invisible in the current stroke state was omitted.");
                continue;
            }
            if (track.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING)
            {
                Report("Ass.TextLayoutAnimation", "Continuous font size or letter spacing was exported as ASS tags. Native per-frame layout may differ from ASS line layout, automatic wrapping, and anchor calculation.");
            }
            var rangeGeometry = track.Target.TextRangeId is { } rangeId ? matching.FirstOrDefault(range => range.Id == rangeId) : null;
            var shadowRotation = rotation + (rangeGeometry?.Rotation ?? 0);
            AnimationValue OutputValue(AnimationValue value)
            {
                if (track.Property != AnimationProperty.SHADOW_OFFSET)
                {
                    return value;
                }
                var parts = AssTransformMath.SinCos(shadowRotation);
                var x = value.Vector.X * scale.X * (rangeGeometry?.Scale.X ?? 1);
                var y = value.Vector.Y * scale.Y * (rangeGeometry?.Scale.Y ?? 1);
                return new ScenePoint(parts.Cosine * x - parts.Sine * y, parts.Sine * x + parts.Cosine * y);
            }
            string Tags(AnimationValue value, int mask, AnimationTransformMode mode) => ValueTags(track.Property, OutputValue(value),
                mask, mode, channel, track.Target.TextRangeId is null ? new(1, 1) : scale,
                track.Target.TextRangeId is null ? 0 : rotation,
                track.Property == AnimationProperty.SHADOW_OFFSET ? 1 : Math.Sqrt(Math.Abs(scale.X * scale.Y)));
            double[] Components(AnimationValue value)
            {
                value = OutputValue(value);
                return Enumerable.Range(0, value.ComponentCount)
                    .Select(component => value.IsColor && component < 3 ? Encode(value.GetComponent(component)) : value.GetComponent(component)).ToArray();
            }
            var forceSampling = track.Property == AnimationProperty.SHADOW_OFFSET && shadowRotation != 0 &&
                (!track.IsOrdered || track.Transforms.Any(operation => operation.ComponentMask != 0));
            result.Append(AssTextAnimationTrackWriter.Write(track, Tags, Components, origin,
                layer.AnimationOffset + layer.End - layer.Start, line.Id, diagnostics, forceSampling));
        }
        return result.ToString();
    }

    private Dictionary<(AnimationProperty Property, SubtitleAnimationState State), AnimationTrack> SelectTracks(int offset)
    {
        var selected = new Dictionary<(AnimationProperty Property, SubtitleAnimationState State), AnimationTrack>();
        foreach (var track in layer.Tracks.Where(track => (HandlesForExport(track) || projection &&
            track.Property is AnimationProperty.LETTER_SPACING or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR) && track.Target.TextRangeId is null))
        {
            selected[(track.Property, track.Target.State)] = track;
        }
        foreach (var range in line.AnimationRanges.Where(range => range.Utf16Start <= offset && offset < range.Utf16Start + range.Utf16Length))
        {
            foreach (var track in layer.Tracks.Where(track => track.Target.TextRangeId == range.Id))
            {
                selected[(track.Property, track.Target.State)] = track;
            }
        }
        return selected;
    }

    private ImmutableHashSet<AnimationProperty> SharedVisualProperties(
        Dictionary<(AnimationProperty Property, SubtitleAnimationState State), AnimationTrack> selected, bool karaoke)
    {
        if (projection || !karaoke)
        {
            return [];
        }
        var result = selected.Values.Where(track => track.Target.State != SubtitleAnimationState.NORMAL && IsSharedVisual(track.Property))
            .Select(track => track.Property).ToImmutableHashSet();
        return result.Any(IsBlur) ? result.Union([AnimationProperty.FILL_BLUR, AnimationProperty.STROKE_BLUR, AnimationProperty.SHADOW_BLUR]) : result;
    }

    private void SelectSharedAppearance(Dictionary<(AnimationProperty Property, SubtitleAnimationState State), AnimationTrack> selected,
        ImmutableHashSet<AnimationProperty> shared, SubtitleStyle style, MediaTime origin, int offset)
    {
        if (shared.IsEmpty)
        {
            return;
        }
        var width = selected.Values.Where(track => track.Property == AnimationProperty.STROKE_WIDTH)
            .OrderBy(track => StatePriority(track.Target.State)).FirstOrDefault() ?? layer.Tracks.FirstOrDefault(track =>
                track.Property == AnimationProperty.STROKE_WIDTH && track.Target.State == SubtitleAnimationState.NORMAL &&
                track.Target.TextRangeId is null);
        var overridesNormalWidth = KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE)?.StrokeWidth is not null ||
            line.Karaoke.Any(segment => segment.Utf16Start <= offset && offset < segment.Utf16Start + segment.Utf16Length &&
                segment.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP);
        var strokeWidth = width is null || width.Target.State == SubtitleAnimationState.NORMAL && overridesNormalWidth
            ? style.StrokeWidth : SceneEvaluator.EvaluateTrack(width, origin).Scalar;
        var edgeBlur = strokeWidth > 0 ? AnimationProperty.STROKE_BLUR : AnimationProperty.FILL_BLUR;
        foreach (var group in selected.Values.Where(track => shared.Contains(track.Property)).GroupBy(track => SharedChannel(track.Property)).ToArray())
        {
            var winner = group.OrderBy(track => StatePriority(track.Target.State))
                .ThenBy(track => track.Property == edgeBlur ? 0 : track.Property == AnimationProperty.SHADOW_BLUR ? 1 : 2).First();
            foreach (var track in group)
            {
                selected.Remove((track.Property, track.Target.State));
            }
            selected[(winner.Property, winner.Target.State)] = winner;
        }
        Report("Ass.KaraokeAnimation", "Independent karaoke stroke and shadow animations were mapped to shared ASS channels. Each channel uses INACTIVE before ACTIVE, then NORMAL; competing tracks and activation-specific static switches for that channel were omitted. Both visual states use the selected animation, and reimporting cannot recover state ownership.");
        if (shared.Any(IsBlur))
        {
            Report("Ass.BlurAnimation", "Karaoke fill, stroke, and shadow blur share one ASS blur animation. Within the selected state, the initially visible edge takes priority, then shadow, then the other edge. Independent blur and changes in which edge is visible cannot be preserved.");
        }
    }

    private static bool IsSharedVisual(AnimationProperty property)
    {
        return property is AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or
            AnimationProperty.SHADOW_COLOR or AnimationProperty.SHADOW_OFFSET || IsBlur(property);
    }

    private static bool IsBlur(AnimationProperty property)
    {
        return property is AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_BLUR;
    }

    private static AnimationProperty SharedChannel(AnimationProperty property)
    {
        return IsBlur(property) ? AnimationProperty.FILL_BLUR : property;
    }

    private static int StatePriority(SubtitleAnimationState state)
    {
        return state switch
        {
            SubtitleAnimationState.INACTIVE => 0,
            SubtitleAnimationState.ACTIVE => 1,
            _ => 2
        };
    }

    private bool RemoveIdentityGeometryTrack(Dictionary<(AnimationProperty Property, SubtitleAnimationState State), AnimationTrack> selected,
        AnimationProperty property, AnimationValue identity)
    {
        var key = (property, SubtitleAnimationState.NORMAL);
        if (selected.GetValueOrDefault(key) is not { Target.TextRangeId: not null } track ||
            !AssMoveConversion.TryConstant(track, out var value) || value != identity)
        {
            return false;
        }
        selected.Remove(key);
        var whole = layer.Tracks.FirstOrDefault(candidate => candidate.Property == property &&
            candidate.Target.TextRangeId is null && candidate.Target.State == SubtitleAnimationState.NORMAL && Handles(candidate));
        if (whole is not null)
        {
            selected[key] = whole;
        }
        return true;
    }

    private bool HasWholeGeometryTrack(AnimationProperty property, AnimationValue identity)
    {
        return layer.Tracks.Any(track => track.Property == property && track.Target.TextRangeId is null &&
            (!AssMoveConversion.TryConstant(track, out var value) || value != identity));
    }

    private string ValueTags(AnimationProperty property, AnimationValue value, int mask, AnimationTransformMode mode,
        string channel, ScenePoint scale, double rotation, double appearanceScale)
    {
        var result = new StringBuilder();
        bool Component(int index) => mask == 0 || (mask & (1 << index)) != 0;
        if (value.IsColor)
        {
            var color = value.Color;
            var number = property == AnimationProperty.FILL ? channel : property == AnimationProperty.STROKE ? "3" : "4";
            if (Component(0) || Component(1) || Component(2))
            {
                result.Append('\\').Append(number).Append('c').Append(AssFormatValues.Color(color, false));
            }
            if (Component(3))
            {
                result.Append('\\').Append(number).Append('a').Append(AssFormatValues.Alpha(color));
            }
            return result.ToString();
        }
        if (property == AnimationProperty.SCALE)
        {
            if (Component(0) && value.Vector.X >= 0)
            {
                result.Append("\\fscx").Append(Number(value.Vector.X * scale.X * 100));
            }
            if (Component(1) && value.Vector.Y >= 0)
            {
                result.Append("\\fscy").Append(Number(value.Vector.Y * scale.Y * 100));
            }
        }
        else if (property == AnimationProperty.SHADOW_OFFSET)
        {
            if (Component(0))
            {
                result.Append("\\xshad").Append(Number(value.Vector.X * appearanceScale));
            }
            if (Component(1))
            {
                result.Append("\\yshad").Append(Number(value.Vector.Y * appearanceScale));
            }
        }
        else
        {
            var tag = property switch
            {
                AnimationProperty.FONT_SIZE => "fs", AnimationProperty.LETTER_SPACING => "fsp",
                AnimationProperty.STROKE_WIDTH => "bord", AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_BLUR => "blur",
                AnimationProperty.ROTATION => "frz", AnimationProperty.ROTATION_X => "frx", AnimationProperty.ROTATION_Y => "fry", _ => null
            };
            if (tag is not null)
            {
                var amount = property switch
                {
                    AnimationProperty.FONT_SIZE when mode == AnimationTransformMode.MULTIPLY_BY => (value.Scalar - 1) * 10,
                    AnimationProperty.STROKE_WIDTH => value.Scalar * appearanceScale,
                    AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_BLUR => value.Scalar * appearanceScale / AssBlurConversion.SigmaPerUnit,
                    AnimationProperty.ROTATION => -(value.Scalar + rotation),
                    AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y => -value.Scalar,
                    _ => value.Scalar
                };
                if (tag == "blur" && !projection)
                {
                    AssExportPrecision.AddBlurRange(amount, line.Id, diagnostics, reported);
                }
                result.Append('\\').Append(tag);
                if (mode == AnimationTransformMode.MULTIPLY_BY && amount >= 0)
                {
                    result.Append('+');
                }
                result.Append(Number(amount));
            }
        }
        return result.ToString();
    }

    private string Number(double amount)
    {
        AssExportPrecision.AddNumbers(line.Id, diagnostics, reported, amount);
        return AssFormatValues.Number(amount);
    }

    private static bool Equivalent(AnimationTrack first, AnimationTrack second)
    {
        if (first.IsOrdered != second.IsOrdered)
        {
            return false;
        }
        return first.IsOrdered ? first.InitialValue == second.InitialValue && first.Transforms.Length == second.Transforms.Length &&
            first.Transforms.Zip(second.Transforms).All(pair => pair.First with { Id = pair.Second.Id } == pair.Second) : first.Keyframes.SequenceEqual(second.Keyframes);
    }

    private static double Encode(double value)
    {
        var bounded = Math.Clamp(value, 0, 1);
        return bounded <= 0.0031308 ? bounded * 12.92 : 1.055 * Math.Pow(bounded, 1 / 2.4) - 0.055;
    }

    private void Report(string code, string message)
    {
        if (reported.Add(code))
        {
            diagnostics.Add(new(code, message, SubtitleId: line.Id));
        }
    }
}
