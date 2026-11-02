namespace AegiNext.SubtitleFormats;

internal sealed record AssScriptInfoContext
{
    internal int Width { get; init; }
    internal int Height { get; init; }
    internal int TargetWidth { get; init; }
    internal int TargetHeight { get; init; }
    internal bool HasWidth { get; init; }
    internal bool HasHeight { get; init; }
    internal double ScaleX => (double)TargetWidth / Width;
    internal double ScaleY => (double)TargetHeight / Height;
    internal int WrapStyle { get; init; }
    internal bool BlurUsesPlayRes { get; init; }
    internal double BlurScaleX { get; init; }
    internal double BlurScaleY { get; init; }
    internal double PerspectiveDistance { get; init; }
    internal required AssResolutionContext Resolution { get; init; }
}
