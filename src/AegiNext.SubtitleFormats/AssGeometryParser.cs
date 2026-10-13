using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssGeometryParser
{
    private readonly SubtitleLine original;
    private readonly IReadOnlyDictionary<string, AssStyleDefinition> styles;
    private readonly double canvasScaleX;
    private readonly double canvasScaleY;
    private readonly bool sourceConstraints;
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];
    private AssStyleDefinition baseline;
    private ScenePoint currentScale;
    private double currentRotation;
    private double currentRotationX;
    private double currentRotationY;
    private ScenePoint observedScale;
    private double observedRotation;
    private double observedRotationX;
    private double observedRotationY;
    private bool observed;
    private bool mixedScaleX;
    private bool mixedScaleY;
    private bool mixedRotation;
    private bool mixedRotationX;
    private bool mixedRotationY;
    private bool hasPlacement;
    private ScenePoint? placement;
    private AnimationTrack? move;

    internal AssGeometryParser(SubtitleLine original, IReadOnlyDictionary<string, AssStyleDefinition> styles,
        double canvasScaleX, double canvasScaleY, bool sourceConstraints = true)
    {
        this.original = original;
        this.styles = styles;
        this.canvasScaleX = canvasScaleX;
        this.canvasScaleY = canvasScaleY;
        this.sourceConstraints = sourceConstraints;
        baseline = OriginalDefinition();
        currentScale = baseline.Scale;
        currentRotation = baseline.Rotation;
    }

    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;
    internal ScenePoint CurrentScale => currentScale;
    internal double CurrentRotation => currentRotation;
    internal double CurrentRotationX => currentRotationX;
    internal double CurrentRotationY => currentRotationY;
    internal ScenePoint? Placement => placement;

    internal void Reset(string name)
    {
        baseline = name.Length > 0 && styles.TryGetValue(name, out var style) ? style : OriginalDefinition();
        currentScale = baseline.Scale;
        currentRotation = baseline.Rotation;
        currentRotationX = 0;
        currentRotationY = 0;
    }

    internal void Apply(string name, string value)
    {
        switch (name)
        {
            case "fsc":
                currentScale = baseline.Scale;
                break;
            case "fscx":
                currentScale = currentScale with { X = value.Length == 0 ? baseline.Scale.X : Scale(value) };
                break;
            case "fscy":
                currentScale = currentScale with { Y = value.Length == 0 ? baseline.Scale.Y : Scale(value) };
                break;
            case "fr":
            case "frz":
                currentRotation = value.Length == 0 ? baseline.Rotation : AssFormatValues.Number(value);
                break;
            case "frx":
                currentRotationX = value.Length == 0 ? 0 : AssFormatValues.Number(value);
                break;
            case "fry":
                currentRotationY = value.Length == 0 ? 0 : AssFormatValues.Number(value);
                break;
        }
    }

    private double Scale(string value)
    {
        var scale = AssFormatValues.Number(value) / 100;
        return sourceConstraints ? Math.Max(scale, 0) : scale;
    }

    internal void Observe()
    {
        if (!observed)
        {
            observedScale = currentScale;
            observedRotation = currentRotation;
            observedRotationX = currentRotationX;
            observedRotationY = currentRotationY;
            observed = true;
            return;
        }
        mixedScaleX |= !observedScale.X.Equals(currentScale.X);
        mixedScaleY |= !observedScale.Y.Equals(currentScale.Y);
        mixedRotation |= !observedRotation.Equals(currentRotation);
        mixedRotationX |= !observedRotationX.Equals(currentRotationX);
        mixedRotationY |= !observedRotationY.Equals(currentRotationY);
    }

    internal LayerTransform Transform()
    {
        if (!observed)
        {
            Observe();
        }
        var x = Component(observedScale.X, mixedScaleX, 1, 10000, "horizontal scaling", true);
        var y = Component(observedScale.Y, mixedScaleY, 1, 10000, "vertical scaling", true);
        var rotation = Component(observedRotation, mixedRotation, 0, 1e9, "rotation", false);
        var rotationX = Component(observedRotationX, mixedRotationX, 0, 1e9, "X rotation", false);
        var rotationY = Component(observedRotationY, mixedRotationY, 0, 1e9, "Y rotation", false);
        if (!x.Equals(1d) || !y.Equals(1d))
        {
            Report("Ass.TransformLayout", "ASS glyph scaling was imported as a native clip transform. The layout engines wrap text at different stages and use different glyph anchors, so automatic wrapping or placement may change.", 0, 0);
        }
        if (!canvasScaleX.Equals(canvasScaleY) && (rotation != 0 || rotationX != 0 || rotationY != 0))
        {
            Report(rotationX != 0 || rotationY != 0 ? "Ass.PerspectiveResampling" : "Ass.TransformLayout",
                "ASS rotation combined with nonuniform canvas resampling may produce shear that native scaling and rotation cannot fully represent. Rotation was preserved approximately.", 0, 0);
        }
        return new() { Scale = new(x, y), Rotation = -rotation, RotationX = -rotationX, RotationY = -rotationY };
    }

    internal bool TryPlacement(string name, string value, int sourceStart, int sourceLength, out ScenePoint position)
    {
        position = new();
        if (hasPlacement)
        {
            Report("Ass.DuplicatePlacement", "Duplicate ASS position or movement tags on the same line were ignored; the first value was used.", sourceStart, sourceLength);
            return false;
        }
        var arguments = AssOverrideTags.Arguments(value);
        if (name == "pos" && arguments.Length != 2 || name == "move" && arguments.Length is not (4 or 6))
        {
            throw new InvalidDataException(name == "pos" ? "ASS pos requires two coordinates." : "ASS move requires four coordinates and optionally two times.");
        }
        hasPlacement = true;
        position = new(AssFormatValues.Number(arguments[0]) * canvasScaleX, AssFormatValues.Number(arguments[1]) * canvasScaleY);
        if (!ValidPoint(position))
        {
            Report("Ass.UnsupportedTransform", "ASS placement exceeds the native coordinate range and was skipped.", sourceStart, sourceLength);
            return false;
        }
        if (name == "pos")
        {
            placement = position;
            return true;
        }
        var target = new ScenePoint(AssFormatValues.Number(arguments[2]) * canvasScaleX, AssFormatValues.Number(arguments[3]) * canvasScaleY);
        var delta = new ScenePoint(target.X - position.X, target.Y - position.Y);
        if (!ValidPoint(target) || !ValidPoint(delta))
        {
            Report("Ass.UnsupportedTransform", "ASS movement coordinates or translation exceed the native range. The movement was skipped.", sourceStart, sourceLength);
            return false;
        }
        var start = MediaTime.Zero;
        var end = original.End - original.Start;
        if (arguments.Length == 6)
        {
            var first = AssFormatValues.Number(arguments[4]);
            var last = AssFormatValues.Number(arguments[5]);
            if (first != 0 || last != 0)
            {
                if (first < long.MinValue || first >= long.MaxValue || last < long.MinValue || last >= long.MaxValue)
                {
                    Report("Ass.MoveTiming", "ASS movement times exceed the supported range. The movement was skipped.", sourceStart, sourceLength);
                    return false;
                }
                var firstMilliseconds = (long)first;
                var lastMilliseconds = (long)last;
                if (firstMilliseconds > 0 || lastMilliseconds > 0)
                {
                    start = new(firstMilliseconds, 1000);
                    end = new(lastMilliseconds, 1000);
                }
                if (!first.Equals((long)first) || !last.Equals((long)last))
                {
                    Report("Ass.MoveTiming", "ASS movement times were parsed as integer milliseconds; fractional milliseconds were discarded.", sourceStart, sourceLength);
                }
            }
        }
        if (end < start)
        {
            (start, end) = (end, start);
        }
        if (end == start)
        {
            Report("Ass.MoveTiming", "ASS instantaneous movement cannot be represented as a continuous position track and was skipped.", sourceStart, sourceLength);
            return false;
        }
        if (delta != new ScenePoint())
        {
            move = new(AnimationProperty.POSITION,
            [
                new(start, AnimationValue.FromVector(new())),
                new(end, AnimationValue.FromVector(delta))
            ]);
        }
        placement = position;
        return true;
    }

    internal ImmutableArray<AnimationTrack> Tracks(MediaTime contentOffset)
    {
        if (move is null)
        {
            return [];
        }
        var rebased = move with
        {
            Keyframes = move.Keyframes.Select(frame => frame with { Time = frame.Time + contentOffset }).ToImmutableArray()
        };
        return LayerAnimationTiming.Clip(new ProjectLayer
        {
            Start = original.Start, End = original.End, AnimationOffset = contentOffset, Tracks = [rebased]
        }).Tracks;
    }

    private AssStyleDefinition OriginalDefinition()
    {
        return styles.TryGetValue(original.StyleName, out var style) ? style : new(original.StyleName, original.Style, SceneColor.White);
    }

    private double Component(double value, bool mixed, double fallback, double maximum, string name, bool positive)
    {
        if (mixed)
        {
            Report("Ass.InlineTransform", $"Inline ASS {name} values differ and cannot be stored as a whole-line clip transform. This component was skipped.", 0, 0);
            return fallback;
        }
        if (!double.IsFinite(value) || Math.Abs(value) > maximum || positive && value < 0)
        {
            Report("Ass.UnsupportedTransform", $"ASS {name} is outside the convertible native range. This component was skipped.", 0, 0);
            return fallback;
        }
        return value;
    }

    private static bool ValidPoint(ScenePoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        Math.Abs(point.X) <= 1e9 && Math.Abs(point.Y) <= 1e9;

    private void Report(string code, string message, int start, int length)
    {
        diagnostics.Add(new(code, message, start, length, original.Id));
    }
}
