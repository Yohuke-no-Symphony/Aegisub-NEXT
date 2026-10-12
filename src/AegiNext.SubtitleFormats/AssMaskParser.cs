using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssMaskParser(MediaTime duration, double scaleX, double scaleY, int width, int height)
{
    private ClipMask? mask;
    private readonly List<AssMaskTransform> transforms = [];
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];

    internal ClipMask? Mask => mask;
    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal void Apply(string name, string value, Guid subtitleId, int sourceStart, int sourceLength)
    {
        var parsed = ParseGeometry(name, value);
        if (mask is not null || transforms.Count > 0)
        {
            diagnostics.Add(new("Ass.DuplicateMask", "Duplicate static ASS clipping uses the last rectangle value. Later static clipping overrides preceding clipping transforms.", sourceStart, sourceLength, subtitleId));
            transforms.Clear();
        }
        mask = parsed;
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
            var target = ParseGeometry(clip.Name, clip.Value);
            if (acceleration < 0)
            {
                diagnostics.Add(new("Ass.MaskAcceleration", "Negative accel produces infinity at the transform start and cannot be stored as a finite project mask. Base clipping was preserved and the transform was discarded.", sourceStart, sourceLength, subtitleId));
                continue;
            }
            if (target is not RectangleClipMask rectangle)
            {
                diagnostics.Add(new("Ass.VectorMaskTransform", "ASS cannot animate vector clipping with t. The static vector was preserved; use native mask animation.", sourceStart, sourceLength, subtitleId));
                mask ??= target;
                continue;
            }
            mask ??= new RectangleClipMask { BottomRight = new(width, height), Transform = new() { Pivot = new(width / 2d, height / 2d) }, Inverted = rectangle.Inverted };
            if (mask is not RectangleClipMask || mask.Inverted != rectangle.Inverted)
            {
                diagnostics.Add(new("Ass.MixedMaskModes", "ASS transforms mixing clip and iclip, or rectangles and vectors, cannot be preserved as one mask. The base shape was preserved and incompatible transforms were discarded.", sourceStart, sourceLength, subtitleId));
                continue;
            }
            transforms.Add(new(start, end, acceleration, rectangle));
        }
        return tags.All(tag => tag.Name is "clip" or "iclip");
    }

    internal ImmutableArray<AnimationTrack> Tracks()
    {
        if (transforms.Count == 0 || mask is not RectangleClipMask rectangle)
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

    private ClipMask ParseGeometry(string name, string value)
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
            return new RectangleClipMask
            {
                Inverted = name == "iclip", TopLeft = topLeft, BottomRight = bottomRight,
                Transform = new() { Pivot = new((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2) }
            };
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
        return AssMaskDrawing.Parse(arguments[^1], name == "iclip", scaleX / divisor, scaleY / divisor);
    }

    private static MediaTime Milliseconds(string value)
    {
        return new(checked((long)AssFormatValues.Number(value)), 1000);
    }
}
