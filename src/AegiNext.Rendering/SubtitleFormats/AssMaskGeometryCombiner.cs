using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using SkiaSharp;

namespace AegiNext.Rendering.SubtitleFormats;

/// <summary>复用字幕渲染的蒙版几何，将 ASS 静态裁切组合为原生闭合路径。</summary>
public sealed class AssMaskGeometryCombiner : ISubtitleMaskGeometryCombiner
{
    /// <summary>按两个裁切的反相状态执行交、差或并；空区域保留为零面积矩形。</summary>
    public bool TryCombine(VectorClipMask vector, RectangleClipMask rectangle, out ClipMask result, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(rectangle);
        result = vector;
        reason = null;
        using var vectorPath = ClipMaskGeometry.Create(vector);
        using var rectanglePath = ClipMaskGeometry.Create(rectangle);
        var operation = (vector.Inverted, rectangle.Inverted) switch
        {
            (false, false) => SKPathOp.Intersect,
            (false, true) => SKPathOp.Difference,
            (true, false) => SKPathOp.ReverseDifference,
            _ => SKPathOp.Union
        };
        using var combined = vectorPath.Op(rectanglePath, operation);
        if (combined is null)
        {
            reason = "The path engine could not combine the static clipping geometry.";
            return false;
        }
        using var winding = combined.ToWinding();
        if (winding is null)
        {
            reason = "The path engine could not preserve nonzero winding for the combined clipping geometry.";
            return false;
        }
        if (!SkiaMaskContours.TryCreate(winding, out var contours, out reason))
        {
            return false;
        }
        var inverted = vector.Inverted && rectangle.Inverted;
        result = contours.IsEmpty
            ? new RectangleClipMask { Inverted = inverted }
            : new VectorClipMask { Contours = contours, Inverted = inverted };
        return true;
    }
}
