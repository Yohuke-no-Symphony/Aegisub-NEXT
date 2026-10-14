using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssMaskParser(MediaTime duration, double scaleX, double scaleY, int width, int height,
    ISubtitleMaskGeometryCombiner? geometryCombiner = null)
{
    private VectorClipMask? vector;
    private RectangleClipMask? rectangle;
    private bool hasVectorSlot;
    private bool dirty;
    private ClipMask? resolved;
    private Guid subtitleId;
    private int sourceStart;
    private int sourceLength;
    private readonly List<AssMaskTransform> transforms = [];
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];

    internal ClipMask? Mask
    {
        get
        {
            if (!dirty)
            {
                return resolved;
            }
            dirty = false;
            resolved = vector ?? (ClipMask?)rectangle;
            if (vector is null || rectangle is null)
            {
                return resolved;
            }
            if (transforms.Count > 0)
            {
                diagnostics.Add(new("Ass.MixedMaskAnimation", "The first static vector clipping was preserved. Animated rectangle clipping combined with a vector requires mask animation baking and was omitted.", sourceStart, sourceLength, subtitleId));
                transforms.Clear();
            }
            string? reason = null;
            if (geometryCombiner is not null && geometryCombiner.TryCombine(vector, rectangle, new(width, height), out var combined, out reason))
            {
                resolved = combined;
                diagnostics.Add(new("Ass.MaskGeometryPrecision", "Static vector and rectangle clipping were combined into a native path. Floating-point geometry and native edge antialiasing may differ from the source player's pixel clipping.", sourceStart, sourceLength, subtitleId));
            }
            else
            {
                diagnostics.Add(new("Ass.MixedMaskModes", "The first vector clipping was preserved, but its additional rectangle clipping could not be combined: " +
                    (reason ?? "no geometry conversion service was provided") + ".", sourceStart, sourceLength, subtitleId));
            }
            return resolved;
        }
    }
    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal void Apply(string name, string value, Guid subtitleId, int sourceStart, int sourceLength)
    {
        var arguments = AssOverrideTags.Arguments(value);
        if (arguments.Length != 4)
        {
            if (hasVectorSlot)
            {
                diagnostics.Add(new("Ass.DuplicateMask", "Duplicate vector clipping was ignored; the first source vector clipping occupies this event's vector slot.", sourceStart, sourceLength, subtitleId));
                return;
            }
            if (arguments.Length is not (1 or 2))
            {
                throw new InvalidDataException("ASS vector clipping requires drawing commands and an optional scale value.");
            }
            hasVectorSlot = true;
            vector = ParseGeometry(name, value) as VectorClipMask;
            if (vector is null)
            {
                diagnostics.Add(new("Ass.EmptyVectorMask", "The first vector clipping has no drawable contour and was ignored, while still occupying the source vector slot.", sourceStart, sourceLength, subtitleId));
            }
        }
        else
        {
            var parsed = (RectangleClipMask)ParseGeometry(name, value)!;
            if (rectangle is not null || transforms.Count > 0)
            {
                diagnostics.Add(new("Ass.DuplicateMask", "Duplicate static rectangle clipping uses the last value and cancels preceding rectangle transforms.", sourceStart, sourceLength, subtitleId));
            }
            transforms.Clear();
            rectangle = parsed;
        }
        this.subtitleId = subtitleId;
        this.sourceStart = sourceStart;
        this.sourceLength = sourceLength;
        dirty = true;
    }

    internal bool TryTransform(string value, Guid subtitleId, int sourceStart, int sourceLength)
    {
        var arguments = AssOverrideTags.Arguments(value);
        if (arguments.Length is < 1 or > 4 || !arguments[^1].StartsWith('\\'))
        {
            throw new InvalidDataException("The ASS t argument count or tag list is invalid.");
        }
        var tags = AssOverrideTags.Parse(arguments[^1]).ToArray();
        var clips = tags.Where(tag => tag.Name is "clip" or "iclip").ToArray();
        if (clips.Length == 0)
        {
            return false;
        }
        var start = arguments.Length >= 3 ? Milliseconds(arguments[0]) : MediaTime.Zero;
        var end = arguments.Length >= 3 ? Milliseconds(arguments[1]) : duration;
        if (end == MediaTime.Zero)
        {
            end = duration;
        }
        var acceleration = arguments.Length is 2 or 4 ? AssFormatValues.Number(arguments[^2]) : 1;
        if (end < start || !double.IsFinite(acceleration))
        {
            throw new InvalidDataException("ASS clipping transforms require ordered times and a finite accel value.");
        }
        foreach (var clip in clips)
        {
            if (AssOverrideTags.Arguments(clip.Value).Length != 4)
            {
                Apply(clip.Name, clip.Value, subtitleId, sourceStart, sourceLength);
                diagnostics.Add(new("Ass.VectorMaskTransform", "Vector clipping inside t is static in ASS; its first source vector was preserved without animation.", sourceStart, sourceLength, subtitleId));
                continue;
            }
            var target = (RectangleClipMask)ParseGeometry(clip.Name, clip.Value)!;
            if (acceleration < 0)
            {
                diagnostics.Add(new("Ass.MaskAcceleration", "Negative accel produces infinity at the transform start and cannot be stored as a finite project mask. Base clipping was preserved and the transform was discarded.", sourceStart, sourceLength, subtitleId));
                continue;
            }
            rectangle ??= (RectangleClipMask)ClipMaskPlacementGeometry.CreatePlacement(
                new RectangleClipMask { BottomRight = new(width, height), Inverted = target.Inverted }, new(width, height));
            if (rectangle.Inverted != target.Inverted)
            {
                diagnostics.Add(new("Ass.MixedMaskModes", "ASS transforms mixing clip and iclip, or rectangles and vectors, cannot be preserved as one mask. The base shape was preserved and incompatible transforms were discarded.", sourceStart, sourceLength, subtitleId));
                continue;
            }
            transforms.Add(new(start, end, acceleration, target));
            this.subtitleId = subtitleId;
            this.sourceStart = sourceStart;
            this.sourceLength = sourceLength;
            dirty = true;
        }
        return tags.All(tag => tag.Name is "clip" or "iclip");
    }

    internal ImmutableArray<AnimationTrack> Tracks()
    {
        _ = Mask;
        if (transforms.Count == 0 || rectangle is null || vector is not null)
        {
            return [];
        }
        return [Track(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, rectangle.TopLeft, item => item.Mask.TopLeft),
            Track(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT, rectangle.BottomRight, item => item.Mask.BottomRight)];
    }

    private AnimationTrack Track(AnimationProperty property, ScenePoint initial, Func<AssMaskTransform, ScenePoint> target)
    {
        if (transforms.Count == 1 && transforms[0].Acceleration > 0 && transforms[0].Start >= MediaTime.Zero && transforms[0].End > transforms[0].Start)
        {
            var item = transforms[0];
            var first = new Keyframe(item.Start, AnimationValue.FromVector(initial), KeyframeInterpolation.POWER) { Exponent = item.Acceleration };
            return new(property, [first, new(item.End, AnimationValue.FromVector(target(item)))]);
        }
        return new(property, [])
        {
            InitialValue = AnimationValue.FromVector(initial),
            Transforms = transforms.Select(item => new AnimationTransformOperation(Guid.NewGuid(), item.Start, item.End,
                AnimationValue.FromVector(target(item)), item.Acceleration)).ToImmutableArray()
        };
    }

    private ClipMask? ParseGeometry(string name, string value)
    {
        var arguments = AssOverrideTags.Arguments(value);
        if (arguments.Length == 4)
        {
            var topLeft = new ScenePoint(AssFormatValues.Integer(arguments[0]) * scaleX, AssFormatValues.Integer(arguments[1]) * scaleY);
            var bottomRight = new ScenePoint(AssFormatValues.Integer(arguments[2]) * scaleX, AssFormatValues.Integer(arguments[3]) * scaleY);
            if (topLeft.X > bottomRight.X || topLeft.Y > bottomRight.Y)
            {
                throw new InvalidDataException("ASS rectangular clipping coordinates must specify the top-left corner followed by the bottom-right corner.");
            }
            return ClipMaskPlacementGeometry.CreatePlacement(new RectangleClipMask
            {
                Inverted = name == "iclip", TopLeft = topLeft, BottomRight = bottomRight
            }, new(width, height));
        }
        if (arguments.Length is not (1 or 2))
        {
            throw new InvalidDataException("ASS vector clipping requires drawing commands and an optional scale value.");
        }
        var scale = arguments.Length == 2 ? AssFormatValues.Integer(arguments[0]) : 1;
        if (scale is < 1 or > 31)
        {
            throw new InvalidDataException("ASS vector clipping scale must be between 1 and 31.");
        }
        var divisor = Math.Pow(2, scale - 1);
        var parsed = AssMaskDrawing.Parse(arguments[^1], name == "iclip", scaleX / divisor, scaleY / divisor);
        return parsed is null ? null : ClipMaskPlacementGeometry.CreatePlacement(parsed, new(width, height));
    }

    private static MediaTime Milliseconds(string value)
    {
        return new(checked((long)AssFormatValues.Number(value)), 1000);
    }
}
