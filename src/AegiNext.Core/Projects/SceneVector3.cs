namespace AegiNext.Core.Projects;

/// <summary>完整三维向量；原生位移使用工程像素，旋转使用角度，空间正 Z 指向屏幕深处。</summary>
public readonly record struct SceneVector3(double X, double Y, double Z)
{
    /// <summary>相加两组三维位移。</summary>
    public static SceneVector3 operator +(SceneVector3 left, SceneVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}
