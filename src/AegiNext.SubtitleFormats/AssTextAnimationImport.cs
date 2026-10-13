using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssTextAnimationImport
{
    private readonly SubtitleLine original;
    private readonly Dictionary<string, AssTextAnimationChannel> channels;
    private readonly List<AssTextAnimationRun> runs = [];
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];
    private readonly HashSet<(string Code, string Message)> reported = [];
    private int revision;
    private int observedRevision = -1;
    private int observedCandidate;
    private bool observedKaraoke;
    private bool normalizedScale;
    private bool normalizedProjection;
    private readonly bool sourceConstraints;

    internal AssTextAnimationImport(SubtitleLine original, SceneColor secondary, ScenePoint scale, double rotation, bool sourceConstraints = true,
        double rotationX = 0, double rotationY = 0)
    {
        this.original = original;
        this.sourceConstraints = sourceConstraints;
        channels = new()
        {
            ["fs"] = new(original.Style.FontSize), ["fsp"] = new(original.Style.LetterSpacing),
            ["bord"] = new(original.Style.StrokeWidth), ["blur"] = new(0),
            ["scale"] = new(scale), ["frz"] = new(-rotation), ["frx"] = new(-rotationX), ["fry"] = new(-rotationY),
            ["shadow"] = new(original.Style.ShadowOffset), ["1c"] = new(original.Style.Fill),
            ["2c"] = new(secondary), ["3c"] = new(original.Style.Stroke), ["4c"] = new(original.Style.ShadowColor)
        };
    }

    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal bool HasShadowAnimation => !channels["shadow"].Snapshot(0).Operations.IsEmpty;

    internal AssRotationOriginGeometry RotationOriginGeometry() => AssRotationOriginGeometry.FromRuns(runs, original.End - original.Start);

    internal bool TryFixedLayout(SubtitleLine line, out SubtitleLine measuredLine)
    {
        measuredLine = line;
        var typography = new List<(AssTextAnimationRun Run, double Size, double Spacing)>();
        foreach (var run in runs)
        {
            if (!AssGeometryAnimationWindow.TryConstant(run.Channels["fs"], original.End - original.Start, out var size) ||
                !AssGeometryAnimationWindow.TryConstant(run.Channels["fsp"], original.End - original.Start, out var spacing) ||
                !double.IsFinite(size.Scalar) || size.Scalar is < 0.01 or > 4096 ||
                !double.IsFinite(spacing.Scalar) || spacing.Scalar is < -4096 or > 4096)
            {
                return false;
            }
            typography.Add((run, size.Scalar, spacing.Scalar));
        }
        if (typography.Count == 0)
        {
            return false;
        }
        var boundaries = line.InlineSpans.SelectMany(span => new[] { span.Utf16Start, span.Utf16Start + span.Utf16Length })
            .Concat(runs.SelectMany(run => new[] { run.Utf16Start, run.Utf16Start + run.Utf16Length }))
            .Append(0).Append(line.Text.Length).Distinct().Order().ToArray();
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        var runIndex = 0;
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            while (runIndex < typography.Count && typography[runIndex].Run.Utf16Start + typography[runIndex].Run.Utf16Length <= start)
            {
                runIndex++;
            }
            var style = line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= start &&
                start < span.Utf16Start + span.Utf16Length)?.Style ?? new SubtitleInlineStyleOverride();
            if (runIndex < typography.Count && typography[runIndex].Run.Utf16Start <= start)
            {
                var run = typography[runIndex];
                style = style with { FontSize = run.Size, LetterSpacing = run.Spacing };
            }
            if (spans.Count > 0 && spans[^1].Style == style && spans[^1].Utf16Start + spans[^1].Utf16Length == start)
            {
                spans[^1] = spans[^1] with { Utf16Length = end - spans[^1].Utf16Start };
            }
            else
            {
                spans.Add(new(start, end - start, style));
            }
        }
        measuredLine = line with { InlineSpans = spans.ToImmutable() };
        return true;
    }

    internal void Observe(int offset, int length, bool karaoke, int excludedCandidate)
    {
        if (runs.Count > 0 && observedRevision == revision && observedCandidate == excludedCandidate &&
            observedKaraoke == karaoke && runs[^1].Utf16Start + runs[^1].Utf16Length == offset)
        {
            runs[^1] = runs[^1] with { Utf16Length = runs[^1].Utf16Length + length };
            return;
        }
        observedRevision = revision;
        observedCandidate = excludedCandidate;
        observedKaraoke = karaoke;
        var next = new AssTextAnimationRun(offset, length, karaoke,
            channels.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Snapshot(excludedCandidate)));
        if (runs.Count > 0 && runs[^1].Utf16Start + runs[^1].Utf16Length == offset && runs[^1].Equivalent(next))
        {
            runs[^1] = runs[^1] with { Utf16Length = runs[^1].Utf16Length + length };
        }
        else
        {
            runs.Add(next);
        }
    }

    internal void Set(string name, SubtitleStyle style, SceneColor secondary, ScenePoint scale, double rotation, double blur,
        double rotationX = 0, double rotationY = 0)
    {
        revision++;
        switch (name)
        {
            case "fs": channels["fs"].Set(style.FontSize); break;
            case "fsp": channels["fsp"].Set(style.LetterSpacing); break;
            case "bord": channels["bord"].Set(style.StrokeWidth); break;
            case "blur": channels["blur"].Set(blur); break;
            case "fsc": channels["scale"].Set(scale); break;
            case "fscx": channels["scale"].Set(scale, 1); break;
            case "fscy": channels["scale"].Set(scale, 2); break;
            case "fr":
            case "frz": channels["frz"].Set(-rotation); break;
            case "frx": channels["frx"].Set(-rotationX); break;
            case "fry": channels["fry"].Set(-rotationY); break;
            case "shad": channels["shadow"].Set(style.ShadowOffset); break;
            case "xshad": channels["shadow"].Set(style.ShadowOffset, 1); break;
            case "yshad": channels["shadow"].Set(style.ShadowOffset, 2); break;
            case "c":
            case "1c": channels["1c"].Set(style.Fill, 7); break;
            case "2c": channels["2c"].Set(secondary, 7); break;
            case "3c": channels["3c"].Set(style.Stroke, 7); break;
            case "4c": channels["4c"].Set(style.ShadowColor, 7); break;
            case "1a": channels["1c"].Set(style.Fill, 8); break;
            case "2a": channels["2c"].Set(secondary, 8); break;
            case "3a": channels["3c"].Set(style.Stroke, 8); break;
            case "4a": channels["4c"].Set(style.ShadowColor, 8); break;
            case "alpha":
                channels["1c"].Set(style.Fill, 8);
                channels["2c"].Set(secondary, 8);
                channels["3c"].Set(style.Stroke, 8);
                channels["4c"].Set(style.ShadowColor, 8);
                break;
            case "r":
                foreach (var key in channels.Keys)
                {
                    channels[key].Set(key switch
                    {
                        "fs" => style.FontSize, "fsp" => style.LetterSpacing, "bord" => style.StrokeWidth,
                        "blur" => 0, "scale" => scale, "frz" => -rotation, "frx" => -rotationX, "fry" => -rotationY,
                        "shadow" => style.ShadowOffset,
                        "1c" => style.Fill, "2c" => secondary, "3c" => style.Stroke, _ => style.ShadowColor
                    });
                }
                break;
        }
    }

    internal void AddNumeric(string name, AssTransformTiming timing, double value, int candidate = 0, double? maximum = null)
    {
        revision++;
        var key = name is "fscx" or "fscy" ? "scale" : name == "fr" ? "frz" : name;
        AnimationValue target = name is "fscx" or "fscy" ? new ScenePoint(value, value) : name is "fr" or "frz" or "frx" or "fry" ? -value : value;
        channels[key].Add(new(timing, target, name == "fscx" ? 1 : name == "fscy" ? 2 : 0, KaraokeCandidate: candidate,
            ClampNonNegative: sourceConstraints && name is "fscx" or "fscy" or "bord" or "blur",
            Maximum: sourceConstraints ? maximum : null));
    }

    internal bool AddStyle(string name, string value, AssTransformTiming timing, double scaleY,
        AssResolutionContext resolution, double resetFontSize, int candidate = 0)
    {
        revision++;
        if (name == "fs")
        {
            var relative = value[0] is '+' or '-';
            var amount = AssFormatValues.Number(value);
            channels["fs"].Add(new(timing, relative ? 1 + amount / 10 : amount * scaleY,
                Mode: relative ? AnimationTransformMode.MULTIPLY_BY : AnimationTransformMode.INTERPOLATE_TO,
                NonPositiveFallback: sourceConstraints ? resetFontSize : null));
            return true;
        }
        if (name is "shad" or "xshad" or "yshad")
        {
            var amount = AssFormatValues.Number(value);
            var target = resolution.Shadow(amount);
            channels["shadow"].Add(new(timing, target, name == "xshad" ? 1 : name == "yshad" ? 2 : 0,
                KaraokeCandidate: candidate, ClampNonNegative: sourceConstraints && name == "shad"));
            return true;
        }
        if (name is "c" or "1c" or "2c" or "3c" or "4c")
        {
            channels[name == "c" ? "1c" : name].Add(new(timing, AssFormatValues.Color(value, SceneColor.White), 7, KaraokeCandidate: candidate));
            return true;
        }
        if (name is "alpha" or "1a" or "2a" or "3a" or "4a")
        {
            var target = SceneColor.White with { Alpha = AssFormatValues.Alpha(value) };
            if (name == "alpha")
            {
                for (var component = 1; component <= 4; component++)
                {
                    channels[component + "c"].Add(new(timing, target, 8, KaraokeCandidate: candidate));
                }
            }
            else
            {
                channels[name[0] + "c"].Add(new(timing, target, 8, KaraokeCandidate: candidate));
            }
            return true;
        }
        return false;
    }

    internal LayerTransform NormalizeTransform(LayerTransform transform)
    {
        var rotationKeys = new[] { "frz", "frx", "fry" };
        var hasPerspective = runs.Any(run => rotationKeys.Skip(1).Any(key =>
            run.Channels[key].Initial.Scalar != 0 || run.Channels[key].Operations.Any(operation => operation.Value.Scalar != 0)));
        normalizedProjection = hasPerspective && runs.Count > 0 && rotationKeys.Append("scale").Any(key =>
            runs.Skip(1).Any(run => !run.Channels[key].Equivalent(runs[0].Channels[key])));
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var snapshot = run.Channels["scale"];
            var normalized = NormalizeScale(snapshot, transform.Scale);
            if (!normalized.Equivalent(snapshot))
            {
                normalizedScale = true;
                runs[index] = run with { Channels = run.Channels.SetItem("scale", normalized) };
            }
            foreach (var key in rotationKeys)
            {
                run = runs[index];
                runs[index] = run with { Channels = run.Channels.SetItem(key, NormalizeRotation(run.Channels[key], key)) };
            }
        }
        if (normalizedProjection)
        {
            return transform with { Scale = new(1, 1), Rotation = 0, RotationX = 0, RotationY = 0 };
        }
        var mixed = runs.Count > 0 && runs.Skip(1).Any(run => !run.Channels["scale"].Equivalent(runs[0].Channels["scale"]));
        var staticGeometry = runs.Any(run => run.Channels["scale"].Initial.Vector != transform.Scale);
        if (!mixed && !staticGeometry)
        {
            return transform;
        }
        return transform with
        {
            Scale = new(ScaleNeedsUnitBasis(transform.Scale.X, 0) ? 1 : transform.Scale.X,
                ScaleNeedsUnitBasis(transform.Scale.Y, 1) ? 1 : transform.Scale.Y)
        };
    }

    internal (SubtitleLine Line, ImmutableArray<AnimationTrack> Tracks) Convert(SubtitleLine line,
        ImmutableArray<AnimationTrack> existing, LayerTransform transform, MediaTime contentOffset)
    {
        var ranges = ImmutableArray.CreateBuilder<SubtitleAnimationRange>();
        var tracks = existing.ToBuilder();
        var rangeIds = new Dictionary<int, Guid>();
        foreach (var key in channels.Keys)
        {
            var property = Property(key);
            var animated = runs.Any(run => !run.Channels[key].Operations.IsEmpty);
            var mixed = runs.Count > 0 && runs.Skip(1).Any(run => !run.Channels[key].Equivalent(runs[0].Channels[key]) ||
                key is "1c" or "2c" && run.Karaoke != runs[0].Karaoke);
            var staticGeometry = normalizedProjection && (key is "scale" or "frz" or "frx" or "fry") ||
                runs.Any(run => key == "scale" && run.Channels[key].Initial.Vector != transform.Scale ||
                    key == "frz" && run.Channels[key].Initial.Scalar != transform.Rotation ||
                    key == "frx" && run.Channels[key].Initial.Scalar != transform.RotationX ||
                    key == "fry" && run.Channels[key].Initial.Scalar != transform.RotationY);
            var replaceGeometry = key == "scale" && (normalizedScale || mixed || staticGeometry) ||
                normalizedProjection && (key is "frz" or "frx" or "fry");
            if (!animated && !staticGeometry || existing.Any(track => track.Property == property) && key != "blur" && !replaceGeometry)
            {
                continue;
            }
            if (replaceGeometry)
            {
                var wholeLineTarget = new AnimationTrackTarget(property);
                for (var index = tracks.Count - 1; index >= 0; index--)
                {
                    if (tracks[index].Target == wholeLineTarget)
                    {
                        tracks.RemoveAt(index);
                    }
                }
            }
            if (key == "blur" && runs.Any(BorderCrossesZero))
            {
                Report("Ass.TransformAppearanceAnimation", "ASS blur animation crosses a zero stroke width and changes its target over time. Blur animation was omitted and other animations were preserved.");
                continue;
            }
            foreach (var runIndex in mixed || staticGeometry ? Enumerable.Range(0, runs.Count) : Enumerable.Range(0, Math.Min(runs.Count, 1)))
            {
                var run = runs[runIndex];
                var snapshot = run.Channels[key];
                if (snapshot.Operations.IsEmpty && !staticGeometry || key == "2c" && !run.Karaoke)
                {
                    continue;
                }
                Guid? rangeId = null;
                if (mixed || staticGeometry)
                {
                    if (!rangeIds.TryGetValue(runIndex, out var id))
                    {
                        if (rangeIds.Count >= 256)
                        {
                            Report("Ass.TextRangeBudget", "Visible ASS animation ranges exceed the 256-ranges-per-subtitle budget. Additional range animations were omitted; text and static styles were preserved.");
                            continue;
                        }
                        id = Guid.NewGuid();
                        rangeIds.Add(runIndex, id);
                    }
                    rangeId = id;
                }
                var state = key == "2c" ? SubtitleAnimationState.INACTIVE : key == "1c" && run.Karaoke
                    ? SubtitleAnimationState.ACTIVE : SubtitleAnimationState.NORMAL;
                var targetProperty = key == "blur" ? run.Channels["bord"].Initial.Scalar > 0 ? AnimationProperty.STROKE_BLUR : AnimationProperty.FILL_BLUR : property;
                if (snapshot.Operations.IsEmpty)
                {
                    continue;
                }
                if (!tracks.Any(track => track.Property == targetProperty && track.Target.TextRangeId == rangeId && track.Target.State == state))
                {
                    AddTrack(tracks, snapshot, targetProperty, rangeId, state, key, transform, contentOffset);
                }
                if (key == "blur")
                {
                    var blurTarget = new AnimationTrackTarget(targetProperty, TextRangeId: rangeId, State: state);
                    var blur = tracks.LastOrDefault(track => track.Target == blurTarget);
                    if (blur is not null)
                    {
                        tracks.Add(blur with
                        {
                            Target = blurTarget with { Property = AnimationProperty.SHADOW_BLUR },
                            Transforms = blur.Transforms.Select(operation => operation with { Id = Guid.NewGuid() }).ToImmutableArray()
                        });
                    }
                }
            }
        }
        foreach (var pair in rangeIds.OrderBy(pair => pair.Key))
        {
            var run = runs[pair.Key];
            var baseScale = run.Channels["scale"].Initial.Vector;
            ranges.Add(new(pair.Value, run.Utf16Start, run.Utf16Length)
            {
                Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR,
                Scale = new(baseScale.X == transform.Scale.X ? 1 : baseScale.X / (transform.Scale.X == 0 ? 1 : transform.Scale.X),
                    baseScale.Y == transform.Scale.Y ? 1 : baseScale.Y / (transform.Scale.Y == 0 ? 1 : transform.Scale.Y)),
                Rotation = run.Channels["frz"].Initial.Scalar - transform.Rotation,
                RotationX = run.Channels["frx"].Initial.Scalar - transform.RotationX,
                RotationY = run.Channels["fry"].Initial.Scalar - transform.RotationY,
                PerspectiveDistance = transform.PerspectiveDistance
            });
        }
        var result = line with { AnimationRanges = ranges.ToImmutable() };
        if (result.AnimationRanges.Any(range => range.Scale != new ScenePoint(1, 1) || range.Rotation != 0 || range.RotationX != 0 || range.RotationY != 0) ||
            tracks.Any(track => track.Target.TextRangeId is not null && track.Property is AnimationProperty.SCALE or
                AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y))
        {
            Report("Ass.TextRangeGeometry", "ASS segment transforms were stored as text ranges. Native transforms after layout may differ from the ASS order of glyph, edge, shadow, and shared layout operations.");
        }
        return (result, LayerAnimationTiming.Clip(new ProjectLayer
        {
            Start = original.Start, End = original.End, AnimationOffset = contentOffset, Tracks = tracks.ToImmutable()
        }).Tracks);
    }

    private AssTextAnimationSnapshot NormalizeRotation(AssTextAnimationSnapshot snapshot, string key)
    {
        static bool Valid(double value) => double.IsFinite(value) && Math.Abs(value) <= 1e9;
        var initial = Valid(snapshot.Initial.Scalar) ? snapshot.Initial : AnimationValue.FromScalar(0);
        var operations = snapshot.Operations.Where(operation => Valid(operation.Value.Scalar)).ToImmutableArray();
        if (initial != snapshot.Initial || operations.Length != snapshot.Operations.Length)
        {
            Report("Ass.TransformRange", $"ASS {key} rotation exceeds the native range. Invalid base values reverted to zero and invalid operations were omitted; other properties were preserved.");
        }
        return new(initial, operations);
    }

    private AssTextAnimationSnapshot NormalizeScale(AssTextAnimationSnapshot snapshot, ScenePoint fallback)
    {
        var initial = snapshot.Initial;
        for (var component = 0; component < initial.ComponentCount; component++)
        {
            if (!ValidScale(initial.GetComponent(component), component))
            {
                var fallbackValue = component == 0 ? fallback.X : fallback.Y;
                initial = initial.WithComponent(component, ValidScale(fallbackValue, component) ? fallbackValue : 1);
                Report("Ass.TransformRange", "ASS glyph scaling base values exceed the native range. This axis reverted to its fallback; the other axis and other content were preserved.");
            }
        }
        var operations = ImmutableArray.CreateBuilder<AssTextAnimationOperation>();
        foreach (var operation in snapshot.Operations)
        {
            var value = operation.Value;
            var mask = operation.ComponentMask == 0 ? (1 << value.ComponentCount) - 1 : operation.ComponentMask;
            var remaining = mask;
            for (var component = 0; component < value.ComponentCount; component++)
            {
                if (!ValidScale(operation.ClampNonNegative ? Math.Max(value.GetComponent(component), 0) : value.GetComponent(component), component))
                {
                    var bit = 1 << component;
                    if ((mask & bit) != 0)
                    {
                        remaining &= ~bit;
                        Report("Ass.TransformRange", "ASS glyph scaling animation exceeds the native range. This axis operation was omitted; the other axis and other content were preserved.");
                    }
                    value = value.WithComponent(component, initial.GetComponent(component));
                }
            }
            if (remaining != 0)
            {
                operations.Add(operation with
                {
                    Value = value,
                    ComponentMask = remaining == mask ? operation.ComponentMask : remaining
                });
            }
        }
        return new(initial, operations.ToImmutable());
    }

    private static bool ValidScale(double value, int component) => double.IsFinite(value) && value >= 0 &&
        value <= AnimationPropertyMetadata.GetMaximum(AnimationProperty.SCALE, component);

    private bool ScaleNeedsUnitBasis(double basis, int component)
    {
        if (!double.IsFinite(basis) || basis == 0)
        {
            return true;
        }
        return runs.Any(run =>
        {
            var snapshot = run.Channels["scale"];
            return !ValidScale(snapshot.Initial.GetComponent(component) / basis, component) ||
                snapshot.Operations.Any(operation => !ValidScale((operation.ClampNonNegative
                    ? Math.Max(operation.Value.GetComponent(component), 0) : operation.Value.GetComponent(component)) / basis, component));
        });
    }

    private void AddTrack(ImmutableArray<AnimationTrack>.Builder tracks, AssTextAnimationSnapshot snapshot,
        AnimationProperty property, Guid? rangeId, SubtitleAnimationState state, string key, LayerTransform transform, MediaTime offset)
    {
        var appearanceScale = Math.Sqrt(Math.Abs(transform.Scale.X * transform.Scale.Y));
        if (appearanceScale == 0)
        {
            appearanceScale = 1;
        }
        AnimationValue ConvertValue(AnimationValue value, AnimationTransformMode mode)
        {
            if (mode == AnimationTransformMode.MULTIPLY_BY)
            {
                return value;
            }
            if (key is "bord" or "blur")
            {
                return value.Scalar / appearanceScale;
            }
            if (key == "shadow")
            {
                var rotation = AssTransformMath.SinCos(transform.Rotation);
                return new ScenePoint((rotation.Cosine * value.Vector.X + rotation.Sine * value.Vector.Y) /
                    (transform.Scale.X == 0 ? 1 : transform.Scale.X),
                    (-rotation.Sine * value.Vector.X + rotation.Cosine * value.Vector.Y) /
                    (transform.Scale.Y == 0 ? 1 : transform.Scale.Y));
            }
            if (key == "scale" && rangeId is not null)
            {
                return new ScenePoint(value.Vector.X / (transform.Scale.X == 0 ? 1 : transform.Scale.X),
                    value.Vector.Y / (transform.Scale.Y == 0 ? 1 : transform.Scale.Y));
            }
            return rangeId is null ? value : key switch
            {
                "frz" => value.Scalar - transform.Rotation,
                "frx" => value.Scalar - transform.RotationX,
                "fry" => value.Scalar - transform.RotationY,
                _ => value
            };
        }
        var track = new AnimationTrack(new AnimationTrackTarget(property, TextRangeId: rangeId, State: state), [])
        {
            InitialValue = ConvertValue(snapshot.Initial, AnimationTransformMode.INTERPOLATE_TO),
            ColorSpace = snapshot.Initial.IsColor ? AnimationColorSpace.SRGB : AnimationColorSpace.LINEAR_RGB,
            Transforms = snapshot.Operations.Select(operation => new AnimationTransformOperation(Guid.NewGuid(),
                operation.Timing.Start + offset, operation.Timing.End + offset, ConvertValue(operation.Value, operation.Mode), operation.Timing.Acceleration)
            {
                ComponentMask = operation.ComponentMask, Mode = operation.Mode
            }).ToImmutableArray()
        };
        var minimum = key == "fs" ? AnimationPropertyMetadata.GetMinimum(AnimationProperty.FONT_SIZE) : double.NegativeInfinity;
        if (AssSourceAnimationEvaluator.NeedsSampling(snapshot, minimum))
        {
            double[] Components(AnimationValue value) => Enumerable.Range(0, value.ComponentCount).Select(value.GetComponent).ToArray();
            var discontinuities = AssSourceAnimationEvaluator.Discontinuities(snapshot).ToArray();
            var samples = AssAnimationSampler.Sample(track, Components,
                offset, offset + original.End - original.Start,
                time => AssSourceAnimationEvaluator.Evaluate(snapshot, time - offset),
                time => AssSourceAnimationEvaluator.Evaluate(snapshot, time - offset, true),
                discontinuities.Select(time => time + offset));
            var belowFontFloor = key == "fs" && (samples.Frames.Any(frame => frame.Value.Scalar < minimum) ||
                discontinuities.Any(time => time >= MediaTime.Zero && time <= original.End - original.Start &&
                    AssSourceAnimationEvaluator.Evaluate(snapshot, time, true).Scalar < minimum));
            if (belowFontFloor)
            {
                Report("Ass.FontSizeRange", "ASS continuous font size passes through positive values below the native minimum of 0.01. It was clamped to the native minimum; per-operation style resets were preserved.");
            }
            track = track with
            {
                InitialValue = null, Transforms = [],
                Keyframes = samples.Frames.Select(frame => frame with
                {
                    Value = ConvertValue(key == "fs" ? Math.Max(frame.Value.Scalar, minimum) : frame.Value,
                        AnimationTransformMode.INTERPOLATE_TO)
                }).ToImmutableArray()
            };
            Report(samples.Limited ? "Ass.AnimationSamplingLimit" : "Ass.AnimationSampling",
                samples.Limited ? $"ASS {key} per-operation constraint sampling reached the 1-millisecond, breakpoint quantization, or 4096-point limit. Errors in some intervals may exceed 1/255." :
                    $"ASS {key} interpolated per-operation constraints were sampled as native animation. Errors in checked source-coordinate components do not exceed 1/255, with a minimum interval of 1 millisecond.");
        }
        else if (key == "shadow" && transform.Rotation != 0 && snapshot.Operations.Any(operation => operation.ComponentMask != 0))
        {
            var source = track with
            {
                InitialValue = snapshot.Initial,
                Transforms = snapshot.Operations.Select(operation => new AnimationTransformOperation(Guid.NewGuid(),
                    operation.Timing.Start + offset, operation.Timing.End + offset, operation.Value, operation.Timing.Acceleration)
                {
                    ComponentMask = operation.ComponentMask
                }).ToImmutableArray()
            };
            double[] Components(AnimationValue value)
            {
                var converted = ConvertValue(value, AnimationTransformMode.INTERPOLATE_TO).Vector;
                return [converted.X, converted.Y];
            }
            var samples = AssAnimationSampler.Sample(source, Components, offset, offset + original.End - original.Start);
            track = track with
            {
                InitialValue = null, Transforms = [],
                Keyframes = samples.Frames.Select(frame => frame with
                {
                    Value = ConvertValue(frame.Value, AnimationTransformMode.INTERPOLATE_TO)
                }).ToImmutableArray()
            };
            Report(samples.Limited ? "Ass.AnimationSamplingLimit" : "Ass.AnimationSampling",
                samples.Limited ? "Conversion sampling for independent shadow axes under rotation reached the 1-millisecond or 4096-point limit. Errors in some intervals may exceed 1/255." :
                    "Independent shadow axes under rotation were sampled as native vectors. Errors in checked components do not exceed 1/255, with a minimum interval of 1 millisecond.");
        }
        track = LayerAnimationTiming.Clip(new ProjectLayer
        {
            Start = original.Start, End = original.End, AnimationOffset = offset, Tracks = [track]
        }).Tracks[0];
        try
        {
            var range = rangeId is { } id ? new SubtitleAnimationRange(id, 0, Math.Max(original.Text.Length, 1)) : null;
            var validationLine = original with { Text = original.Text.Length == 0 ? "a" : original.Text, AnimationRanges = range is null ? [] : [range] };
            ProjectValidator.Validate(new() { Subtitles = [validationLine], Layers = [new() { Id = original.Id, SubtitleId = original.Id, Start = original.Start, End = original.End, AnimationOffset = offset, Tracks = [track] }] });
            tracks.Add(track);
        }
        catch (InvalidDataException)
        {
            Report("Ass.TransformRange", $"ASS {key} animation exceeds the native range or time budget. The track was discarded and other content was preserved.");
        }
    }

    private static bool BorderCrossesZero(AssTextAnimationRun run)
    {
        var border = run.Channels["bord"];
        var values = border.Operations.Select(operation => operation.Value.Scalar).Prepend(border.Initial.Scalar).ToArray();
        return values.Any(value => value > 0) && values.Any(value => value <= 0);
    }

    private static AnimationProperty Property(string key) => key switch
    {
        "fs" => AnimationProperty.FONT_SIZE, "fsp" => AnimationProperty.LETTER_SPACING,
        "bord" => AnimationProperty.STROKE_WIDTH, "blur" => AnimationProperty.STROKE_BLUR,
        "scale" => AnimationProperty.SCALE, "frz" => AnimationProperty.ROTATION,
        "frx" => AnimationProperty.ROTATION_X, "fry" => AnimationProperty.ROTATION_Y,
        "shadow" => AnimationProperty.SHADOW_OFFSET, "1c" or "2c" => AnimationProperty.FILL,
        "3c" => AnimationProperty.STROKE, _ => AnimationProperty.SHADOW_COLOR
    };

    private void Report(string code, string message)
    {
        if (reported.Add((code, message)))
        {
            diagnostics.Add(new(code, message, SubtitleId: original.Id));
        }
    }
}
