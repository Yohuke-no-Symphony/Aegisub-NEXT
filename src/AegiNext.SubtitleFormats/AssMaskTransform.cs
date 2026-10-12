using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssMaskTransform(MediaTime Start, MediaTime End, double Acceleration, RectangleClipMask Mask);
