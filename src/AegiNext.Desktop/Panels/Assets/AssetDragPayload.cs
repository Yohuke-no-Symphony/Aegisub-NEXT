using Avalonia.Input;

namespace AegiNext.Desktop.Panels.Assets;

internal sealed record AssetDragPayload(Guid ProjectId, Guid Id, bool IsFolder)
{
    internal static DataFormat<AssetDragPayload> Format { get; } = DataFormat.CreateInProcessFormat<AssetDragPayload>("AegiNext.Asset");
}
