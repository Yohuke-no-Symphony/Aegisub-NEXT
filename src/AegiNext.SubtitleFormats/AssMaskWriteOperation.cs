using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssMaskWriteOperation(MediaTime Start, MediaTime End, double Acceleration, ScenePoint TopLeft, ScenePoint BottomRight);
