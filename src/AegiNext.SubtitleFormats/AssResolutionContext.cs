using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssResolutionContext(double borderScaleX, double borderScaleY)
{
    internal double BorderScaleX { get; } = borderScaleX;
    internal double BorderScaleY { get; } = borderScaleY;
    internal double StrokeScale { get; } = Math.Sqrt(borderScaleX * borderScaleY);
    internal bool ApproximatesStroke => !BorderScaleX.Equals(BorderScaleY);

    internal double Stroke(double value) => value * StrokeScale;

    internal ScenePoint Shadow(double value) => new(value * BorderScaleX, value * BorderScaleY);
}
