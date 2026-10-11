namespace AegiNext.Desktop.Panels.Assets;

internal sealed record AssetFolderNode(Guid? Id, string Name, IReadOnlyList<AssetFolderNode> Children);
