using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record AssetItemEditTarget(ProjectDocument Document, long Generation, Guid ItemId, Guid AssetId, string Name);
