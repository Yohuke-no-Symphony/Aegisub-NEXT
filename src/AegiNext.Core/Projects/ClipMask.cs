using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>仅属于字幕片段、使用原生定位及固定局部参考框的蒙版，独立于字幕变换。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RectangleClipMask), "RECTANGLE")]
[JsonDerivedType(typeof(VectorClipMask), "VECTOR")]
public abstract record ClipMask
{
    public bool Inverted { get; init; }
    public LayerPosition Placement { get; init; } = new();
    public MaskReferenceFrame ReferenceFrame { get; init; } = new();
    public MaskTransform Transform { get; init; } = new();
}
