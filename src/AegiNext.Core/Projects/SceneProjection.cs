namespace AegiNext.Core.Projects;

/// <summary>原生平面三维旋转、透视和轴心保位共用的纯数学契约。</summary>
public static class SceneProjection
{
    public const double DEFAULT_DISTANCE = 312.5;
    public const double MIN_DISTANCE = 0.001;
    public const double MAX_DISTANCE = 1e9;
    public const double NEAR_PLANE_RATIO = 1d / 16384;

    /// <summary>判断该姿态是否需要深度补偿及透视几何。</summary>
    public static bool RequiresPerspective(LayerTransform transform) =>
        Math.IEEERemainder(transform.RotationX, 360) != 0 ||
        Math.IEEERemainder(transform.RotationY, 360) != 0 || transform.ProjectionTranslation.Z != 0;

    /// <summary>对局部二维位移先缩放，再按 Z、X、Y 依次旋转。</summary>
    public static SceneVector3 RotateScaled(ScenePoint delta, LayerTransform transform) =>
        Rotate(new(delta.X * transform.Scale.X, delta.Y * transform.Scale.Y, 0), transform);

    /// <summary>按 Z、X、Y 顺序旋转三维向量，原始多圈参数保持不变。</summary>
    public static SceneVector3 Rotate(SceneVector3 value, LayerTransform transform)
    {
        var z = SinCos(transform.Rotation);
        var x = SinCos(transform.RotationX);
        var y = SinCos(transform.RotationY);
        var zx = z.Cos * value.X - z.Sin * value.Y;
        var zy = z.Sin * value.X + z.Cos * value.Y;
        var xy = x.Cos * zy - x.Sin * value.Z;
        var xz = x.Sin * zy + x.Cos * value.Z;
        return new(y.Cos * zx + y.Sin * xz, xy, -y.Sin * zx + y.Cos * xz);
    }

    /// <summary>创建以原生输出位置为透视中心的投影，分母为 (distance + depth) / distance。</summary>
    public static ProjectionMatrix3x3 CreateMatrix(LayerTransform transform, ScenePoint pivot, ScenePoint center)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var distance = transform.PerspectiveDistance;
        if (!double.IsFinite(distance) || distance < MIN_DISTANCE || distance > MAX_DISTANCE)
        {
            throw new ArgumentOutOfRangeException(nameof(transform), "Perspective distance is outside the project length budget.");
        }
        var x = RotateScaled(new(1, 0), transform);
        var y = RotateScaled(new(0, 1), transform);
        var translation = transform.ProjectionTranslation;
        var offsetX = translation.X - x.X * pivot.X - y.X * pivot.Y;
        var offsetY = translation.Y - x.Y * pivot.X - y.Y * pivot.Y;
        var offsetZ = distance + translation.Z - x.Z * pivot.X - y.Z * pivot.Y;
        return new(x.X + center.X * x.Z / distance, y.X + center.X * y.Z / distance,
            offsetX + center.X * offsetZ / distance,
            x.Y + center.Y * x.Z / distance, y.Y + center.Y * y.Z / distance,
            offsetY + center.Y * offsetZ / distance,
            x.Z / distance, y.Z / distance, offsetZ / distance);
    }

    private static (double Sin, double Cos) SinCos(double degrees)
    {
        var reduced = Math.IEEERemainder(degrees, 360);
        return reduced switch
        {
            0 => (0, 1),
            90 => (1, 0),
            -90 => (-1, 0),
            180 or -180 => (0, -1),
            _ => Math.SinCos(reduced * Math.PI / 180)
        };
    }
}
