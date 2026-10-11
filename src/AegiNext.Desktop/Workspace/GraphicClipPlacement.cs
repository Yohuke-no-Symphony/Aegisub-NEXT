using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed record GraphicClipPlacement(MediaTime Start, MediaTime End, Guid? TrackId, ScenePoint? Position);
