using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>将工程共享轮廓投影为 Skia 路径，供绘制、命中和画布轮廓显示使用。</summary>
public static class ProjectShapeGeometry
{
    /// <summary>创建局部坐标路径；调用方负责释放。</summary>
    public static SKPath CreatePath(LayerShape shape)
    {
        var geometry = ShapeGeometry.CreatePath(shape);
        var path = new SKPath();
        path.MoveTo((float)geometry.Start.X, (float)geometry.Start.Y);
        foreach (var segment in geometry.Segments)
        {
            path.CubicTo((float)segment.Control1.X, (float)segment.Control1.Y, (float)segment.Control2.X,
                (float)segment.Control2.Y, (float)segment.End.X, (float)segment.End.Y);
        }
        if (geometry.Closed)
        {
            path.Close();
        }
        return path;
    }
}
