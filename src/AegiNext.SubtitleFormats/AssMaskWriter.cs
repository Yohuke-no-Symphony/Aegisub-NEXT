using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssMaskWriter
{
    internal static string? WriteTags(ProjectLayer layer, MediaTime origin, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (layer.Mask is null)
        {
            return string.Empty;
        }
        var tracks = layer.Tracks.Where(track => AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToArray();
        if (tracks.Length == 0)
        {
            return StaticTags(layer.Mask, layer.SubtitleId, diagnostics);
        }
        if (layer.Mask is not RectangleClipMask rectangle || rectangle.Transform != new MaskTransform { Pivot = rectangle.Transform.Pivot } ||
            tracks.Any(track => track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)))
        {
            return null;
        }
        var top = tracks.FirstOrDefault(track => track.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var bottom = tracks.FirstOrDefault(track => track.Property == AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT);
        if (top is not null && bottom is not null && (top.Transforms.IsEmpty != bottom.Transforms.IsEmpty ||
            !SameTiming(top, bottom)))
        {
            return null;
        }
        var master = top ?? bottom!;
        var initialTop = Initial(top, rectangle.TopLeft);
        var initialBottom = Initial(bottom, rectangle.BottomRight);
        var operations = new List<AssMaskWriteOperation>();
        if (!master.Transforms.IsEmpty)
        {
            for (var index = 0; index < master.Transforms.Length; index++)
            {
                var operation = master.Transforms[index];
                operations.Add(new(operation.Start - origin, operation.End - origin, operation.Acceleration,
                    top?.Transforms[index].Value.Vector ?? rectangle.TopLeft, bottom?.Transforms[index].Value.Vector ?? rectangle.BottomRight));
            }
        }
        else
        {
            for (var index = 0; index + 1 < master.Keyframes.Length; index++)
            {
                var key = master.Keyframes[index];
                if (NativeCurve(key) is not { } curve)
                {
                    return null;
                }
                var end = master.Keyframes[index + 1].Time - origin;
                operations.Add(new(curve.Instant ? end : key.Time - origin, end, curve.Acceleration,
                    top?.Keyframes[index + 1].Value.Vector ?? rectangle.TopLeft, bottom?.Keyframes[index + 1].Value.Vector ?? rectangle.BottomRight));
            }
        }
        if (!Integral(initialTop) || !Integral(initialBottom) || operations.Any(operation => !Integral(operation.TopLeft) || !Integral(operation.BottomRight) || operation.End == MediaTime.Zero || !ExactMilliseconds(operation.Start) || !ExactMilliseconds(operation.End)))
        {
            return null;
        }
        var result = new StringBuilder(RectangleTag(rectangle.Inverted, initialTop, initialBottom, layer.SubtitleId, diagnostics));
        foreach (var operation in operations)
        {
            result.Append("\\t(").Append(Milliseconds(operation.Start)).Append(',').Append(Milliseconds(operation.End)).Append(',')
                .Append(operation.Acceleration.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(RectangleTag(rectangle.Inverted, operation.TopLeft, operation.BottomRight, layer.SubtitleId, diagnostics)).Append(')');
        }
        return result.ToString();
    }

    internal static string StaticTags(ClipMask mask, Guid? subtitleId, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (mask is RectangleClipMask rectangle && mask.Transform.Rotation % 360 == 0)
        {
            var first = AssMaskGeometry.Transform(rectangle.TopLeft, mask.Transform);
            var second = AssMaskGeometry.Transform(rectangle.BottomRight, mask.Transform);
            if (Integral(first) && Integral(second))
            {
                return RectangleTag(mask.Inverted, new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
                    new(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)), subtitleId, diagnostics);
            }
        }
        var quantized = false;
        var result = new StringBuilder(mask.Inverted ? "\\iclip(7," : "\\clip(7,");
        var contours = mask switch
        {
            RectangleClipMask value => AssMaskGeometry.RectangleVector(value).Contours,
            VectorClipMask value => value.Contours,
            _ => throw new InvalidDataException("Unknown mask geometry.")
        };
        foreach (var contour in contours)
        {
            result.Append("m ");
            AppendPoint(contour.Nodes[0].Position);
            for (var index = 0; index < contour.Nodes.Length; index++)
            {
                var first = contour.Nodes[index];
                var second = contour.Nodes[(index + 1) % contour.Nodes.Length];
                if (first.OutHandle == default && second.InHandle == default)
                {
                    if (index + 1 < contour.Nodes.Length)
                    {
                        result.Append(" l ");
                        AppendPoint(second.Position);
                    }
                }
                else
                {
                    result.Append(" b ");
                    AppendPoint(new(first.Position.X + first.OutHandle.X, first.Position.Y + first.OutHandle.Y));
                    result.Append(' ');
                    AppendPoint(new(second.Position.X + second.InHandle.X, second.Position.Y + second.InHandle.Y));
                    result.Append(' ');
                    AppendPoint(second.Position);
                }
            }
            result.Append(' ');
        }
        if (quantized)
        {
            diagnostics.Add(new("Ass.MaskQuantization", "Vector mask coordinates were quantized to 1/64 of a project pixel.", SubtitleId: subtitleId));
        }
        return result.ToString().TrimEnd() + ")";

        void AppendPoint(ScenePoint point)
        {
            point = AssMaskGeometry.Transform(point, mask.Transform);
            var x = Math.Round(point.X * 64, MidpointRounding.ToEven);
            var y = Math.Round(point.Y * 64, MidpointRounding.ToEven);
            if (x / 64 != point.X || y / 64 != point.Y)
            {
                quantized = true;
            }
            result.Append(x.ToString("0", CultureInfo.InvariantCulture)).Append(' ').Append(y.ToString("0", CultureInfo.InvariantCulture));
        }
    }

    private static bool SameTiming(AnimationTrack first, AnimationTrack second)
    {
        if (!first.Transforms.IsEmpty)
        {
            return first.Transforms.Select(operation => (operation.Start, operation.End, operation.Acceleration))
                .SequenceEqual(second.Transforms.Select(operation => (operation.Start, operation.End, operation.Acceleration)));
        }
        return first.Keyframes.Select(key => key.Time).SequenceEqual(second.Keyframes.Select(key => key.Time)) &&
            first.Keyframes.Take(first.Keyframes.Length - 1).Select(NativeCurve)
                .SequenceEqual(second.Keyframes.Take(second.Keyframes.Length - 1).Select(NativeCurve));
    }

    private static (bool Instant, double Acceleration)? NativeCurve(Keyframe key)
    {
        (bool Instant, double Acceleration)? result = null;
        for (var component = 0; component < key.Value.ComponentCount; component++)
        {
            var curve = key.GetCurve(component);
            if (AssCurveCompatibility.IsReversedNonlinear(curve))
            {
                return null;
            }
            (bool Instant, double Acceleration)? native = curve.Interpolation switch
            {
                KeyframeInterpolation.HOLD => (true, 1),
                KeyframeInterpolation.LINEAR => (false, 1),
                KeyframeInterpolation.POWER when curve.CurveStart == 0 || curve.Exponent == 1 => (false, curve.Exponent),
                KeyframeInterpolation.EASE_IN when curve.CurveStart == 0 => (false, 2),
                _ => null
            };
            if (native is null || result is not null && result != native)
            {
                return null;
            }
            result = native;
        }
        return result;
    }

    private static ScenePoint Initial(AnimationTrack? track, ScenePoint fallback)
    {
        return track is null ? fallback : track.InitialValue?.Vector ?? track.Keyframes[0].Value.Vector;
    }

    private static string RectangleTag(bool inverted, ScenePoint first, ScenePoint second, Guid? subtitleId, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (!Integral(first) || !Integral(second))
        {
            diagnostics.Add(new("Ass.MaskQuantization", "ASS rectangular clipping coordinates were quantized to integer project pixels.", SubtitleId: subtitleId));
        }
        return (inverted ? "\\iclip(" : "\\clip(") + Integer(first.X) + "," + Integer(first.Y) + "," + Integer(second.X) + "," + Integer(second.Y) + ")";
    }

    private static bool Integral(ScenePoint point) => point.X == Math.Round(point.X) && point.Y == Math.Round(point.Y);
    private static string Integer(double value) => Math.Round(value, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
    private static bool ExactMilliseconds(MediaTime value) => new MediaTime(value.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value, 1000) == value;
    private static string Milliseconds(MediaTime value) => value.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value.ToString(CultureInfo.InvariantCulture);

}
