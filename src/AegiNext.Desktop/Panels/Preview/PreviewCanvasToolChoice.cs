using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Panels.Preview;

internal sealed record PreviewCanvasToolChoice(CanvasEditMode Mode, ShapeKind? ShapeKind, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
