using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed record ProjectedSubtitleGrapheme(SKRect OriginalBounds, ProjectiveMapping Projection);
