using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

internal readonly record struct ProjectionClipPlane(double X, double Y, double Constant)
{
    internal double Evaluate(ScenePoint point) => X * point.X + Y * point.Y + Constant;

    internal ProjectionClipPlane Transform(ProjectionMatrix3x3 matrix) => new(
        X * matrix.M11 + Y * matrix.M21 + Constant * matrix.M31,
        X * matrix.M12 + Y * matrix.M22 + Constant * matrix.M32,
        X * matrix.M13 + Y * matrix.M23 + Constant * matrix.M33);
}
