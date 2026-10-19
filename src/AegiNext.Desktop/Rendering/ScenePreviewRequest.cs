using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed record ScenePreviewRequest(long Sequence, long SceneRevision, ProjectDocument Document,
    MediaTime TargetTime, MediaTime? VideoTime, SdrVideoFrame? Background, int Width, int Height,
    string Directory, bool Interactive, SystemFontCatalog? FontCatalog = null);
