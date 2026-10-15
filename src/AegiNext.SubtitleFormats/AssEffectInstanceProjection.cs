using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssEffectInstanceProjection
{
    internal static ProjectDocument Create(ProjectDocument document,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (!document.Layers.Any(RequiresProjection))
        {
            return document;
        }
        var scene = new PreparedProjectScene(document);
        var subtitles = document.Subtitles.ToDictionary(line => line.Id);
        var layers = document.Layers.Select(layer =>
        {
            if (!RequiresProjection(layer))
            {
                return layer;
            }
            var subtitle = layer.SubtitleId is { } subtitleId ? subtitles[subtitleId] : null;
            var plans = scene.GetEffectPlans(layer.Id);
            var projected = ProjectClip(scene, layer, subtitle, plans, diagnostics);
            if (projected.Subtitle is { } line)
            {
                subtitles[line.Id] = line;
            }
            diagnostics.Add(new("Ass.EffectInstances", "The ordered animation stack was evaluated into temporary ASS animation. Editable script snapshots, parameters, and stack identities remain only in the project.",
                SubtitleId: layer.SubtitleId, LayerId: layer.SubtitleId is null ? layer.Id : null));
            return projected.Layer;
        }).ToImmutableArray();
        return document with
        {
            Layers = layers,
            Subtitles = document.Subtitles.Select(line => subtitles[line.Id]).ToImmutableArray()
        };
    }

    private static bool RequiresProjection(ProjectLayer layer) =>
        !layer.EffectRowOrder.IsEmpty || layer.Effects.Any(instance => instance.IsEnabled);

    private static (ProjectLayer Layer, SubtitleLine? Subtitle) ProjectClip(PreparedProjectScene scene, ProjectLayer layer,
        SubtitleLine? subtitle, ImmutableArray<CompiledEffectScript> plans,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var channels = plans.SelectMany(plan => plan.Channels).ToArray();
        var allRanges = (subtitle?.AnimationRanges ?? []).AddRange(plans.SelectMany(plan => plan.Ranges));
        var ranges = allRanges.ToBuilder();
        var output = layer.Tracks.ToDictionary(track => track.Target);
        var (origin, end) = LayerAnimationTiming.GetRange(layer);
        var sourceTracks = layer.Tracks.Concat(channels.SelectMany(channel => new[] { channel.Multiplier, channel.Offset })).ToArray();
        var sourceTimes = sourceTracks.SelectMany(track => track.Keyframes.Select(frame => frame.Time)
                .Concat(track.Transforms.SelectMany(operation => new[] { operation.Start, operation.End })))
            .Concat(channels.SelectMany(channel => new[] { channel.Start, channel.End }))
            .Concat(layer.Effects.Where(instance => instance.IsEnabled).SelectMany(instance => new[] { instance.Start, instance.End }))
            .Append(origin).Append(end).Where(time => time >= origin && time <= end).Distinct().Order().ToArray();
        var evaluations = new Dictionary<(MediaTime Time, bool Before), EvaluatedLayer>();
        EvaluatedLayer Evaluate(MediaTime time, bool before)
        {
            var key = (time, before);
            if (!evaluations.TryGetValue(key, out var evaluated))
            {
                evaluated = SceneEvaluator.EvaluateLayer(scene, layer.Id, time, before);
                if (evaluations.Count >= 64)
                {
                    evaluations.Clear();
                }
                evaluations.Add(key, evaluated);
            }
            return evaluated;
        }
        AnimationTrack Sample(AnimationTrackTarget target, Func<EvaluatedLayer, AnimationValue> value)
        {
            AnimationValue At(MediaTime time, bool before)
            {
                return value(Evaluate(time > end ? end : time, before || time >= end));
            }
            var first = value(Evaluate(origin, false));
            var seed = new AnimationTrack(target, sourceTimes.Select(time => new Keyframe(time, first)).ToImmutableArray());
            var discontinuities = sourceTimes.Where(time => time > origin && time < end &&
                Different(At(time, false), At(time, true))).ToArray();
            var sampled = AssAnimationSampler.Sample(seed, Components, origin, end,
                time => At(time, false), time => At(time, true), discontinuities);
            diagnostics.Add(new(sampled.Limited ? "Ass.AnimationSamplingLimit" : "Ass.AnimationSampling",
                sampled.Limited ? "Effect animation reached the 1-millisecond or 4096-point sampling limit; some checked intervals exceed the output error tolerance." :
                    "Effect animation was sampled as ASS linear transforms with millisecond timing and checked component errors below 1/255.",
                SubtitleId: layer.SubtitleId, LayerId: layer.SubtitleId is null ? layer.Id : null));
            return LayerAnimationTiming.Clip(layer with { Effects = [], EffectRowOrder = [], Tracks = [new(target, sampled.Frames)] }).Tracks[0];
        }
        foreach (var target in channels.Select(channel => Canonical(channel.Target)).Where(target =>
                     subtitle is null || !IsStyle(target.Property)).Distinct())
        {
            foreach (var existing in output.Keys.Where(existing => Canonical(existing) == target).ToArray())
            {
                output.Remove(existing);
            }
            output[target] = Sample(target, evaluated => NativeValue(evaluated, layer, subtitle, allRanges, target));
        }
        if (subtitle is not null)
        {
            var affected = channels.Select(channel => channel.Target)
                .Concat(layer.EffectRowOrder.IsEmpty ? [] : layer.Tracks.Select(track => track.Target))
                .Where(target => IsStyle(target.Property)).Select(target => (target.Property, target.State)).Distinct().ToArray();
            foreach (var existing in output.Keys.Where(target => affected.Contains((target.Property, target.State))).ToArray())
            {
                output.Remove(existing);
            }
            var boundaries = new SortedSet<int> { 0, subtitle.Text.Length };
            foreach (var range in allRanges)
            {
                boundaries.Add(range.Utf16Start);
                boundaries.Add(range.Utf16Start + range.Utf16Length);
            }
            foreach (var span in subtitle.InlineSpans)
            {
                boundaries.Add(span.Utf16Start);
                boundaries.Add(span.Utf16Start + span.Utf16Length);
            }
            foreach (var span in subtitle.KaraokeStyleSpans)
            {
                boundaries.Add(span.Utf16Start);
                boundaries.Add(span.Utf16Start + span.Utf16Length);
            }
            foreach (var clip in subtitle.Karaoke.Concat(subtitle.InactiveKaraoke))
            {
                boundaries.Add(clip.Utf16Start);
                boundaries.Add(clip.Utf16Start + clip.Utf16Length);
            }
            var offsets = boundaries.ToArray();
            for (var index = 0; index < offsets.Length - 1; index++)
            {
                var offset = offsets[index];
                if (affected.Length == 0)
                {
                    continue;
                }
                var cell = allRanges.LastOrDefault(range => range.Utf16Start == offset &&
                    range.Utf16Length == offsets[index + 1] - offset);
                if (cell is null && ranges.Count >= 256)
                {
                    throw new InvalidDataException("ASS effect projection exceeds the 256 text-range budget. The source project was not modified.");
                }
                if (cell is null)
                {
                    cell = new(Guid.NewGuid(), offset, offsets[index + 1] - offset);
                    ranges.Add(cell);
                }
                foreach (var (property, state) in affected)
                {
                    var style = StaticStyle(subtitle, offset, state);
                    var target = new AnimationTrackTarget(property, TextRangeId: cell.Id, State: state);
                    output[target] = Sample(target, evaluated => StyleValue(
                        SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, style, offset, state), property));
                }
            }
        }
        return (layer with { Effects = [], EffectRowOrder = [], Tracks = output.Values.ToImmutableArray() },
            subtitle is null ? null : subtitle with { AnimationRanges = ranges.ToImmutable() });
    }

    private static AnimationValue NativeValue(EvaluatedLayer evaluated, ProjectLayer layer, SubtitleLine? subtitle,
        ImmutableArray<SubtitleAnimationRange> ranges, AnimationTrackTarget target)
    {
        if (target.Property == AnimationProperty.ROTATION_3D)
        {
            var range = target.TextRangeId is { } rangeId ? ranges.Single(value => value.Id == rangeId) : null;
            var fallback = range is not null ? new SceneVector3(range.RotationX, range.RotationY, range.Rotation) :
                new SceneVector3(layer.Transform.RotationX, layer.Transform.RotationY, layer.Transform.Rotation);
            var rotation = evaluated.AnimationValues.TryGetValue(target, out var canonical) ? canonical.Vector3 : fallback;
            return new SceneVector3(
                RotationComponent(evaluated, target, AnimationProperty.ROTATION_X, rotation.X),
                RotationComponent(evaluated, target, AnimationProperty.ROTATION_Y, rotation.Y),
                RotationComponent(evaluated, target, AnimationProperty.ROTATION, rotation.Z));
        }
        if (evaluated.AnimationValues.TryGetValue(target, out var value))
        {
            return value;
        }
        if (target.Property == AnimationProperty.PATH_PROGRESS && layer.MotionPath is { } path)
        {
            return MotionPathProgress.EvaluateNatural(path, evaluated.LocalTime);
        }
        return SubtitleAnimationEvaluation.GetBaseValue(layer,
            subtitle is null ? null : subtitle with { AnimationRanges = ranges }, target);
    }

    private static double RotationComponent(EvaluatedLayer evaluated, AnimationTrackTarget target,
        AnimationProperty property, double fallback)
    {
        return evaluated.AnimationValues.TryGetValue(target with { Property = property }, out var value) ? value.Scalar : fallback;
    }

    private static AnimationTrackTarget Canonical(AnimationTrackTarget target)
    {
        return target.Property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y
            ? target with { Property = AnimationProperty.ROTATION_3D } : target;
    }

    private static bool IsStyle(AnimationProperty property)
    {
        return property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING or AnimationProperty.FILL or
            AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or
            AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or
            AnimationProperty.SHADOW_COLOR;
    }

    private static SubtitleStyle StaticStyle(SubtitleLine subtitle, int offset, SubtitleAnimationState state)
    {
        var style = subtitle.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset &&
            offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(subtitle.Style) ?? subtitle.Style;
        if (state == SubtitleAnimationState.NORMAL)
        {
            return style;
        }
        var clip = subtitle.Karaoke.Concat(subtitle.InactiveKaraoke).FirstOrDefault(segment => segment.Utf16Start <= offset &&
            offset < segment.Utf16Start + segment.Utf16Length);
        var rangeStyle = KaraokeVisualStyleResolver.RangeStyleAt(subtitle, offset, state == SubtitleAnimationState.ACTIVE
            ? KaraokeVisualState.ACTIVE : KaraokeVisualState.INACTIVE);
        return state == SubtitleAnimationState.ACTIVE
            ? KaraokeVisualStyleResolver.ResolveActive(style, subtitle.KaraokeStyle, clip, rangeStyle)
            : KaraokeVisualStyleResolver.ResolveInactive(style, clip, rangeStyle);
    }

    private static AnimationValue StyleValue(SubtitleStyle style, AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.FONT_SIZE => style.FontSize,
            AnimationProperty.LETTER_SPACING => style.LetterSpacing,
            AnimationProperty.FILL => style.Fill,
            AnimationProperty.STROKE => style.Stroke,
            AnimationProperty.STROKE_WIDTH => style.StrokeWidth,
            AnimationProperty.FILL_BLUR => style.FillBlur,
            AnimationProperty.STROKE_BLUR => style.StrokeBlur,
            AnimationProperty.SHADOW_OFFSET => style.ShadowOffset,
            AnimationProperty.SHADOW_BLUR => style.ShadowBlur,
            AnimationProperty.SHADOW_COLOR => style.ShadowColor,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    private static double[] Components(AnimationValue value)
    {
        return Enumerable.Range(0, value.ComponentCount).Select(component =>
        {
            var number = value.GetComponent(component);
            if (!value.IsColor || component == 3)
            {
                return number;
            }
            number = Math.Clamp(number, 0, 1);
            return number <= 0.0031308 ? number * 12.92 : 1.055 * Math.Pow(number, 1 / 2.4) - 0.055;
        }).ToArray();
    }

    private static bool Different(AnimationValue first, AnimationValue second)
    {
        return Enumerable.Range(0, first.ComponentCount).Any(component =>
            Math.Abs(first.GetComponent(component) - second.GetComponent(component)) > 1e-12);
    }
}
