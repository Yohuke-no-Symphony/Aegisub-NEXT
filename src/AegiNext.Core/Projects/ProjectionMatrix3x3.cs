namespace AegiNext.Core.Projects;

/// <summary>采用列向量的双精度平面齐次投影；保留分母符号用于前向裁剪。</summary>
public readonly record struct ProjectionMatrix3x3(double M11, double M12, double M13,
    double M21, double M22, double M23, double M31, double M32, double M33)
{
    public static ProjectionMatrix3x3 Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>返回投影点的齐次分母。</summary>
    public double W(ScenePoint point) => M31 * point.X + M32 * point.Y + M33;

    /// <summary>映射已确定处于有效投影域的点。</summary>
    public ScenePoint Map(ScenePoint point)
    {
        var w = W(point);
        return new((M11 * point.X + M12 * point.Y + M13) / w,
            (M21 * point.X + M22 * point.Y + M23) / w);
    }

    /// <summary>映射有限且满足前向分母条件的点。</summary>
    public bool TryMap(ScenePoint point, out ScenePoint result, double minimumW = 0)
    {
        var w = W(point);
        if (!double.IsFinite(w) || w == 0 || w < minimumW)
        {
            result = default;
            return false;
        }
        result = Map(point);
        return double.IsFinite(result.X) && double.IsFinite(result.Y);
    }

    /// <summary>逆映射后再次确认原投影的前向可见域。</summary>
    public bool TryUnmap(ScenePoint point, out ScenePoint result, double minimumW = SceneProjection.NEAR_PLANE_RATIO)
    {
        if (TryInvert(out var inverse) && inverse.TryMap(point, out result, double.NegativeInfinity) &&
            TryMap(result, out _, minimumW))
        {
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>求非奇异投影的逆矩阵。</summary>
    public bool TryInvert(out ProjectionMatrix3x3 inverse)
    {
        var a = M22 * M33 - M23 * M32;
        var b = M23 * M31 - M21 * M33;
        var c = M21 * M32 - M22 * M31;
        var determinant = M11 * a + M12 * b + M13 * c;
        if (!double.IsFinite(determinant) || determinant == 0)
        {
            inverse = default;
            return false;
        }
        inverse = new(a / determinant, (M13 * M32 - M12 * M33) / determinant,
            (M12 * M23 - M13 * M22) / determinant,
            b / determinant, (M11 * M33 - M13 * M31) / determinant,
            (M13 * M21 - M11 * M23) / determinant,
            c / determinant, (M12 * M31 - M11 * M32) / determinant,
            (M11 * M22 - M12 * M21) / determinant);
        return inverse.IsFinite;
    }

    public bool IsFinite => double.IsFinite(M11) && double.IsFinite(M12) && double.IsFinite(M13) &&
        double.IsFinite(M21) && double.IsFinite(M22) && double.IsFinite(M23) &&
        double.IsFinite(M31) && double.IsFinite(M32) && double.IsFinite(M33);

    /// <summary>在源平面按投影前向半平面裁切多边形。</summary>
    public IReadOnlyList<ScenePoint> Clip(IReadOnlyList<ScenePoint> polygon,
        double minimumW = SceneProjection.NEAR_PLANE_RATIO)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var result = new List<ScenePoint>();
        if (polygon.Count == 0)
        {
            return result;
        }
        var previous = polygon[^1];
        var previousW = W(previous);
        foreach (var current in polygon)
        {
            var currentW = W(current);
            var previousInside = double.IsFinite(previousW) && previousW >= minimumW;
            var currentInside = double.IsFinite(currentW) && currentW >= minimumW;
            if (previousInside != currentInside && double.IsFinite(previousW) && double.IsFinite(currentW))
            {
                var fraction = (minimumW - previousW) / (currentW - previousW);
                result.Add(new(previous.X + fraction * (current.X - previous.X),
                    previous.Y + fraction * (current.Y - previous.Y)));
            }
            if (currentInside)
            {
                result.Add(current);
            }
            previous = current;
            previousW = currentW;
        }
        return result;
    }

    /// <summary>组合投影，右侧矩阵先作用于源点。</summary>
    public static ProjectionMatrix3x3 operator *(ProjectionMatrix3x3 left, ProjectionMatrix3x3 right) => new(
        left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31,
        left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32,
        left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33,
        left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31,
        left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32,
        left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33,
        left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31,
        left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32,
        left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33);
}
