namespace AegiNext.Core.Projects;

/// <summary>蒙版原生定位、固定参考框及精确仿射边界计算，不依赖渲染或界面框架。</summary>
public static class ClipMaskPlacementGeometry
{
    private const double MAX_PIXEL_POSITION = 1e9;
    public const double MAX_REFERENCE_ORIGIN = 2e9;
    public const double MAX_REFERENCE_SIZE = 4e9;
    public const double MAX_NORMALIZED_PIVOT = 4e9;

    /// <summary>解析完整蒙版的原生定位与动画变换，工程画布尺寸由调用方显式提供。</summary>
    public static ProjectionMatrix3x3 CreateMatrix(ClipMask mask, ScenePoint canvasSize)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return CreateMatrix(mask.Placement, mask.ReferenceFrame, mask.Transform, canvasSize);
    }

    /// <summary>为尚未放置的局部几何建立中心定位和固定参考框；极限坐标选择可表示的定位，保留输入几何及独立动画。</summary>
    public static ClipMask CreatePlacement(ClipMask mask, ScenePoint canvasSize)
    {
        ArgumentNullException.ThrowIfNull(mask);
        RequireCanvas(canvasSize);
        var frame = CaptureReferenceFrame(mask);
        var pivot = new ScenePoint(Math.Clamp(frame.Origin.X + frame.Size.X / 2, -MAX_PIXEL_POSITION, MAX_PIXEL_POSITION),
            Math.Clamp(frame.Origin.Y + frame.Size.Y / 2, -MAX_PIXEL_POSITION, MAX_PIXEL_POSITION));
        var anchor = new ScenePoint(CreationAnchor(pivot.X, canvasSize.X), CreationAnchor(pivot.Y, canvasSize.Y));
        return mask with
        {
            ReferenceFrame = frame,
            Placement = new()
            {
                Anchor = anchor,
                Pivot = new((pivot.X - frame.Origin.X) / frame.Size.X, (pivot.Y - frame.Origin.Y) / frame.Size.Y),
                Offset = new(pivot.X - anchor.X * canvasSize.X, pivot.Y - anchor.Y * canvasSize.Y)
            }
        };
    }

    /// <summary>把工程画布锚点、局部归一化轴心和独立动画变换解析为双精度矩阵。</summary>
    public static ProjectionMatrix3x3 CreateMatrix(LayerPosition placement, MaskReferenceFrame frame,
        MaskTransform transform, ScenePoint canvasSize)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(transform);
        RequireCanvas(canvasSize);
        var position = LayerPositionGeometry.BasePosition(placement, canvasSize);
        var pivot = LayerPositionGeometry.LocalPivot(placement, frame.Origin, frame.Size);
        var angle = transform.Rotation * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var m11 = cosine * transform.Scale.X;
        var m12 = -sine * transform.Scale.Y;
        var m21 = sine * transform.Scale.X;
        var m22 = cosine * transform.Scale.Y;
        return new(m11, m12, position.X + transform.Position.X - m11 * pivot.X - m12 * pivot.Y,
            m21, m22, position.Y + transform.Position.Y - m21 * pivot.X - m22 * pivot.Y, 0, 0, 1);
    }

    /// <summary>按静态原几何的精确边界捕获参考框；每轴至少保留一个逻辑像素以支持退化几何。</summary>
    public static MaskReferenceFrame CaptureReferenceFrame(ClipMask mask)
    {
        var bounds = GetBounds(mask, ProjectionMatrix3x3.Identity);
        return new() { Origin = bounds.Origin, Size = new(Math.Max(bounds.Size.X, 1), Math.Max(bounds.Size.Y, 1)) };
    }

    /// <summary>取得仿射投影后的精确几何边界，支持非零常数齐次分母，保持零面积并计入贝塞尔曲线极值。</summary>
    public static (ScenePoint Origin, ScenePoint Size) GetBounds(ClipMask mask, ProjectionMatrix3x3 matrix)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (!matrix.IsFinite || matrix.M31 != 0 || matrix.M32 != 0 || matrix.M33 == 0)
        {
            throw new ArgumentException("Mask bounds require a finite affine matrix.", nameof(matrix));
        }
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        if (mask is RectangleClipMask rectangle)
        {
            Include(matrix.Map(rectangle.TopLeft));
            Include(matrix.Map(new(rectangle.BottomRight.X, rectangle.TopLeft.Y)));
            Include(matrix.Map(rectangle.BottomRight));
            Include(matrix.Map(new(rectangle.TopLeft.X, rectangle.BottomRight.Y)));
        }
        else if (mask is VectorClipMask vector)
        {
            foreach (var contour in vector.Contours)
            {
                for (var index = 0; index < contour.Nodes.Length; index++)
                {
                    var first = contour.Nodes[index];
                    var next = contour.Nodes[(index + 1) % contour.Nodes.Length];
                    var start = matrix.Map(first.Position);
                    var control1 = matrix.Map(new(first.Position.X + first.OutHandle.X, first.Position.Y + first.OutHandle.Y));
                    var control2 = matrix.Map(new(next.Position.X + next.InHandle.X, next.Position.Y + next.InHandle.Y));
                    var end = matrix.Map(next.Position);
                    var x = CubicBounds(start.X, control1.X, control2.X, end.X);
                    var y = CubicBounds(start.Y, control1.Y, control2.Y, end.Y);
                    Include(new(x.Minimum, y.Minimum));
                    Include(new(x.Maximum, y.Maximum));
                }
            }
        }
        else
        {
            throw new ArgumentException("Unknown mask geometry.", nameof(mask));
        }
        return double.IsPositiveInfinity(left) ? default : (new(left, top), new(right - left, bottom - top));

        void Include(ScenePoint point)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
    }

    private static (double Minimum, double Maximum) CubicBounds(double start, double control1, double control2, double end)
    {
        var bounds = (Minimum: Math.Min(start, end), Maximum: Math.Max(start, end));
        var a = -start + 3 * control1 - 3 * control2 + end;
        var b = 2 * (start - 2 * control1 + control2);
        var c = control1 - start;
        if (a == 0)
        {
            return b == 0 ? bounds : IncludeExtremum(bounds, start, control1, control2, end, -c / b);
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
        {
            return bounds;
        }
        var q = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
        if (q == 0)
        {
            return IncludeExtremum(bounds, start, control1, control2, end, -b / (2 * a));
        }
        bounds = IncludeExtremum(bounds, start, control1, control2, end, q / a);
        return IncludeExtremum(bounds, start, control1, control2, end, c / q);
    }

    private static (double Minimum, double Maximum) IncludeExtremum((double Minimum, double Maximum) bounds,
        double start, double control1, double control2, double end, double progress)
    {
        if (progress is <= 0 or >= 1)
        {
            return bounds;
        }
        var remaining = 1 - progress;
        var value = remaining * remaining * remaining * start + 3 * remaining * remaining * progress * control1 +
            3 * remaining * progress * progress * control2 + progress * progress * progress * end;
        return (Math.Min(bounds.Minimum, value), Math.Max(bounds.Maximum, value));
    }

    private static void RequireCanvas(ScenePoint canvasSize)
    {
        if (!double.IsFinite(canvasSize.X) || !double.IsFinite(canvasSize.Y) || canvasSize.X <= 0 || canvasSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasSize), "The project canvas must have finite positive dimensions.");
        }
    }

    private static double CreationAnchor(double pivot, double canvasSize)
    {
        var offset = pivot - canvasSize / 2;
        return Math.Abs(offset) <= MAX_PIXEL_POSITION ? 0.5 : Math.Clamp(pivot / canvasSize, 0, 1);
    }
}
