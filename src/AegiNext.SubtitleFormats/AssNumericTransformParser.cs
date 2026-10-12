using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssNumericTransformParser
{
    private readonly SubtitleLine original;
    private readonly Dictionary<string, AssNumericImportChannel> channels;
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];
    private readonly double blurMaximum;
    private bool observed;
    private bool hasBorder;
    private bool hasNoBorder;
    private bool hasBlur;
    private bool hasShadow;
    private int revision;
    private int observedRevision = -1;
    private int observedCandidate;

    internal AssNumericTransformParser(SubtitleLine original, ScenePoint scale, double rotation, double blurMaximum = double.PositiveInfinity)
    {
        this.original = original;
        this.blurMaximum = blurMaximum;
        channels = new()
        {
            ["fsp"] = new(original.Style.LetterSpacing), ["bord"] = new(original.Style.StrokeWidth), ["blur"] = new(0),
            ["fscx"] = new(scale.X), ["fscy"] = new(scale.Y), ["frz"] = new(rotation)
        };
    }

    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal void Set(string name, double value)
    {
        channels[Canonical(name)].Set(value);
        revision++;
    }

    internal void Reset(SubtitleStyle style, ScenePoint scale, double rotation)
    {
        Set("fsp", style.LetterSpacing);
        Set("bord", style.StrokeWidth);
        Set("blur", 0);
        Set("fscx", scale.X);
        Set("fscy", scale.Y);
        Set("frz", rotation);
    }

    internal void Add(string name, AssTransformTiming timing, double value, int karaokeCandidate = 0)
    {
        channels[Canonical(name)].Add(timing, value, karaokeCandidate);
        revision++;
    }

    internal void Observe(SubtitleStyle style, int excludedCandidate)
    {
        observed = true;
        hasShadow |= style.ShadowColor.Alpha > 0;
        if (observedRevision == revision && observedCandidate == excludedCandidate)
        {
            return;
        }
        observedRevision = revision;
        observedCandidate = excludedCandidate;
        foreach (var channel in channels.Values)
        {
            channel.Observe(excludedCandidate);
        }
        hasBorder |= channels["bord"].Values(excludedCandidate).Any(value => value > 0);
        hasNoBorder |= channels["bord"].Values(excludedCandidate).Any(value => value <= 0);
        hasBlur |= channels["blur"].Values(excludedCandidate).Any(value => value > 0);
    }

    internal ImmutableArray<AnimationTrack> Tracks(LayerTransform transform, MediaTime contentOffset)
    {
        if (!observed)
        {
            return [];
        }
        foreach (var pair in channels.Where(pair => pair.Value.Mixed && pair.Value.HasAnimation))
        {
            Report("Ass.InlineTransform", $"Inline ASS {pair.Key} base values or animations differ and cannot be stored as whole-line animation. Only this property animation was discarded.");
        }
        var tracks = ImmutableArray.CreateBuilder<AnimationTrack>();
        var appearanceScale = Math.Sqrt(transform.Scale.X * transform.Scale.Y);
        if (appearanceScale == 0)
        {
            appearanceScale = 1;
        }
        AddScalar(tracks, "fsp", AnimationProperty.LETTER_SPACING, value => value);
        AddScalar(tracks, "bord", AnimationProperty.STROKE_WIDTH, value => value / appearanceScale);
        AddScalar(tracks, "frz", AnimationProperty.ROTATION, value => -value);
        if (Usable("blur"))
        {
            if (hasBorder && hasNoBorder)
            {
                Report("Ass.TransformAppearanceAnimation", "ASS blur animation changes its target depending on whether a stroke exists and cannot map to a single native blur track. Blur animation was discarded; stroke and other properties were preserved.");
            }
            else
            {
                AddScalar(tracks, "blur", hasBorder ? AnimationProperty.STROKE_BLUR : AnimationProperty.FILL_BLUR,
                    value => value / appearanceScale);
            }
        }
        var scaleTrack = Scale(transform.Scale);
        if (scaleTrack is not null)
        {
            tracks.Add(scaleTrack);
        }
        if ((Usable("fscx") || Usable("fscy") || Usable("frz")) && (hasBorder || hasBlur || hasShadow) ||
            Usable("bord") && hasBlur)
        {
            Report("Ass.TransformAppearanceAnimation", "ASS animated glyph transforms apply stroke, blur, and shadow in a different order from native whole-layer transforms. Geometry animation was preserved; edge or shadow appearance may diverge over time.");
        }
        var rebased = tracks.Select(track => track with
        {
            Keyframes = track.Keyframes.Select(frame => frame with { Time = frame.Time + contentOffset }).ToImmutableArray(),
            Transforms = track.Transforms.Select(operation => operation with { Start = operation.Start + contentOffset, End = operation.End + contentOffset }).ToImmutableArray()
        }).ToImmutableArray();
        return LayerAnimationTiming.Clip(new ProjectLayer
        {
            Start = original.Start, End = original.End, AnimationOffset = contentOffset, Tracks = rebased
        }).Tracks;
    }

    private void AddScalar(ImmutableArray<AnimationTrack>.Builder tracks, string name, AnimationProperty property, Func<double, double> convert)
    {
        if (!Usable(name))
        {
            return;
        }
        var channel = channels[name];
        if (name == "bord" && channel.Operations.Any(operation => operation.Value < 0) ||
            name == "blur" && channel.Operations.Any(operation => operation.Value < 0 || operation.Value > blurMaximum))
        {
            return;
        }
        var values = channel.Operations.Select(operation => operation.Value).Prepend(channel.Initial).Select(convert);
        if (values.Any(value => !double.IsFinite(value) || value < AnimationPropertyMetadata.GetMinimum(property) || value > AnimationPropertyMetadata.GetMaximum(property)))
        {
            Report("Ass.TransformRange", $"ASS {name} animation exceeds the native property range. This property animation was discarded and other content was preserved.");
            return;
        }
        tracks.Add(Track(property, AnimationValue.FromScalar(convert(channel.Initial)), channel.Operations,
            operation => AnimationValue.FromScalar(convert(operation.Value))));
    }

    private AnimationTrack? Scale(ScenePoint fallback)
    {
        if (RequiresScaleClamp("fscx") || RequiresScaleClamp("fscy"))
        {
            return null;
        }
        var horizontal = ScaleAxis("fscx");
        var vertical = ScaleAxis("fscy");
        if (!horizontal && !vertical)
        {
            return null;
        }
        var x = channels["fscx"];
        var y = channels["fscy"];
        var initial = new ScenePoint(horizontal ? x.Initial : fallback.X, vertical ? y.Initial : fallback.Y);
        if (!horizontal || !vertical)
        {
            var channel = horizontal ? x : y;
            return Track(AnimationProperty.SCALE, AnimationValue.FromVector(initial), channel.Operations,
                operation => AnimationValue.FromVector(horizontal ? new(operation.Value, initial.Y) : new(initial.X, operation.Value)));
        }
        if (x.Operations.Length == y.Operations.Length && x.Operations.Zip(y.Operations).All(pair => pair.First.Timing == pair.Second.Timing))
        {
            var targets = x.Operations.Select((operation, index) => new ScenePoint(operation.Value, y.Operations[index].Value)).ToArray();
            var index = 0;
            return Track(AnimationProperty.SCALE, AnimationValue.FromVector(initial), x.Operations,
                _ => AnimationValue.FromVector(targets[index++]));
        }
        if (Continuous(x) && Continuous(y))
        {
            var axes = LayerAnimationTiming.Clip(new ProjectLayer
            {
                Start = original.Start, End = original.End,
                Tracks =
                [
                    Track(AnimationProperty.SCALE_X, AnimationValue.FromScalar(x.Initial), x.Operations, operation => AnimationValue.FromScalar(operation.Value)),
                    Track(AnimationProperty.SCALE_Y, AnimationValue.FromScalar(y.Initial), y.Operations, operation => AnimationValue.FromScalar(operation.Value))
                ]
            }).Tracks;
            return LegacyAnimationTrackMigration.Merge(axes, AnimationProperty.SCALE, AnimationValue.FromVector(initial)).Single();
        }
        Report("Ass.TransformScaleAxes", "ASS horizontal and vertical scaling contain overlapping or instantaneous operations with independent timing and cannot map exactly to native whole-vector operations. Scaling animation was discarded and other properties were preserved.");
        return null;
    }

    private bool RequiresScaleClamp(string name) => Usable(name) && channels[name].Operations.Any(operation => operation.Value < 0);

    private bool ScaleAxis(string name)
    {
        if (!Usable(name))
        {
            return false;
        }
        var channel = channels[name];
        if (channel.Operations.Select(operation => operation.Value).Prepend(channel.Initial)
            .Any(value => !double.IsFinite(value) || value is < 0 or > 10000))
        {
            Report("Ass.TransformRange", $"ASS {name} animation exceeds the native range. This axis animation was discarded; the other axis and other properties were preserved.");
            return false;
        }
        return true;
    }

    private bool Usable(string name) => !channels[name].Mixed && !channels[name].Operations.IsEmpty;

    private static bool Continuous(AssNumericImportChannel channel) => channel.Operations.Length == 1 &&
        channel.Operations[0].Timing.End > channel.Operations[0].Timing.Start && channel.Operations[0].Timing.Acceleration > 0;

    private static AnimationTrack Track(AnimationProperty property, AnimationValue initial,
        ImmutableArray<AssNumericImportOperation> operations, Func<AssNumericImportOperation, AnimationValue> target)
    {
        if (operations.Length == 1 && operations[0].Timing is var timing && timing.End > timing.Start && timing.Acceleration > 0)
        {
            return new(property,
            [
                new(timing.Start, initial, KeyframeInterpolation.POWER) { Exponent = timing.Acceleration },
                new(timing.End, target(operations[0]))
            ]);
        }
        return new(property, [])
        {
            InitialValue = initial,
            Transforms = operations.Select(operation => new AnimationTransformOperation(Guid.NewGuid(), operation.Timing.Start,
                operation.Timing.End, target(operation), operation.Timing.Acceleration)).ToImmutableArray()
        };
    }

    private static string Canonical(string name) => name == "fr" ? "frz" : name;

    private void Report(string code, string message)
    {
        if (!diagnostics.Any(diagnostic => diagnostic.Code == code && diagnostic.Message == message))
        {
            diagnostics.Add(new(code, message, 0, 0, original.Id));
        }
    }
}
