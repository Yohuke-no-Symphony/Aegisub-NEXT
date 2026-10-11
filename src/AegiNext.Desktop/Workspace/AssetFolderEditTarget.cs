using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record AssetFolderEditTarget(ProjectDocument Document, long Generation, Guid FolderId, string Name);
