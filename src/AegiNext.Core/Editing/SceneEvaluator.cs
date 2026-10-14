using System.Collections.Immutable;
using System.Collections.Frozen;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>纯函数动画求值，不依赖时钟、文件系统或渲染框架。</summary>
public static class SceneEvaluator
{
    /// <summary>验证工程并按精确半开时间区间求出可见合成树。</summary>
    public static ImmutableArray<EvaluatedLayer> Evaluate(ProjectDocument document, MediaTime time)
    {
        return Evaluate(new PreparedProjectScene(document), time);
    }

    /// <summary>求值已验证快照，适用于预览及逐帧压制。</summary>
    public static ImmutableArray<EvaluatedLayer> Evaluate(PreparedProjectScene scene, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return EvaluateLayers(scene.Clips.LayersInDrawingOrder, scene.Subtitles, time);
    }

    /// <summary>求关键帧插值或按源顺序叠加原生变换，所有面板、脚本、预览及压制共用此结果。</summary>
    public static AnimationValue EvaluateTrack(AnimationTrack track, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.IsOrdered)
        {
            if (!track.Keyframes.IsDefaultOrEmpty || track.InitialValue is not { } initial)
            {
                throw new InvalidDataException("有序变换轨道不能同时包含关键帧且必须具有初始值。");
            }

            var result = initial;
            foreach (var operation in track.Transforms)
            {
                var transformFraction = time < operation.Start ? 0 : time >= operation.End ? 1 :
                    operation.Curve is { } curve
                        ? CurveFraction(curve.Interpolation, curve.CurveStart, curve.CurveEnd,
                            Fraction(time - operation.Start, operation.End - operation.Start), curve.Exponent, curve.Reverse)
                        : Math.Pow(Fraction(time - operation.Start, operation.End - operation.Start), operation.Acceleration);
                for (var component = 0; component < result.ComponentCount; component++)
                {
                    if (operation.ComponentMask != 0 && (operation.ComponentMask & (1 << component)) == 0)
                    {
                        continue;
                    }
                    var start = result.GetComponent(component);
                    var target = operation.Value.GetComponent(component);
                    var componentValue = operation.Mode switch
                    {
                        AnimationTransformMode.INTERPOLATE_TO => result.IsColor
                            ? AnimationColorInterpolation.Interpolate(start, target, transformFraction, track.ColorSpace, component)
                            : start + (target - start) * transformFraction,
                        AnimationTransformMode.MULTIPLY_BY => start * (1 + (target - 1) * transformFraction),
                        _ => throw new InvalidDataException("未知动画变换模式。")
                    };
                    result = result.WithComponent(component, componentValue);
                }
            }

            return result;
        }

        if (track.Keyframes.IsDefaultOrEmpty)
        {
            throw new ArgumentException("轨道没有关键帧。", nameof(track));
        }

        var frames = track.Keyframes;
        if (time <= frames[0].Time)
        {
            return frames[0].Value;
        }

        if (time >= frames[^1].Time)
        {
            return frames[^1].Value;
        }

        var lower = 1;
        var upper = frames.Length - 1;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (time >= frames[middle].Time)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        var first = frames[lower - 1];
        var second = frames[lower];
        var fraction = Fraction(time - first.Time, second.Time - first.Time);
        var value = first.Value;
        for (var component = 0; component < first.Value.ComponentCount; component++)
        {
            var curve = component > 0 && !first.ComponentCurves.IsDefaultOrEmpty ? first.ComponentCurves[component - 1] : null;
            var componentFraction = curve is null
                ? CurveFraction(first.Interpolation, first.CurveStart, first.CurveEnd, fraction, first.Exponent, first.Reverse)
                : CurveFraction(curve.Interpolation, curve.CurveStart, curve.CurveEnd, fraction, curve.Exponent, curve.Reverse);
            var start = first.Value.GetComponent(component);
            var end = second.Value.GetComponent(component);
            value = value.WithComponent(component, first.Value.IsColor
                ? AnimationColorInterpolation.Interpolate(start, end, componentFraction, track.ColorSpace, component)
                : start + (end - start) * componentFraction);
        }

        return value;
    }

    /// <summary>读取标量轨道值，拒绝向量轨道。</summary>
    public static double EvaluateScalarTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Scalar;

    /// <summary>读取二维向量轨道值，拒绝标量轨道。</summary>
    public static ScenePoint EvaluateVectorTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Vector;

    /// <summary>读取三维向量轨道值，拒绝其他维度。</summary>
    public static SceneVector3 EvaluateVector3Track(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Vector3;

    /// <summary>读取完整线性 RGBA 轨道值，拒绝其他维度。</summary>
    public static SceneColor EvaluateColorTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Color;

    private static double CurveFraction(KeyframeInterpolation interpolation, double start, double end, double fraction,
        double exponent, bool reverse)
    {
        if (reverse)
        {
            return interpolation == KeyframeInterpolation.HOLD
                ? fraction == 0 ? 0 : 1
                : 1 - CurveFraction(interpolation, 1 - end, 1 - start, 1 - fraction, exponent, false);
        }
        var range = end - start;
        var offset = range * fraction;
        return interpolation switch
        {
            KeyframeInterpolation.HOLD => 0,
            KeyframeInterpolation.LINEAR => fraction,
            KeyframeInterpolation.POWER => PowerFraction(start, end, fraction, exponent),
            KeyframeInterpolation.EASE_IN => fraction * (2 * start + offset) / (2 * start + range),
            KeyframeInterpolation.EASE_OUT => fraction * (2 * (1 - end) + range * (2 - fraction)) /
                (2 * (1 - end) + range),
            KeyframeInterpolation.EASE_IN_OUT => fraction *
                (6 * start * (1 - start) + 3 * offset * (1 - 2 * start) - 2 * offset * offset) /
                (6 * start * (1 - start) + 3 * range * (1 - 2 * start) - 2 * range * range),
            _ => throw new InvalidDataException("未知关键帧插值。")
        };
    }

    private static double PowerFraction(double start, double end, double fraction, double exponent)
    {
        if (fraction <= 0)
        {
            return 0;
        }

        if (fraction >= 1)
        {
            return 1;
        }

        if (start == 0)
        {
            return Math.Pow(fraction, exponent);
        }

        var relativeRange = (end - start) / start;
        var numerator = ExponentialMinusOne(exponent * LogarithmOnePlus(relativeRange * fraction));
        var denominator = ExponentialMinusOne(exponent * LogarithmOnePlus(relativeRange));
        if (double.IsPositiveInfinity(denominator))
        {
            return Math.Pow((start + (end - start) * fraction) / end, exponent);
        }

        return denominator == 0 ? fraction : Math.Clamp(numerator / denominator, 0, 1);
    }

    private static double LogarithmOnePlus(double value)
    {
        if (Math.Abs(value) >= 1e-5)
        {
            return Math.Log(1 + value);
        }

        return value * (1 + value * (-0.5 + value * (1d / 3 - value / 4)));
    }

    private static double ExponentialMinusOne(double value)
    {
        if (Math.Abs(value) >= 1e-5)
        {
            return Math.Exp(value) - 1;
        }

        return value * (1 + value * (0.5 + value * (1d / 6 + value / 24)));
    }

    /// <summary>按片段内容时间求值独立蒙版，原生定位、固定参考框及拓扑身份保持不变。</summary>
    public static ClipMask? EvaluateMask(ProjectLayer layer, MediaTime contentTime)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return EvaluateMask(layer.Mask, layer.Tracks.Where(track => AnimationPropertyMetadata.IsMaskProperty(track.Property))
            .ToDictionary(track => track.Target, track => EvaluateTrack(track, contentTime)));
    }

    private static ClipMask? EvaluateMask(ClipMask? mask, Dictionary<AnimationTrackTarget, AnimationValue> values)
    {
        if (mask is null)
        {
            return null;
        }

        var transform = mask.Transform with
        {
            Position = GetVector(values, AnimationProperty.MASK_POSITION, mask.Transform.Position),
            Scale = GetVector(values, AnimationProperty.MASK_SCALE, mask.Transform.Scale),
            Rotation = Get(values, AnimationProperty.MASK_ROTATION, mask.Transform.Rotation)
        };
        if (transform != mask.Transform)
        {
            mask = mask with { Transform = transform };
        }

        if (mask is RectangleClipMask rectangle)
        {
            var topLeft = GetVector(values, AnimationProperty.MASK_RECTANGLE_TOP_LEFT, rectangle.TopLeft);
            var bottomRight = GetVector(values, AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT, rectangle.BottomRight);
            return topLeft == rectangle.TopLeft && bottomRight == rectangle.BottomRight ? rectangle :
                rectangle with { TopLeft = topLeft, BottomRight = bottomRight };
        }

        var vector = (VectorClipMask)mask;
        ImmutableArray<MaskContour>.Builder? contours = null;
        for (var contourIndex = 0; contourIndex < vector.Contours.Length; contourIndex++)
        {
            var contour = vector.Contours[contourIndex];
            ImmutableArray<MaskNode>.Builder? nodes = null;
            for (var nodeIndex = 0; nodeIndex < contour.Nodes.Length; nodeIndex++)
            {
                var node = contour.Nodes[nodeIndex];
                var position = values.TryGetValue(new(AnimationProperty.MASK_NODE_POSITION, node.Id), out var point) ? point.Vector : node.Position;
                var inHandle = values.TryGetValue(new(AnimationProperty.MASK_NODE_IN_HANDLE, node.Id), out var input) ? input.Vector : node.InHandle;
                var outHandle = values.TryGetValue(new(AnimationProperty.MASK_NODE_OUT_HANDLE, node.Id), out var output) ? output.Vector : node.OutHandle;
                if (position != node.Position || inHandle != node.InHandle || outHandle != node.OutHandle)
                {
                    nodes ??= contour.Nodes.ToBuilder();
                    nodes[nodeIndex] = node with { Position = position, InHandle = inHandle, OutHandle = outHandle };
                }
            }

            if (nodes is not null)
            {
                contours ??= vector.Contours.ToBuilder();
                contours[contourIndex] = contour with { Nodes = nodes.ToImmutable() };
            }
        }

        return contours is null ? vector : vector with { Contours = contours.ToImmutable() };
    }

    /// <summary>以 [0,1] 段参数求路径位置，超界参数夹在首尾。</summary>
    public static ScenePoint EvaluatePath(PathGeometry path, double progress)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Segments.IsDefaultOrEmpty || !double.IsFinite(progress))
        {
            throw new ArgumentException("路径或参数无效。", nameof(path));
        }

        var scaled = Math.Clamp(progress, 0, 1) * path.Segments.Length;
        var index = Math.Min((int)scaled, path.Segments.Length - 1);
        var t = scaled - index;
        var a = index == 0 ? path.Start : path.Segments[index - 1].End;
        var segment = path.Segments[index];
        var inverse = 1 - t;
        return new(inverse * inverse * inverse * a.X + 3 * inverse * inverse * t * segment.Control1.X +
            3 * inverse * t * t * segment.Control2.X + t * t * t * segment.End.X,
            inverse * inverse * inverse * a.Y + 3 * inverse * inverse * t * segment.Control1.Y +
            3 * inverse * t * t * segment.Control2.Y + t * t * t * segment.End.Y);
    }

    /// <summary>以内容时钟求值单个已验证片段，不按可见时间过滤，可用于片段端点或不可见片段的测量。</summary>
    public static EvaluatedLayer EvaluateLayer(ProjectLayer layer, SubtitleLine? subtitle, MediaTime contentTime)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var values = layer.Tracks.ToDictionary(track => track.Target, track => EvaluateTrack(track, contentTime));
        var rotation = GetRotation(values, null, new(layer.Transform.RotationX, layer.Transform.RotationY, layer.Transform.Rotation));
        var transform = layer.Transform with
        {
            Position = GetVector(values, AnimationProperty.POSITION, layer.Transform.Position),
            Scale = GetVector(values, AnimationProperty.SCALE, layer.Transform.Scale),
            Rotation = rotation.Z,
            RotationX = rotation.X,
            RotationY = rotation.Y,
            PerspectiveDistance = Get(values, AnimationProperty.PERSPECTIVE_DISTANCE, layer.Transform.PerspectiveDistance)
        };
        if (layer.MotionPath is { } motion)
        {
            var progress = Get(values, AnimationProperty.PATH_PROGRESS, Fraction(contentTime, motion.Duration));
            var point = EvaluatePath(motion.Path, progress);
            var pathRotation = transform.Rotation;
            if (motion.OrientToPath)
            {
                var before = EvaluatePath(motion.Path, progress - 0.00001);
                var after = EvaluatePath(motion.Path, progress + 0.00001);
                pathRotation += Math.Atan2(after.Y - before.Y, after.X - before.X) * 180 / Math.PI;
            }

            transform = transform with { X = transform.X + point.X, Y = transform.Y + point.Y, Rotation = pathRotation };
        }

        var fill = subtitle?.Style.Fill ?? layer.Fill;
        var stroke = subtitle?.Style.Stroke ?? layer.Stroke;
        return new(layer, contentTime, transform, Get(values, AnimationProperty.OPACITY, layer.Opacity),
            GetColor(values, AnimationProperty.FILL, fill), GetColor(values, AnimationProperty.STROKE, stroke),
            Get(values, AnimationProperty.STROKE_WIDTH, subtitle?.Style.StrokeWidth ?? layer.StrokeWidth),
            Get(values, AnimationProperty.BLUR, layer.Blur), subtitle)
        {
            Mask = EvaluateMask(layer.Mask, values),
            HasFillAnimation = values.ContainsKey(new(AnimationProperty.FILL)),
            HasStrokeAnimation = values.ContainsKey(new(AnimationProperty.STROKE)),
            HasStrokeWidthAnimation = values.ContainsKey(new(AnimationProperty.STROKE_WIDTH)),
            LetterSpacing = Get(values, AnimationProperty.LETTER_SPACING, subtitle?.Style.LetterSpacing ?? 0),
            FillBlur = Get(values, AnimationProperty.FILL_BLUR, subtitle?.Style.FillBlur ?? 0),
            StrokeBlur = Get(values, AnimationProperty.STROKE_BLUR, subtitle?.Style.StrokeBlur ?? 0),
            HasLetterSpacingAnimation = values.ContainsKey(new(AnimationProperty.LETTER_SPACING)),
            HasFillBlurAnimation = values.ContainsKey(new(AnimationProperty.FILL_BLUR)),
            HasStrokeBlurAnimation = values.ContainsKey(new(AnimationProperty.STROKE_BLUR)),
            FontSize = Get(values, AnimationProperty.FONT_SIZE, subtitle?.Style.FontSize ?? 0),
            ShadowOffset = GetVector(values, AnimationProperty.SHADOW_OFFSET, subtitle?.Style.ShadowOffset ?? new(0, 0)),
            ShadowBlur = Get(values, AnimationProperty.SHADOW_BLUR, subtitle?.Style.ShadowBlur ?? 0),
            ShadowColor = GetColor(values, AnimationProperty.SHADOW_COLOR, subtitle?.Style.ShadowColor ?? SceneColor.Transparent),
            HasFontSizeAnimation = values.ContainsKey(new(AnimationProperty.FONT_SIZE)),
            HasShadowOffsetAnimation = values.ContainsKey(new(AnimationProperty.SHADOW_OFFSET)),
            HasShadowBlurAnimation = values.ContainsKey(new(AnimationProperty.SHADOW_BLUR)),
            HasShadowColorAnimation = values.ContainsKey(new(AnimationProperty.SHADOW_COLOR)),
            AnimationValues = values.ToImmutableDictionary(),
            AnimationRanges = subtitle is null ? [] : subtitle.AnimationRanges.Select(range => EvaluateRange(range, values)).ToImmutableArray()
        };
    }

    private static SubtitleAnimationRange EvaluateRange(SubtitleAnimationRange range,
        Dictionary<AnimationTrackTarget, AnimationValue> values)
    {
        var rotation = GetRotation(values, range.Id, new(range.RotationX, range.RotationY, range.Rotation));
        return range with
        {
            Offset = values.TryGetValue(new(AnimationProperty.POSITION, TextRangeId: range.Id), out var offset) ? offset.Vector : range.Offset,
            Scale = values.TryGetValue(new(AnimationProperty.SCALE, TextRangeId: range.Id), out var scale) ? scale.Vector : range.Scale,
            Rotation = rotation.Z,
            RotationX = rotation.X,
            RotationY = rotation.Y,
            PerspectiveDistance = values.TryGetValue(new(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id), out var distance) ? distance.Scalar : range.PerspectiveDistance
        };
    }

    private static SceneVector3 GetRotation(Dictionary<AnimationTrackTarget, AnimationValue> values, Guid? rangeId,
        SceneVector3 fallback)
    {
        if (values.TryGetValue(new(AnimationProperty.ROTATION_3D, TextRangeId: rangeId), out var rotation))
        {
            return rotation.Vector3;
        }

        return new(values.TryGetValue(new(AnimationProperty.ROTATION_X, TextRangeId: rangeId), out var x) ? x.Scalar : fallback.X,
            values.TryGetValue(new(AnimationProperty.ROTATION_Y, TextRangeId: rangeId), out var y) ? y.Scalar : fallback.Y,
            values.TryGetValue(new(AnimationProperty.ROTATION, TextRangeId: rangeId), out var z) ? z.Scalar : fallback.Z);
    }

    private static ImmutableArray<EvaluatedLayer> EvaluateLayers(ImmutableArray<ProjectLayer> layers,
        FrozenDictionary<Guid, SubtitleLine> subtitles, MediaTime time)
    {
        var result = ImmutableArray.CreateBuilder<EvaluatedLayer>();
        foreach (var layer in layers)
        {
            if (time < layer.Start || time >= layer.End)
            {
                continue;
            }

            var subtitle = layer.SubtitleId is { } id ? subtitles[id] : null;
            var local = time - layer.Start + layer.AnimationOffset;
            result.Add(EvaluateLayer(layer, subtitle, local));
        }

        return result.ToImmutable();
    }

    private static double Get(Dictionary<AnimationTrackTarget, AnimationValue> values, AnimationProperty property, double fallback)
    {
        return values.TryGetValue(new(property), out var value) ? value.Scalar : fallback;
    }

    private static ScenePoint GetVector(Dictionary<AnimationTrackTarget, AnimationValue> values, AnimationProperty property, ScenePoint fallback)
    {
        return values.TryGetValue(new(property), out var value) ? value.Vector : fallback;
    }

    private static SceneColor GetColor(Dictionary<AnimationTrackTarget, AnimationValue> values, AnimationProperty property, SceneColor fallback)
    {
        return values.TryGetValue(new(property), out var value) ? value.Color : fallback;
    }

    private static double Fraction(MediaTime elapsed, MediaTime duration)
    {
        return Math.Clamp(((double)elapsed.Numerator / elapsed.Denominator) /
            ((double)duration.Numerator / duration.Denominator), 0, 1);
    }
}
