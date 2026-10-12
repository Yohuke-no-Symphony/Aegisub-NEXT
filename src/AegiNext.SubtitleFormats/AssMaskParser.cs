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
            diagnostics.Add(new("Ass.DuplicateMask", "ASS 重复的静态裁切采用最后的矩形值；后置静态裁切会覆盖此前裁切变换。", sourceStart, sourceLength, subtitleId));
            transforms.Clear();
        }
        mask = parsed;
    }

    internal bool TryTransform(string value, Guid subtitleId, int sourceStart, int sourceLength)
    {
        var arguments = AssOverrideTags.Arguments(value);
        if (arguments.Length is < 1 or > 4 || !arguments[^1].StartsWith('\\'))
        {
            throw new InvalidDataException("ASS t 参数数量或标签列表非法。");
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
            throw new InvalidDataException("ASS 裁切变换必须具有有序时间及有限 accel。");
        }
        foreach (var clip in clips)
        {
            var target = ParseGeometry(clip.Name, clip.Value);
            if (acceleration < 0)
            {
                diagnostics.Add(new("Ass.MaskAcceleration", "负 accel 在变换起点产生无穷大，不能保存为有限项目蒙版；已保留基础裁切并舍弃该变换。", sourceStart, sourceLength, subtitleId));
                continue;
            }
            if (target is not RectangleClipMask rectangle)
            {
                diagnostics.Add(new("Ass.VectorMaskTransform", "ASS 不能以 t 动画矢量裁切，已保留静态矢量；请使用原生蒙版动画。", sourceStart, sourceLength, subtitleId));
                mask ??= target;
                continue;
            }
            mask ??= new RectangleClipMask { BottomRight = new(width, height), Transform = new() { Pivot = new(width / 2d, height / 2d) }, Inverted = rectangle.Inverted };
            if (mask is not RectangleClipMask || mask.Inverted != rectangle.Inverted)
            {
                diagnostics.Add(new("Ass.MixedMaskModes", "ASS 混合 clip、iclip 或矩形与矢量的变换不能保留为同一蒙版；已保留基础形状并舍弃不兼容变换。", sourceStart, sourceLength, subtitleId));
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
                throw new InvalidDataException("ASS 矩形裁切必须按左上、右下顺序指定坐标。");
            }
            return new RectangleClipMask
            {
                Inverted = name == "iclip", TopLeft = topLeft, BottomRight = bottomRight,
                Transform = new() { Pivot = new((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2) }
            };
        }
        if (arguments.Length is not (1 or 2))
        {
            throw new InvalidDataException("ASS 矢量裁切必须指定绘图以及可选 scale。");
        }
        var scale = arguments.Length == 2 ? AssFormatValues.Integer(arguments[0]) : 1;
        if (scale is < 1 or > 31)
        {
            throw new InvalidDataException("ASS 矢量裁切 scale 必须在 1 至 31 之间。");
        }
        var divisor = Math.Pow(2, scale - 1);
        return AssMaskDrawing.Parse(arguments[^1], name == "iclip", scaleX / divisor, scaleY / divisor);
    }

    private static MediaTime Milliseconds(string value)
    {
        return new(checked((long)AssFormatValues.Number(value)), 1000);
    }
}
