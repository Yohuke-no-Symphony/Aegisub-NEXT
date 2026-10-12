using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssRotationOriginExport(ScenePoint Origin, ScenePoint PositionOffset, double PositionRotation)
{
    internal static AssRotationOriginExport? Create(ProjectLayer layer, SubtitleLine line, ScenePoint basis,
        ScenePoint alignmentDelta, ScenePoint outputScale, double outputRotation, ScenePoint position,
        AssLinearMove? move, bool convertedPath, bool needsMeasurement, bool hasMeasurement, out string reason)
    {
        reason = string.Empty;
        if (AssRotationOriginConversion.HasRangeGeometry(line, layer.Tracks))
        {
            reason = "text ranges contain independent translation, scaling, or rotation";
            return null;
        }
        var geometry = layer.Tracks.Where(track => track.Target.TextRangeId is null &&
            track.Property is AnimationProperty.POSITION or AnimationProperty.SCALE or AnimationProperty.ROTATION).ToArray();
        if (geometry.Any(track => track.Target.State != SubtitleAnimationState.NORMAL))
        {
            reason = "geometry for independent text states cannot share a fixed origin";
            return null;
        }
        var scale = layer.Transform.Scale;
        var rotation = layer.Transform.Rotation;
        var animatedRotation = false;
        foreach (var track in geometry)
        {
            var constant = AssRotationOriginConversion.TryConstant(track, out var value);
            if (track.Property == AnimationProperty.SCALE)
            {
                if (!constant)
                {
                    reason = "animated scaling requires coordinated origin and position changes";
                    return null;
                }
                scale = value.Vector;
            }
            else if (track.Property == AnimationProperty.ROTATION)
            {
                if (constant)
                {
                    rotation = value.Scalar;
                }
                else
                {
                    animatedRotation = true;
                }
            }
            else if (move is null)
            {
                reason = "the position track cannot be fully converted to a single straight-line movement";
                return null;
            }
        }
        if (!double.IsFinite(scale.X) || !double.IsFinite(scale.Y) || scale.X is <= 0 or > 10000 ||
            scale.Y is <= 0 or > 10000 || scale != outputScale)
        {
            reason = "origin compensation requires fully preserved, fixed positive scaling";
            return null;
        }
        if (!double.IsFinite(rotation) || !animatedRotation && !rotation.Equals(outputRotation))
        {
            reason = "whole-line rotation was not fully preserved";
            return null;
        }
        if (layer.MotionPath is not null && !convertedPath || move is { Approximate: true })
        {
            reason = "the movement path or speed cannot be converted exactly to a single straight-line movement";
            return null;
        }
        if (animatedRotation && move is not null && move.First != move.Last)
        {
            reason = "rotation animation and movement coexist and require coordinated position changes";
            return null;
        }
        if (needsMeasurement && (!hasMeasurement || HasUnmeasuredTypography(layer)))
        {
            reason = "a complete fixed layout pivot is unavailable; static measurements cannot compensate for font-size or letter-spacing layout tracks";
            return null;
        }
        if (!AssRotationOriginConversion.ValidPoint(basis))
        {
            reason = "the layout reference exceeds the convertible coordinate range";
            return null;
        }
        AssRotationOriginExport converted;
        if (!animatedRotation && move is not null)
        {
            var pivot = layer.Transform.Pivot;
            var shifted = new ScenePoint(scale.X * pivot.X, scale.Y * pivot.Y);
            var origin = new ScenePoint(basis.X + shifted.X, basis.Y + shifted.Y);
            var layoutDelta = new ScenePoint(alignmentDelta.X + pivot.X, alignmentDelta.Y + pivot.Y);
            var reference = AssRotationOriginConversion.Rotate(shifted, -rotation);
            var offset = new ScenePoint(basis.X + scale.X * layoutDelta.X - reference.X,
                basis.Y + scale.Y * layoutDelta.Y - reference.Y);
            converted = new(origin, offset, -rotation);
        }
        else
        {
            var origin = new ScenePoint(basis.X + position.X, basis.Y + position.Y);
            var offset = new ScenePoint(basis.X + scale.X * alignmentDelta.X,
                basis.Y + scale.Y * alignmentDelta.Y);
            converted = new(origin, offset, 0);
        }
        if (!AssRotationOriginConversion.ValidPoint(converted.Origin) ||
            !AssRotationOriginConversion.ValidPoint(converted.MapPosition(position)) ||
            move is not null && (!AssRotationOriginConversion.ValidPoint(converted.MapPosition(move.First)) ||
                !AssRotationOriginConversion.ValidPoint(converted.MapPosition(move.Last))))
        {
            reason = "the compensated origin or placement exceeds the convertible coordinate range";
            return null;
        }
        return converted;
    }

    internal ScenePoint MapPosition(ScenePoint position)
    {
        var mapped = AssRotationOriginConversion.Rotate(position, PositionRotation);
        return new(PositionOffset.X + mapped.X, PositionOffset.Y + mapped.Y);
    }

    private static bool HasUnmeasuredTypography(ProjectLayer layer)
    {
        foreach (var track in layer.Tracks.Where(track =>
            track.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING))
        {
            if (track.Property == AnimationProperty.FONT_SIZE || track.Target.TextRangeId is not null ||
                track.Target.State != SubtitleAnimationState.NORMAL || !AssRotationOriginConversion.TryConstant(track, out _))
            {
                return true;
            }
        }
        return false;
    }
}
