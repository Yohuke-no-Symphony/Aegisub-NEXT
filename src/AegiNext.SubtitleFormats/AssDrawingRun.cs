using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssDrawingRun(int SourceStart, int SourceLength, int DrawingScale, double BaselineOffset,
    SubtitleStyle Style, ScenePoint Scale, double Rotation, double RotationX = 0, double RotationY = 0);
